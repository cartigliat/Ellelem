// ollamidesk/RAG/Services/Implementations/HierarchicalChunkingStrategy.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services; // Assuming IRagConfigurationService is here
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // For StructuredDocument, DocumentElement

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implements chunking based on the hierarchical structure (headings, sections)
    /// extracted by a document processor (e.g., PdfStructureExtractor, WordStructureExtractor).
    /// Prepends the hierarchical context (SectionPath) to each chunk.
    /// </summary>
    public class HierarchicalChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;

        // Constants for context formatting
        private const string ContextPrefix = "Context: ";
        private const string ContextSuffix = "\n\n"; // Added newline for separation

        public HierarchicalChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Determines if this strategy can chunk the document based on the availability of structured data.
        /// </summary>
        /// <param name="document">The document model (not directly used here).</param>
        /// <param name="structuredDoc">The structured document information extracted previously.</param>
        /// <returns>True if structuredDoc is not null and contains elements, false otherwise.</returns>
        public bool CanChunk(Document document, StructuredDocument? structuredDoc)
        {
            bool canChunk = structuredDoc != null && structuredDoc.Elements.Any();
            _diagnostics.Log(DiagnosticLevel.Debug, "HierarchicalChunkingStrategy", $"CanChunk called for doc {document.Id}. StructuredDoc provided: {structuredDoc != null}. Elements count: {structuredDoc?.Elements?.Count ?? 0}. Result: {canChunk}");
            return canChunk;
        }

        /// <summary>
        /// Chunks the document based on its structured elements, prepending hierarchical context.
        /// </summary>
        /// <param name="document">The original document model (used for ID and Name).</param>
        /// <param name="structuredDoc">The structured document containing elements with SectionPaths.</param>
        /// <returns>A list of DocumentChunk objects with prepended context.</returns>
        public List<DocumentChunk> Chunk(Document document, StructuredDocument? structuredDoc)
        {
            _diagnostics.StartOperation("HierarchicalChunkingStrategy.Chunk");
            _diagnostics.Log(DiagnosticLevel.Info, "HierarchicalChunkingStrategy", $"Executing hierarchical chunking for document: {document.Id}");

            // Ensure structured document is provided (should be guaranteed by CanChunk, but double-check)
            if (structuredDoc == null || structuredDoc.Elements.Count == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "HierarchicalChunkingStrategy", "No structured elements provided to chunk, returning empty list.");
                _diagnostics.EndOperation("HierarchicalChunkingStrategy.Chunk");
                return new List<DocumentChunk>();
            }

            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;
            int maxChunkSize = _configService.ChunkSize;
            int overlap = _configService.ChunkOverlap; // Get overlap setting

            _diagnostics.Log(DiagnosticLevel.Debug, "HierarchicalChunkingStrategy", $"Using MaxChunkSize: {maxChunkSize}, Overlap: {overlap}");

            foreach (var element in structuredDoc.Elements)
            {
                if (string.IsNullOrWhiteSpace(element.Text)) continue;

                // 1. Construct Context Header
                string contextHeader = string.IsNullOrWhiteSpace(element.SectionPath)
                                         ? ""
                                         : $"{ContextPrefix}{element.SectionPath}{ContextSuffix}";
                int contextHeaderLength = contextHeader.Length;

                // 2. Calculate available content length
                int maxContentLength = maxChunkSize - contextHeaderLength;

                // Check if header itself is too long
                if (maxContentLength <= 10) // Reserve at least 10 chars for actual content
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "HierarchicalChunkingStrategy", $"Context header for path '{element.SectionPath}' is too long ({contextHeaderLength} chars) relative to ChunkSize ({maxChunkSize}). Skipping element.");
                    continue;
                }

                string elementText = element.Text;

                // 3. Check if element fits in one chunk (with header)
                if (elementText.Length <= maxContentLength)
                {
                    chunks.Add(CreateChunk(
                        document.Id,
                        contextHeader + elementText, // Prepend header
                        chunkIndex++,
                        document.Name,
                        element.Type.ToString(),
                        element.SectionPath,
                        element.HeadingLevel));
                    _diagnostics.Log(DiagnosticLevel.Debug, "HierarchicalChunkingStrategy", $"Added single chunk for element type {element.Type} at path '{element.SectionPath}', length {elementText.Length + contextHeaderLength}.");
                }
                else // 4. Element is too large, needs splitting
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "HierarchicalChunkingStrategy", $"Element type {element.Type} at path '{element.SectionPath}' is too large ({elementText.Length} chars vs max content {maxContentLength}). Splitting...");

                    // --- Splitting Logic (using simple character-based split with overlap) ---
                    // Consider replacing this with sentence splitting or a more sophisticated method if needed.
                    int currentPosition = 0;
                    int subChunkCount = 0;

                    while (currentPosition < elementText.Length)
                    {
                        int length = Math.Min(maxContentLength, elementText.Length - currentPosition);
                        string contentPart = elementText.Substring(currentPosition, length);

                        // Add the chunk (prepending the SAME context header)
                        chunks.Add(CreateChunk(
                            document.Id,
                            contextHeader + contentPart, // Prepend header to each part
                            chunkIndex++,
                            document.Name,
                            $"{element.Type}Part", // Indicate it's part of a larger element
                            element.SectionPath,
                            element.HeadingLevel));
                        subChunkCount++;

                        // Calculate step for next chunk start, considering overlap
                        int step = maxContentLength - overlap;
                        if (step <= 0) // Prevent infinite loop if overlap >= maxContentLength
                        {
                            step = Math.Max(1, maxContentLength / 2); // Ensure progress
                            _diagnostics.Log(DiagnosticLevel.Warning, "HierarchicalChunkingStrategy", $"Overlap ({overlap}) is too large for max content length ({maxContentLength}). Using step {step} to avoid stall.");
                        }

                        currentPosition += step;

                        // Safety break if step is somehow non-positive
                        if (step <= 0)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "HierarchicalChunkingStrategy", $"Splitting step became non-positive ({step}), breaking loop to prevent infinite execution. Element Path: {element.SectionPath}");
                            break;
                        }
                    }
                    _diagnostics.Log(DiagnosticLevel.Debug, "HierarchicalChunkingStrategy", $"Split large element into {subChunkCount} sub-chunks.");
                    // --- End Splitting Logic ---
                }
            }

            _diagnostics.Log(DiagnosticLevel.Info, "HierarchicalChunkingStrategy", $"Finished hierarchical chunking for document {document.Id}. Total chunks created: {chunks.Count}.");
            _diagnostics.EndOperation("HierarchicalChunkingStrategy.Chunk");
            return chunks;
        }

        /// <summary>
        /// Helper method to create a DocumentChunk object.
        /// </summary>
        private DocumentChunk CreateChunk(string documentId, string content, int index, string source, string type, string? path, int? headingLevel)
        {
            return new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = content, // Content now includes prepended context
                ChunkIndex = index,
                Source = source,
                ChunkType = type,
                // Assign structural metadata from the DocumentElement
                SectionPath = path ?? string.Empty,
                HeadingLevel = headingLevel
                // You could add other metadata from element.Metadata here if needed
            };
        }
    }
}