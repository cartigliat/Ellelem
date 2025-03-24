using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the document processing service
    /// </summary>
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStore _vectorStore;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IRagConfigurationService _configService;
        private readonly int _embeddingBatchSize = 15; // Default batch size for embedding generation

        public DocumentProcessingService(
            IDocumentRepository documentRepository,
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            IRagConfigurationService configService,
            RagDiagnosticsService diagnostics)
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                $"Service initialized with settings: ChunkSize={_configService.ChunkSize}, ChunkOverlap={_configService.ChunkOverlap}, EmbeddingBatchSize={_embeddingBatchSize}");
        }

        // Adding back the required interface method, but with simplified implementation
        public Task<Document> LoadFullContentAsync(Document document)
        {
            // Simply return the document as-is since we always have full content now
            return Task.FromResult(document);
        }

        public async Task<Document> ProcessDocumentAsync(Document document)
        {
            _diagnostics.StartOperation("ProcessDocument");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                    $"Processing document: {document.Id}");

                if (document == null)
                {
                    throw new ArgumentNullException(nameof(document));
                }

                // Create chunks
                _diagnostics.StartOperation("DocumentChunking");
                document.Chunks = await ChunkDocumentAsync(document);
                _diagnostics.EndOperation("DocumentChunking");

                _diagnostics.LogDocumentChunking("DocumentProcessingService", document.Id, document.Chunks);

                // If no chunks were created, handle this case
                if (document.Chunks.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                        $"No chunks were created for document: {document.Id}");

                    // Create at least one chunk with the entire content (if it's not too large)
                    if (document.Content.Length <= _configService.ChunkSize * 2)
                    {
                        document.Chunks.Add(new DocumentChunk
                        {
                            DocumentId = document.Id,
                            Content = document.Content.Trim(),
                            ChunkIndex = 0,
                            Source = document.Name
                        });

                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                            "Created a single chunk for the entire document");
                    }
                    else
                    {
                        // For larger documents, create fixed-size chunks as a fallback
                        int chunkCount = (int)Math.Ceiling((double)document.Content.Length / _configService.ChunkSize);
                        for (int i = 0; i < chunkCount; i++)
                        {
                            int startPos = i * _configService.ChunkSize;
                            int length = Math.Min(_configService.ChunkSize, document.Content.Length - startPos);

                            document.Chunks.Add(new DocumentChunk
                            {
                                DocumentId = document.Id,
                                Content = document.Content.Substring(startPos, length).Trim(),
                                ChunkIndex = i,
                                Source = document.Name
                            });
                        }

                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                            $"Created {document.Chunks.Count} fixed-size chunks as fallback");
                    }
                }

                // Generate embeddings for chunks using batch processing
                _diagnostics.StartOperation("GenerateChunkEmbeddings");
                int processedChunks = 0;

                // Process chunks in batches
                for (int i = 0; i < document.Chunks.Count; i += _embeddingBatchSize)
                {
                    // Get current batch (up to batchSize chunks)
                    int currentBatchSize = Math.Min(_embeddingBatchSize, document.Chunks.Count - i);
                    var batch = document.Chunks.GetRange(i, currentBatchSize);

                    // Create tasks for parallel processing
                    var tasks = new Task[batch.Count];

                    for (int j = 0; j < batch.Count; j++)
                    {
                        var chunk = batch[j];
                        tasks[j] = ProcessChunkEmbeddingAsync(chunk);
                    }

                    try
                    {
                        // Wait for all tasks in this batch to complete
                        await Task.WhenAll(tasks);

                        processedChunks += batch.Count;
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                            $"Generated embeddings for {processedChunks}/{document.Chunks.Count} chunks");
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                            $"Failed to process batch starting at index {i}: {ex.Message}");
                    }
                }
                _diagnostics.EndOperation("GenerateChunkEmbeddings");

                // Remove any chunks with empty embeddings
                int originalCount = document.Chunks.Count;
                document.Chunks.RemoveAll(c => c.Embedding == null || c.Embedding.Length == 0);

                if (document.Chunks.Count < originalCount)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                        $"Removed {originalCount - document.Chunks.Count} chunks with empty embeddings");
                }

                document.IsProcessed = document.Chunks.Count > 0;
                document.IsSelected = true; // Auto-select document after processing

                // Save updated document
                await _documentRepository.SaveDocumentAsync(document);

                // Add vectors to store
                _diagnostics.StartOperation("AddVectorsToStore");
                await _vectorStore.AddVectorsAsync(document.Chunks);
                _diagnostics.EndOperation("AddVectorsToStore");

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                    $"Document processed successfully: {document.Id}, {document.Chunks.Count} chunks indexed");

                return document;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                    $"Failed to process document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        // Helper method for processing a single chunk's embedding
        private async Task ProcessChunkEmbeddingAsync(DocumentChunk chunk)
        {
            try
            {
                chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentProcessingService",
                    $"Failed to generate embedding for chunk {chunk.Id}: {ex.Message}");
                chunk.Embedding = new float[0]; // Empty embedding
            }
        }

        public async Task<List<DocumentChunk>> ChunkDocumentAsync(Document document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.Content))
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessingService",
                    $"Empty content for document {document?.Id}");
                return new List<DocumentChunk>();
            }

            // Try to identify document structure for better chunking
            bool hasCodeBlocks = document.Content.Contains("```") ||
                               Regex.IsMatch(document.Content, @"class\s+\w+\s*{");
            bool hasHeaders = Regex.IsMatch(document.Content, @"^#{1,6}\s+.+$", RegexOptions.Multiline);
            bool hasLists = Regex.IsMatch(document.Content, @"^\s*[-*+]\s+.+$", RegexOptions.Multiline);

            _diagnostics.Log(DiagnosticLevel.Debug, "DocumentProcessingService",
                $"Document {document.Id} structure: HasCodeBlocks={hasCodeBlocks}, HasHeaders={hasHeaders}, HasLists={hasLists}");

            // Choose chunking strategy based on content type
            List<DocumentChunk> chunks;

            if (hasCodeBlocks)
            {
                chunks = ChunkCodeDocument(document.Content, document.Id, document.Name);
            }
            else if (hasHeaders)
            {
                chunks = ChunkStructuredDocument(document.Content, document.Id, document.Name);
            }
            else
            {
                chunks = ChunkTextDocument(document.Content, document.Id, document.Name);
            }

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentProcessingService",
                $"Document {document.Id} chunked into {chunks.Count} pieces");

            return await Task.FromResult(chunks);
        }

        // Helper methods for chunking
        private List<DocumentChunk> ChunkTextDocument(string content, string documentId, string source)
        {
            // Split by paragraph first
            var paragraphs = Regex.Split(content, @"\r?\n\r?\n+");

            var chunks = new List<DocumentChunk>();
            var currentChunk = new StringBuilder();
            int chunkIndex = 0;

            foreach (var paragraph in paragraphs)
            {
                string trimmedParagraph = paragraph.Trim();
                if (string.IsNullOrWhiteSpace(trimmedParagraph))
                    continue;

                // If adding this paragraph would exceed chunk size, finalize current chunk
                if (currentChunk.Length > 0 &&
                    currentChunk.Length + trimmedParagraph.Length > _configService.ChunkSize)
                {
                    chunks.Add(new DocumentChunk
                    {
                        DocumentId = documentId,
                        Content = currentChunk.ToString().Trim(),
                        ChunkIndex = chunkIndex++,
                        Source = source
                    });

                    // Start new chunk with overlap
                    var lastChars = currentChunk.ToString();
                    if (lastChars.Length > _configService.ChunkOverlap)
                    {
                        lastChars = lastChars.Substring(lastChars.Length - _configService.ChunkOverlap);
                    }

                    currentChunk.Clear();
                    currentChunk.Append(lastChars);
                }

                // Add paragraph to current chunk
                currentChunk.AppendLine(trimmedParagraph);
                currentChunk.AppendLine();
            }

            // Add final chunk if not empty
            if (currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    DocumentId = documentId,
                    Content = currentChunk.ToString().Trim(),
                    ChunkIndex = chunkIndex,
                    Source = source
                });
            }

            return chunks;
        }

        private List<DocumentChunk> ChunkStructuredDocument(string content, string documentId, string source)
        {
            // Identify headers as splitting points
            var headerMatches = Regex.Matches(content, @"^(#{1,6}\s+.+)$", RegexOptions.Multiline);

            if (headerMatches.Count == 0)
            {
                // Fallback to regular text chunking if no headers found
                return ChunkTextDocument(content, documentId, source);
            }

            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;

            // Process each section (header + content)
            for (int i = 0; i < headerMatches.Count; i++)
            {
                int startPos = headerMatches[i].Index;
                int endPos = (i < headerMatches.Count - 1) ? headerMatches[i + 1].Index : content.Length;

                string section = content.Substring(startPos, endPos - startPos).Trim();

                // Skip empty sections
                if (string.IsNullOrWhiteSpace(section))
                    continue;

                // For very large sections, split them further
                if (section.Length > _configService.ChunkSize * 1.5)
                {
                    // Extract header
                    string header = headerMatches[i].Value;
                    string sectionContent = section.Substring(header.Length).Trim();

                    // Chunk the section content
                    var sectionChunks = ChunkTextDocument(sectionContent, documentId, source);

                    // Add header to each chunk
                    for (int j = 0; j < sectionChunks.Count; j++)
                    {
                        sectionChunks[j].Content = $"{header} (Part {j + 1}/{sectionChunks.Count})\n\n{sectionChunks[j].Content}";
                        sectionChunks[j].ChunkIndex = chunkIndex++;
                    }

                    chunks.AddRange(sectionChunks);
                }
                else
                {
                    chunks.Add(new DocumentChunk
                    {
                        DocumentId = documentId,
                        Content = section,
                        ChunkIndex = chunkIndex++,
                        Source = source
                    });
                }
            }

            return chunks;
        }

        private List<DocumentChunk> ChunkCodeDocument(string content, string documentId, string source)
        {
            // Try to identify classes, functions, and methods
            var codeBlockMatches = Regex.Matches(content, @"```[\s\S]*?```|(?:public|private|protected|internal|static|class|void|function|def)\s+[\w<>]+\s*[\w<>]*\s*\([^)]*\)\s*(?::\s*\w+\s*)?\{");

            if (codeBlockMatches.Count == 0)
            {
                // Fallback to regular text chunking if no code blocks found
                return ChunkTextDocument(content, documentId, source);
            }

            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;

            // Process each code block
            for (int i = 0; i < codeBlockMatches.Count; i++)
            {
                int startPos = codeBlockMatches[i].Index;

                // Find the end of this code block
                int braceCount = 0;
                int endPos = startPos;

                // If it's a markdown code block
                if (content.Substring(startPos).StartsWith("```"))
                {
                    // Find the closing ```
                    int closeBlockPos = content.IndexOf("```", startPos + 3);
                    if (closeBlockPos > 0)
                    {
                        endPos = closeBlockPos + 3; // Include the closing ```
                    }
                    else
                    {
                        // No closing tag found, treat the rest of the document as part of this code block
                        endPos = content.Length;
                    }
                }
                else
                {
                    // It's a code definition (class, method, etc.)
                    // Find the scope end by tracking braces
                    for (int j = startPos; j < content.Length; j++)
                    {
                        if (content[j] == '{')
                        {
                            braceCount++;
                        }
                        else if (content[j] == '}')
                        {
                            braceCount--;
                            if (braceCount == 0)
                            {
                                endPos = j + 1; // Include the closing brace
                                break;
                            }
                        }
                    }

                    // If we couldn't find the end, use the next block start or end of document
                    if (endPos == startPos)
                    {
                        endPos = (i < codeBlockMatches.Count - 1) ? codeBlockMatches[i + 1].Index : content.Length;
                    }
                }

                string codeBlock = content.Substring(startPos, endPos - startPos).Trim();

                // Skip empty blocks
                if (string.IsNullOrWhiteSpace(codeBlock))
                    continue;

                // For very large code blocks, split them
                if (codeBlock.Length > _configService.ChunkSize * 1.5)
                {
                    // For code, we'll preserve lines to avoid breaking syntax
                    string[] lines = codeBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    var currentChunk = new StringBuilder();
                    foreach (var line in lines)
                    {
                        // If adding this line would exceed chunk size, finalize current chunk
                        if (currentChunk.Length > 0 &&
                            currentChunk.Length + line.Length > _configService.ChunkSize)
                        {
                            chunks.Add(new DocumentChunk
                            {
                                DocumentId = documentId,
                                Content = currentChunk.ToString().Trim(),
                                ChunkIndex = chunkIndex++,
                                Source = source
                            });

                            currentChunk.Clear();
                        }

                        currentChunk.AppendLine(line);
                    }

                    // Add final chunk if not empty
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(new DocumentChunk
                        {
                            DocumentId = documentId,
                            Content = currentChunk.ToString().Trim(),
                            ChunkIndex = chunkIndex++,
                            Source = source
                        });
                    }
                }
                else
                {
                    chunks.Add(new DocumentChunk
                    {
                        DocumentId = documentId,
                        Content = codeBlock,
                        ChunkIndex = chunkIndex++,
                        Source = source
                    });
                }
            }

            return chunks;
        }
    }
}