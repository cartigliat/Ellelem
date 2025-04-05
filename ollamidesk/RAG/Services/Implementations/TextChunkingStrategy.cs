// ollamidesk/RAG/Services/Implementations/TextChunkingStrategy.cs
// MODIFIED VERSION - Updated signatures to match IChunkingStrategy
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // <-- ADDED for StructuredDocument

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Strategy for chunking plain text documents based on paragraphs and size limits.
    /// Serves as the default fallback strategy.
    /// </summary>
    public class TextChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;

        public TextChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Determines if this strategy can chunk the document. As the default fallback, it always can.
        /// </summary>
        /// <param name="document">The document to check.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>Always true.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public bool CanChunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy serves as the default/fallback, it doesn't depend on structuredDoc.
            return true;
        }

        /// <summary>
        /// Chunks the document based purely on text content (paragraphs and size limits).
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>A list of document chunks based on text.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public List<DocumentChunk> Chunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy ignores the structuredDoc parameter and works only on document.Content.
            _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Executing Chunk for {document.Id}. StructuredDoc is ignored.");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;

            // (Logic remains the same as before)
            var paragraphs = Regex.Split(content, @"\r?\n\s*\r?\n"); // Handles various line break styles
            var chunks = new List<DocumentChunk>();
            var currentChunk = new StringBuilder();
            int chunkIndex = 0;
            int chunkSize = _configService.ChunkSize;
            int overlap = _configService.ChunkOverlap;

            foreach (var paragraph in paragraphs)
            {
                string trimmedParagraph = paragraph.Trim();
                if (string.IsNullOrWhiteSpace(trimmedParagraph)) continue;

                // Check if adding the next paragraph would exceed the chunk size
                if (currentChunk.Length > 0 && currentChunk.Length + trimmedParagraph.Length + 2 > chunkSize) // +2 for potential newline
                {
                    // Finalize the current chunk
                    string chunkContent = currentChunk.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunkContent))
                    {
                        chunks.Add(new DocumentChunk
                        {
                            Id = Guid.NewGuid().ToString(),
                            DocumentId = documentId,
                            Content = chunkContent,
                            ChunkIndex = chunkIndex++,
                            Source = source,
                            ChunkType = "ParagraphGroup" // Default type for text chunks
                            // SectionPath and HeadingLevel are not typically available here
                        });
                        _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Created text chunk {chunkIndex - 1}, length {chunkContent.Length}");
                    }

                    // Start new chunk, potentially with overlap
                    currentChunk.Clear();
                    if (overlap > 0 && chunkContent.Length > overlap)
                    {
                        // Simple overlap: Take the last 'overlap' characters.
                        // More sophisticated overlap could find sentence boundaries near the overlap point.
                        string overlapText = chunkContent.Substring(chunkContent.Length - overlap);
                        // Find the first space in the overlap to make it cleaner (optional)
                        int firstSpace = overlapText.IndexOf(' ');
                        if (firstSpace > 0 && firstSpace < overlapText.Length - 1)
                        {
                            overlapText = overlapText.Substring(firstSpace + 1);
                        }
                        currentChunk.Append(overlapText).AppendLine();
                        _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Overlap added, length {overlapText.Length}");
                    }
                }

                // Add the current paragraph to the chunk builder
                currentChunk.AppendLine(trimmedParagraph);
            }

            // Add the last remaining chunk
            string finalChunkContent = currentChunk.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(finalChunkContent))
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = finalChunkContent,
                    ChunkIndex = chunkIndex,
                    Source = source,
                    ChunkType = "ParagraphGroup"
                });
                _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Created final text chunk {chunkIndex}, length {finalChunkContent.Length}");
            }
            _diagnostics.Log(DiagnosticLevel.Info, "TextChunkingStrategy", $"Finished for {document.Id}, created {chunks.Count} chunks using text strategy.");
            return chunks;
        }
    }
}