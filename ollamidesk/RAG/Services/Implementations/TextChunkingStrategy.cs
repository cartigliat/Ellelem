// ollamidesk/RAG/Services/Implementations/TextChunkingStrategy.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Strategy for chunking plain text documents based on paragraphs and size limits.
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
        /// This is the default fallback strategy, so it can always chunk.
        /// </summary>
        public bool CanChunk(Document document)
        {
            // This strategy serves as the default/fallback.
            return true;
        }

        public List<DocumentChunk> Chunk(Document document)
        {
            _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Executing Chunk for {document.Id}");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;

            // (Logic moved from ChunkingService.ChunkTextDocument)
            var paragraphs = Regex.Split(content, @"\r?\n\s*\r?\n"); // Handles various line break styles
            var chunks = new List<DocumentChunk>();
            var currentChunk = new StringBuilder();
            int chunkIndex = 0;

            foreach (var paragraph in paragraphs)
            {
                string trimmedParagraph = paragraph.Trim();
                if (string.IsNullOrWhiteSpace(trimmedParagraph)) continue;

                int estimatedTokens = trimmedParagraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length; // Simple estimation

                if (currentChunk.Length > 0 && currentChunk.Length + trimmedParagraph.Length + 2 > _configService.ChunkSize)
                {
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
                            ChunkType = "ParagraphGroup"
                        });
                        _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Created text chunk {chunkIndex - 1}, length {chunkContent.Length}");
                    }

                    currentChunk.Clear();
                    if (_configService.ChunkOverlap > 0 && chunkContent.Length > _configService.ChunkOverlap)
                    {
                        string overlapText = chunkContent.Substring(Math.Max(0, chunkContent.Length - _configService.ChunkOverlap));
                        // Basic overlap, could be smarter (find sentence break)
                        currentChunk.Append(overlapText).AppendLine();
                        _diagnostics.Log(DiagnosticLevel.Debug, "TextChunkingStrategy", $"Overlap added, length {overlapText.Length}");
                    }
                }
                currentChunk.AppendLine(trimmedParagraph);
            }

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
            _diagnostics.Log(DiagnosticLevel.Info, "TextChunkingStrategy", $"Finished for {documentId}, created {chunks.Count} chunks.");
            return chunks;
        }
    }
}