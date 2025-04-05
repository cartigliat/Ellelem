// ollamidesk/RAG/Services/Implementations/DocumentProcessingService.cs
// CORRECTED VERSION - Removed invalid ProcessChunkEmbeddingAsync call from fallback logic
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations;
using ollamidesk.RAG.Exceptions;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the document processing service
    /// </summary>
    public class DocumentProcessingService : IDocumentProcessingService, IDisposable
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStore _vectorStore;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IRagConfigurationService _configService;
        private readonly IChunkingService _chunkingService;
        private readonly DocumentProcessorFactory _documentProcessorFactory;
        private readonly int _embeddingBatchSize;

        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        private bool _disposedValue;

        public DocumentProcessingService(
            IDocumentRepository documentRepository,
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            IRagConfigurationService configService,
            RagDiagnosticsService diagnostics,
            IChunkingService chunkingService,
            DocumentProcessorFactory documentProcessorFactory)
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
            _documentProcessorFactory = documentProcessorFactory ?? throw new ArgumentNullException(nameof(documentProcessorFactory));

            _embeddingBatchSize = _configService.EmbeddingBatchSize;

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
               $"Service initialized with settings: EmbeddingBatchSize={_embeddingBatchSize}");
        }

        public Task<Document> LoadFullContentAsync(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"LoadFullContentAsync called for {document.Id}. Returning document as content is pre-loaded.");
            return Task.FromResult(document);
        }

        public async Task<List<DocumentChunk>> ChunkDocumentAsync(Document document)
        {
            return await _chunkingService.ChunkDocumentAsync(document, null).ConfigureAwait(false);
        }

        public async Task<Document> ProcessDocumentAsync(Document document)
        {
            bool lockAcquired = false;
            StructuredDocument? structuredDoc = null;

            try
            {
                await _processingLock.WaitAsync().ConfigureAwait(false);
                lockAcquired = true;

                _diagnostics.StartOperation("ProcessDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                   $"Processing document: {document.Id} ('{document.Name}')");

                if (document == null) throw new ArgumentNullException(nameof(document));
                if (string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath))
                {
                    throw new FileNotFoundException($"Document file path is invalid or file does not exist for ID: {document.Id}", document.FilePath);
                }

                // --- Ensure Content is Loaded (if necessary) ---
                if (string.IsNullOrWhiteSpace(document.Content))
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService", $"Document {document.Id} content is empty. Further processing might fail if content is required.");
                    // If content loading here is needed, implement it.
                }

                // --- Structure Extraction Step ---
                _diagnostics.StartOperation("ExtractStructure");
                try
                {
                    string extension = Path.GetExtension(document.FilePath).ToLowerInvariant();
                    var processor = _documentProcessorFactory.GetProcessor(extension);

                    if (processor.SupportsStructuredExtraction)
                    {
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Attempting structure extraction for {document.Id} using {processor.GetType().Name}");
                        structuredDoc = await processor.ExtractStructuredContentAsync(document.FilePath).ConfigureAwait(false);
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Structure extraction completed. Found {structuredDoc?.Elements?.Count ?? 0} elements.");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Processor {processor.GetType().Name} does not support structured extraction for {document.Id}.");
                    }
                }
                catch (NotSupportedException nse)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService", $"Structure extraction skipped: {nse.Message}");
                    structuredDoc = null;
                }
                catch (Exception structEx)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"Structure extraction failed for document {document.Id}: {structEx.Message}");
                    structuredDoc = null;
                }
                finally
                {
                    _diagnostics.EndOperation("ExtractStructure");
                }

                // --- Chunking ---
                _diagnostics.StartOperation("DocumentChunking");
                try
                {
                    document.Chunks = await _chunkingService.ChunkDocumentAsync(document, structuredDoc).ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Chunking service returned {document.Chunks?.Count ?? 0} chunks for {document.Id}.");
                }
                catch (Exception chunkEx)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"Error during chunking for document {document.Id}: {chunkEx.Message}");
                    document.IsProcessed = false;
                    document.Chunks = new List<DocumentChunk>();
                    await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    _diagnostics.EndOperation("DocumentChunking");
                }

                // --- Fallback Chunking ---
                if (document.Chunks.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                       $"No chunks were created by ChunkingService for document: {document.Id}. Attempting fallback.");

                    if (!string.IsNullOrWhiteSpace(document.Content) && document.Content.Length <= _configService.ChunkSize * 2)
                    {
                        document.Chunks.Add(new DocumentChunk
                        {
                            Id = Guid.NewGuid().ToString(),
                            DocumentId = document.Id,
                            Content = document.Content.Trim(),
                            ChunkIndex = 0,
                            Source = document.Name,
                            ChunkType = "FullDocument"
                        });
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", "Fallback: Created a single chunk for the entire document");
                        // <<< NO embedding call needed here >>>
                    }
                    else if (!string.IsNullOrWhiteSpace(document.Content))
                    {
                        int chunkCount = (int)Math.Ceiling((double)document.Content.Length / _configService.ChunkSize);
                        for (int i = 0; i < chunkCount; i++)
                        {
                            int startPos = i * _configService.ChunkSize;
                            int length = Math.Min(_configService.ChunkSize, document.Content.Length - startPos);
                            string chunkContent = document.Content.Substring(startPos, length).Trim();
                            if (!string.IsNullOrWhiteSpace(chunkContent))
                            {
                                document.Chunks.Add(new DocumentChunk
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    DocumentId = document.Id,
                                    Content = chunkContent,
                                    ChunkIndex = i,
                                    Source = document.Name,
                                    ChunkType = "FixedSizeFallback"
                                });
                                // <<< NO embedding call needed here >>>
                            }
                        }
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Fallback: Created {document.Chunks.Count} fixed-size chunks.");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService", $"Document {document.Id} content is empty or whitespace, cannot create fallback chunks.");
                    }
                    // --- ERROR REMOVED: The line causing the error was likely here ---
                    // Remove any stray calls like: await ProcessChunkEmbeddingAsync(chunk);
                }

                // --- Embedding Generation ---
                if (document.Chunks.Count > 0)
                {
                    _diagnostics.StartOperation("GenerateChunkEmbeddings");
                    int processedChunks = 0;
                    // Process chunks in batches using the helper method
                    for (int i = 0; i < document.Chunks.Count; i += _embeddingBatchSize)
                    {
                        int currentBatchSize = Math.Min(_embeddingBatchSize, document.Chunks.Count - i);
                        var batch = document.Chunks.GetRange(i, currentBatchSize);
                        var batchTasks = new List<Task>();

                        foreach (var currentChunk in batch) // Use a different variable name here ('currentChunk')
                        {
                            batchTasks.Add(ProcessChunkEmbeddingAsync(currentChunk)); // Pass the loop variable
                        }

                        try
                        {
                            await Task.WhenAll(batchTasks).ConfigureAwait(false);
                            processedChunks += batch.Count;
                            _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService",
                               $"Embedding batch (size {batch.Count}) completed. Processed {processedChunks}/{document.Chunks.Count} chunks.");
                        }
                        catch (AggregateException aggEx)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"Error(s) in embedding batch starting at index {i}:");
                            foreach (var innerEx in aggEx.InnerExceptions)
                            {
                                _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"  - {innerEx.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                               $"Unexpected error during embedding batch starting at index {i}: {ex.Message}");
                        }
                    }
                    _diagnostics.EndOperation("GenerateChunkEmbeddings");

                    // Filtering and Saving (remains the same)
                    int originalCount = document.Chunks.Count;
                    document.Chunks.RemoveAll(c => c.Embedding == null || c.Embedding.Length == 0);
                    if (document.Chunks.Count < originalCount)
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                           $"Removed {originalCount - document.Chunks.Count} chunks with failed embeddings for document {document.Id}.");
                    }
                }

                // --- Final Status Update & Save ---
                document.IsProcessed = document.Chunks.Count > 0;
                document.IsSelected = true;
                await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false);

                // --- Add Vectors to Store ---
                if (document.IsProcessed)
                {
                    _diagnostics.StartOperation("AddVectorsToStore");
                    try
                    {
                        await _vectorStore.AddVectorsAsync(document.Chunks).ConfigureAwait(false);
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Added {document.Chunks.Count} vectors to store for document {document.Id}");
                    }
                    catch (Exception vsEx)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"Failed to add vectors to store for document {document.Id}: {vsEx.Message}");
                    }
                    finally
                    {
                        _diagnostics.EndOperation("AddVectorsToStore");
                    }
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService", $"Document {document.Id} marked as not processed (no valid chunks). Skipping vector store update.");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                    $"Document processing finished for: {document.Id}, Processed: {document.IsProcessed}, Valid Chunks: {document.Chunks.Count}");

                return document;
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Critical, "DocumentProcessingService",
                  $"Unhandled failure during processing document {document?.Id}: {ex.Message}{Environment.NewLine}Stack Trace: {ex.StackTrace}");
                if (document != null)
                {
                    document.IsProcessed = false;
                    document.Chunks = new List<DocumentChunk>();
                    try { await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false); } catch { /* Ignore save error */ }
                }
                throw new DocumentProcessingException($"Unexpected error processing document {document?.Id}.", ex);
            }
            finally
            {
                if (lockAcquired) _processingLock.Release();
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        // Helper method ProcessChunkEmbeddingAsync remains unchanged
        private async Task ProcessChunkEmbeddingAsync(DocumentChunk chunk) // Parameter name is 'chunk'
        {
            try
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"Generating embedding for chunk {chunk.Id} (Index: {chunk.ChunkIndex}) via EmbeddingService.");
                // Use the parameter 'chunk' here
                chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"Embedding generated for chunk {chunk.Id}, length: {chunk.Embedding?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                   $"EmbeddingService failed for chunk {chunk.Id} (Index: {chunk.ChunkIndex}): {ex.Message}");
                chunk.Embedding = Array.Empty<float>();
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _processingLock?.Dispose();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}