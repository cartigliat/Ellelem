// ollamidesk/RAG/Services/Implementations/DocumentProcessingService.cs
// Corrected version - Removes embedding semaphore and appSettings usage
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration; // Keep for RagSettings via IRagConfigurationService
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

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
        private readonly IChunkingService _chunkingService; // <-- ADDED
        private readonly int _embeddingBatchSize; // Store batch size from config

        // Use a single lock for processing one document at a time
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        // REMOVED: _embeddingSemaphore - Embedding concurrency is handled within OllamaEmbeddingService

        private bool _disposedValue;

        public DocumentProcessingService(
            IDocumentRepository documentRepository,
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            IRagConfigurationService configService,
            RagDiagnosticsService diagnostics,
            IChunkingService chunkingService) // <-- ADDED dependency
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService)); // <-- ADDED initialization

            _embeddingBatchSize = _configService.EmbeddingBatchSize; // Get batch size from config

            // REMOVED lines trying to access appSettings and initialize _embeddingSemaphore
            // int maxConcurrentEmbeddings = appSettings.Ollama.MaxConcurrentRequests > 0 ? appSettings.Ollama.MaxConcurrentRequests : 5;
            // _embeddingSemaphore = new SemaphoreSlim(maxConcurrentEmbeddings, maxConcurrentEmbeddings);

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                $"Service initialized with settings: EmbeddingBatchSize={_embeddingBatchSize}");
            // REMOVED MaxConcurrentEmbeddings from log message
        }

        public Task<Document> LoadFullContentAsync(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"LoadFullContentAsync called for {document.Id}. Returning document as content is pre-loaded.");
            // Return non-null document wrapped in a Task
            return Task.FromResult(document);
        }

        // Interface method - now delegates chunking to IChunkingService
        public async Task<List<DocumentChunk>> ChunkDocumentAsync(Document document)
        {
            // Delegate directly to the injected chunking service
            return await _chunkingService.ChunkDocumentAsync(document).ConfigureAwait(false);
        }


        public async Task<Document> ProcessDocumentAsync(Document document)
        {
            bool lockAcquired = false;

            try
            {
                // Use the semaphore to ensure only one document is processed at a time
                await _processingLock.WaitAsync().ConfigureAwait(false);
                lockAcquired = true;

                _diagnostics.StartOperation("ProcessDocument");

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                    $"Processing document: {document.Id}");

                if (document == null)
                {
                    throw new ArgumentNullException(nameof(document));
                }

                // --- Chunking ---
                _diagnostics.StartOperation("DocumentChunking");
                try
                {
                    // Use the interface method which now delegates
                    document.Chunks = await ChunkDocumentAsync(document).ConfigureAwait(false);
                }
                catch (Exception chunkEx)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService", $"Error during chunking for document {document.Id}: {chunkEx.Message}");
                    document.IsProcessed = false; // Mark as not processed
                    document.Chunks = new List<DocumentChunk>(); // Ensure chunks list is empty
                    // Save the unprocessed state
                    await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false);
                    throw; // Rethrow to indicate processing failure
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

                    // (Fallback logic remains the same as previous corrected version)
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
                            }
                        }
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService", $"Fallback: Created {document.Chunks.Count} fixed-size chunks.");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService", $"Document {document.Id} content is empty or whitespace, cannot create fallback chunks.");
                    }
                }

                // --- Embedding Generation ---
                if (document.Chunks.Count > 0)
                {
                    _diagnostics.StartOperation("GenerateChunkEmbeddings");
                    int processedChunks = 0;
                    var embeddingTasks = new List<Task>();

                    // Process chunks in batches
                    for (int i = 0; i < document.Chunks.Count; i += _embeddingBatchSize)
                    {
                        int currentBatchSize = Math.Min(_embeddingBatchSize, document.Chunks.Count - i);
                        var batch = document.Chunks.GetRange(i, currentBatchSize);
                        var batchTasks = new List<Task>(); // Tasks for the current batch

                        foreach (var chunk in batch)
                        {
                            batchTasks.Add(ProcessChunkEmbeddingAsync(chunk)); // Uses helper below
                        }

                        try
                        {
                            // Wait for the current batch to complete
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
                            // Logged errors, continue processing other batches. Failed chunks will have empty embeddings.
                        }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                               $"Unexpected error during embedding batch starting at index {i}: {ex.Message}");
                            // Logged error, continue processing other batches.
                        }
                    }
                    _diagnostics.EndOperation("GenerateChunkEmbeddings");

                    // --- Filtering and Saving ---
                    int originalCount = document.Chunks.Count;
                    document.Chunks.RemoveAll(c => c.Embedding == null || c.Embedding.Length == 0);
                    if (document.Chunks.Count < originalCount)
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                            $"Removed {originalCount - document.Chunks.Count} chunks with failed embeddings for document {document.Id}.");
                    }
                } // End if(document.Chunks.Count > 0)

                // --- Final Status Update & Save ---
                document.IsProcessed = document.Chunks.Count > 0; // Mark processed only if valid chunks exist
                document.IsSelected = true; // Auto-select after attempt

                await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false);

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
                        // Optionally: Mark document as partially processed or needing re-indexing
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
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Critical, "DocumentProcessingService", // Elevated level for top-level failure
                   $"Unhandled failure during processing document {document?.Id}: {ex.Message}{Environment.NewLine}Stack Trace: {ex.StackTrace}");
                // Ensure document state reflects failure if possible
                if (document != null)
                {
                    document.IsProcessed = false;
                    document.Chunks = new List<DocumentChunk>(); // Clear potentially partial chunks
                    try { await _documentRepository.SaveDocumentAsync(document).ConfigureAwait(false); } catch { /* Ignore save error */ }
                }
                throw; // Rethrow to signal failure to the caller
            }
            finally
            {
                if (lockAcquired) _processingLock.Release();
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        // Helper method for processing a single chunk's embedding (relies on IEmbeddingService's internal concurrency)
        private async Task ProcessChunkEmbeddingAsync(DocumentChunk chunk)
        {
            // REMOVED semaphore acquire/release here
            try
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"Generating embedding for chunk {chunk.Id} (Index: {chunk.ChunkIndex}) via EmbeddingService.");
                chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService", $"Embedding generated for chunk {chunk.Id}, length: {chunk.Embedding?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                    $"EmbeddingService failed for chunk {chunk.Id} (Index: {chunk.ChunkIndex}): {ex.Message}");
                chunk.Embedding = Array.Empty<float>(); // Mark as failed
            }
            finally
            {
                // REMOVED semaphore release
            }
        }


        // --- REMOVED ChunkTextDocument, ChunkStructuredDocument, ChunkCodeDocument methods ---


        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue) // Corrected: removed 'public Task<Document> LoadFullContentAsync;'
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _processingLock?.Dispose();
                    // REMOVED: _embeddingSemaphore?.Dispose();
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