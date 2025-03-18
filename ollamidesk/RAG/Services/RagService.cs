using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services
{
    public class RagService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStore _vectorStore;
        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly int _maxRetrievedChunks;
        private readonly float _minSimilarityScore;
        private readonly RagDiagnosticsService _diagnostics;

        public RagService(
            IDocumentRepository documentRepository,
            IEmbeddingService embeddingService,
            IVectorStore vectorStore,
            RagSettings ragSettings,
            RagDiagnosticsService diagnostics)
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (ragSettings == null)
                throw new ArgumentNullException(nameof(ragSettings));

            _chunkSize = ragSettings.ChunkSize;
            _chunkOverlap = ragSettings.ChunkOverlap;
            _maxRetrievedChunks = ragSettings.MaxRetrievedChunks;
            _minSimilarityScore = ragSettings.MinSimilarityScore;

            _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                $"Service initialized with settings: ChunkSize={_chunkSize}, ChunkOverlap={_chunkOverlap}, " +
                $"MaxRetrievedChunks={_maxRetrievedChunks}, MinSimilarityScore={_minSimilarityScore}");
        }

        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            _diagnostics.StartOperation("GetAllDocuments");

            try
            {
                var documents = await _documentRepository.GetAllDocumentsAsync();
                _diagnostics.Log(DiagnosticLevel.Info, "RagService", $"Retrieved {documents.Count} documents");
                return documents;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService", $"Failed to get all documents: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("GetAllDocuments");
            }
        }

        public async Task<Document> AddDocumentAsync(string filePath)
        {
            _diagnostics.StartOperation("AddDocument");

            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "RagService", $"Adding document: {filePath}");

                // Try to determine file type and read accordingly
                string content;
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                switch (extension)
                {
                    case ".txt":
                    case ".md":
                    case ".cs":
                    case ".json":
                    case ".xml":
                    case ".html":
                    case ".htm":
                    case ".css":
                    case ".js":
                    case ".ts":
                    case ".py":
                    case ".java":
                    case ".c":
                    case ".cpp":
                    case ".h":
                    case ".hpp":
                        content = await File.ReadAllTextAsync(filePath);
                        break;
                    case ".pdf":
                        // For PDF, you would need a proper PDF reader
                        // For now, treat as plain text but log the need for PDF handling
                        _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                            "PDF handling is not implemented, treating as text");
                        content = await File.ReadAllTextAsync(filePath);
                        break;
                    default:
                        _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                            $"Unknown file type: {extension}, treating as text");
                        content = await File.ReadAllTextAsync(filePath);
                        break;
                }

                string name = Path.GetFileName(filePath);

                var document = new Document
                {
                    Name = name,
                    FilePath = filePath,
                    Content = content,
                    IsProcessed = false,
                    IsSelected = false // Initialize as not selected
                };

                await _documentRepository.SaveDocumentAsync(document);
                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Document added with ID: {document.Id}, size: {content.Length} characters");

                return document;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService", $"Failed to add document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("AddDocument");
            }
        }

        public async Task<Document> ProcessDocumentAsync(string documentId)
        {
            _diagnostics.StartOperation("ProcessDocument");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "RagService", $"Processing document: {documentId}");

                var document = await _documentRepository.GetDocumentByIdAsync(documentId);
                if (document == null)
                {
                    throw new ArgumentException($"Document not found: {documentId}");
                }

                // Create chunks
                _diagnostics.StartOperation("DocumentChunking");
                document.Chunks = ChunkDocument(document.Content, document.Id, document.Name);
                _diagnostics.EndOperation("DocumentChunking");

                _diagnostics.LogDocumentChunking("RagService", document.Id, document.Chunks);

                // If no chunks were created, handle this case
                if (document.Chunks.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                        $"No chunks were created for document: {documentId}");

                    // Create at least one chunk with the entire content (if it's not too large)
                    if (document.Content.Length <= _chunkSize * 2)
                    {
                        document.Chunks.Add(new DocumentChunk
                        {
                            DocumentId = document.Id,
                            Content = document.Content.Trim(),
                            ChunkIndex = 0,
                            Source = document.Name
                        });

                        _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                            "Created a single chunk for the entire document");
                    }
                    else
                    {
                        // For larger documents, create fixed-size chunks as a fallback
                        int chunkCount = (int)Math.Ceiling((double)document.Content.Length / _chunkSize);
                        for (int i = 0; i < chunkCount; i++)
                        {
                            int startPos = i * _chunkSize;
                            int length = Math.Min(_chunkSize, document.Content.Length - startPos);

                            document.Chunks.Add(new DocumentChunk
                            {
                                DocumentId = document.Id,
                                Content = document.Content.Substring(startPos, length).Trim(),
                                ChunkIndex = i,
                                Source = document.Name
                            });
                        }

                        _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                            $"Created {document.Chunks.Count} fixed-size chunks as fallback");
                    }
                }

                // Generate embeddings for each chunk
                _diagnostics.StartOperation("GenerateChunkEmbeddings");
                int processedChunks = 0;
                foreach (var chunk in document.Chunks)
                {
                    try
                    {
                        chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
                        processedChunks++;

                        if (processedChunks % 5 == 0)
                        {
                            _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                                $"Generated embeddings for {processedChunks}/{document.Chunks.Count} chunks");
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "RagService",
                            $"Failed to generate embedding for chunk {chunk.Id}: {ex.Message}");

                        // Skip this chunk rather than failing the entire process
                        chunk.Embedding = new float[0]; // Empty embedding
                    }
                }
                _diagnostics.EndOperation("GenerateChunkEmbeddings");

                // Remove any chunks with empty embeddings
                int originalCount = document.Chunks.Count;
                document.Chunks = document.Chunks.Where(c => c.Embedding != null && c.Embedding.Length > 0).ToList();

                if (document.Chunks.Count < originalCount)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
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

                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Document processed successfully: {document.Id}, {document.Chunks.Count} chunks indexed");

                return document;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService", $"Failed to process document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        public async Task<(string augmentedPrompt, List<DocumentChunk> sources)> GenerateAugmentedPromptAsync(
            string query, List<string> selectedDocumentIds)
        {
            _diagnostics.StartOperation("GenerateAugmentedPrompt");

            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new ArgumentException("Query cannot be empty", nameof(query));
                }

                if (selectedDocumentIds == null || selectedDocumentIds.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagService", "No documents selected for retrieval");
                    return (query, new List<DocumentChunk>());
                }

                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Generating augmented prompt for query: \"{(query.Length > 50 ? query.Substring(0, 47) + "..." : query)}\"");
                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Selected document IDs: {string.Join(", ", selectedDocumentIds)}");

                // Generate embedding for query
                _diagnostics.StartOperation("QueryEmbeddingGeneration");
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                _diagnostics.EndOperation("QueryEmbeddingGeneration");

                // Search for similar chunks
                _diagnostics.StartOperation("VectorSearch");
                var searchResults = await _vectorStore.SearchAsync(queryEmbedding, _maxRetrievedChunks * 2); // Get more than needed
                _diagnostics.EndOperation("VectorSearch");

                // Filter by selected documents
                searchResults = searchResults
                    .Where(r => selectedDocumentIds.Contains(r.Chunk.DocumentId))
                    .Where(r => r.Score >= _minSimilarityScore) // Filter out low similarity scores
                    .Take(_maxRetrievedChunks)
                    .ToList();

                // Log the retrieved chunks and their scores
                _diagnostics.LogRetrievedChunks("RagService", query, searchResults);

                if (searchResults.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                        "No relevant chunks found for the query");
                    return (query, new List<DocumentChunk>());
                }

                // Build augmented prompt
                _diagnostics.StartOperation("BuildAugmentedPrompt");
                var promptBuilder = new StringBuilder();
                promptBuilder.AppendLine("You are an AI assistant using information from documents to answer questions. Use ONLY the following context information to answer the query at the end. If you don't know the answer based on the provided context, say you don't have enough information, but try to be helpful by suggesting what might be relevant.");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Context information:");
                promptBuilder.AppendLine();

                var sources = new List<DocumentChunk>();

                foreach (var (chunk, score) in searchResults)
                {
                    promptBuilder.AppendLine($"--- Begin Document Chunk from {chunk.Source} ---");
                    promptBuilder.AppendLine(chunk.Content);
                    promptBuilder.AppendLine($"--- End Document Chunk ---");
                    promptBuilder.AppendLine();

                    sources.Add(chunk);
                }

                promptBuilder.AppendLine("Query: " + query);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Answer: ");

                string augmentedPrompt = promptBuilder.ToString();
                _diagnostics.EndOperation("BuildAugmentedPrompt");

                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Generated augmented prompt with {sources.Count} chunks (total length: {augmentedPrompt.Length} chars)");

                return (augmentedPrompt, sources);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService",
                    $"Failed to generate augmented prompt: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("GenerateAugmentedPrompt");
            }
        }

        public async Task DeleteDocumentAsync(string documentId)
        {
            _diagnostics.StartOperation("DeleteDocument");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "RagService", $"Deleting document: {documentId}");

                // Remove from vector store
                await _vectorStore.RemoveVectorsAsync(documentId);

                // Remove from repository
                await _documentRepository.DeleteDocumentAsync(documentId);

                _diagnostics.Log(DiagnosticLevel.Info, "RagService", $"Document deleted: {documentId}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService", $"Failed to delete document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("DeleteDocument");
            }
        }

        // Update document selection state
        public async Task UpdateDocumentSelectionAsync(string documentId, bool isSelected)
        {
            try
            {
                var document = await _documentRepository.GetDocumentByIdAsync(documentId);
                if (document == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                        $"Document not found for selection update: {documentId}");
                    return;
                }

                document.IsSelected = isSelected;
                await _documentRepository.SaveDocumentAsync(document);

                _diagnostics.Log(DiagnosticLevel.Info, "RagService",
                    $"Updated selection state for document {documentId} to {isSelected}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagService",
                    $"Failed to update document selection: {ex.Message}");
                throw;
            }
        }

        private List<DocumentChunk> ChunkDocument(string content, string documentId, string source)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "RagService",
                    $"Empty content for document {documentId}");
                return new List<DocumentChunk>();
            }

            // Try to identify document structure for better chunking
            bool hasCodeBlocks = content.Contains("```") || Regex.IsMatch(content, @"class\s+\w+\s*{");
            bool hasHeaders = Regex.IsMatch(content, @"^#{1,6}\s+.+$", RegexOptions.Multiline);
            bool hasLists = Regex.IsMatch(content, @"^\s*[-*+]\s+.+$", RegexOptions.Multiline);

            _diagnostics.Log(DiagnosticLevel.Debug, "RagService",
                $"Document {documentId} structure: HasCodeBlocks={hasCodeBlocks}, HasHeaders={hasHeaders}, HasLists={hasLists}");

            // Choose chunking strategy based on content type
            if (hasCodeBlocks)
            {
                return ChunkCodeDocument(content, documentId, source);
            }
            else if (hasHeaders)
            {
                return ChunkStructuredDocument(content, documentId, source);
            }
            else
            {
                return ChunkTextDocument(content, documentId, source);
            }
        }

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
                    currentChunk.Length + trimmedParagraph.Length > _chunkSize)
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
                    if (lastChars.Length > _chunkOverlap)
                    {
                        lastChars = lastChars.Substring(lastChars.Length - _chunkOverlap);
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
            // Implementation details unchanged - just returning the chunking method
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
                if (section.Length > _chunkSize * 1.5)
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
            // Implementation details unchanged - just returning the chunking method
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
                if (codeBlock.Length > _chunkSize * 1.5)
                {
                    // For code, we'll preserve lines to avoid breaking syntax
                    string[] lines = codeBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    var currentChunk = new StringBuilder();
                    foreach (var line in lines)
                    {
                        // If adding this line would exceed chunk size, finalize current chunk
                        if (currentChunk.Length > 0 &&
                            currentChunk.Length + line.Length > _chunkSize)
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