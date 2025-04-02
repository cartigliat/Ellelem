// ollamidesk/RAG/Services/Implementations/StructuredChunkingStrategy.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Strategy for chunking documents based on structural elements like headers (Markdown/similar).
    /// </summary>
    public class StructuredChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;
        // Option 1: Inject Text strategy for fallback on large sections
        private readonly TextChunkingStrategy _textChunkingStrategy;

        public StructuredChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics, TextChunkingStrategy textChunkingStrategy)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _textChunkingStrategy = textChunkingStrategy ?? throw new ArgumentNullException(nameof(textChunkingStrategy));
        }

        public bool CanChunk(Document document)
        {
            // Heuristic: Check for Markdown-style headers
            return Regex.IsMatch(document.Content, @"^#{1,6}\s+.+$", RegexOptions.Multiline);
        }

        public List<DocumentChunk> Chunk(Document document)
        {
            _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Executing Chunk for {document.Id}");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;

            // (Logic moved from ChunkingService.ChunkStructuredDocument)
            var headerMatches = Regex.Matches(content, @"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);

            // If no headers are found despite CanChunk returning true (unlikely), fallback might be needed,
            // but the main service should handle fallback if CanChunk is false.
            if (headerMatches.Count == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "StructuredChunkingStrategy", $"No headers found in {documentId} despite CanChunk=true. Returning empty list.");
                return new List<DocumentChunk>();
            }

            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;
            string currentSectionPath = "";

            if (headerMatches[0].Index > 0)
            {
                string initialContent = content.Substring(0, headerMatches[0].Index).Trim();
                if (!string.IsNullOrWhiteSpace(initialContent))
                {
                    chunks.AddRange(SplitSectionIfTooLarge(initialContent, documentId, source, ref chunkIndex, "Preface", 0, ""));
                    _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Created preface chunk(s) for {documentId}");
                }
            }

            for (int i = 0; i < headerMatches.Count; i++)
            {
                Match headerMatch = headerMatches[i];
                int level = headerMatch.Groups[1].Value.Length;
                string headingText = headerMatch.Groups[2].Value.Trim();

                // Basic path, could be hierarchical later
                currentSectionPath = headingText;

                int startPos = headerMatch.Index;
                int endPos = (i < headerMatches.Count - 1) ? headerMatches[i + 1].Index : content.Length;
                string sectionContentIncludingHeader = content.Substring(startPos, endPos - startPos).Trim();

                if (string.IsNullOrWhiteSpace(sectionContentIncludingHeader)) continue;

                chunks.AddRange(SplitSectionIfTooLarge(sectionContentIncludingHeader, documentId, source, ref chunkIndex, headingText, level, currentSectionPath));
            }
            _diagnostics.Log(DiagnosticLevel.Info, "StructuredChunkingStrategy", $"Finished for {documentId}, created {chunks.Count} chunks.");
            return chunks;
        }

        // Helper moved from ChunkingService
        private List<DocumentChunk> SplitSectionIfTooLarge(string sectionContent, string documentId, string source, ref int chunkIndex, string headingText, int headingLevel, string sectionPath)
        {
            var sectionChunks = new List<DocumentChunk>();
            // Use a slightly larger threshold to allow reasonable section sizes before splitting
            if (sectionContent.Length <= _configService.ChunkSize * 1.5)
            {
                sectionChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = sectionContent,
                    ChunkIndex = chunkIndex++,
                    Source = source,
                    ChunkType = headingLevel > 0 ? "Section" : "ParagraphGroup", // Type based on if it's under a heading
                    HeadingLevel = headingLevel > 0 ? headingLevel : null,
                    SectionPath = sectionPath
                });
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Created single chunk for section '{headingText}', length {sectionContent.Length}");
            }
            else
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Section '{headingText}' is too large ({sectionContent.Length} chars), splitting further using text chunking.");
                // Use the injected TextChunkingStrategy for large sections
                var dummyDoc = new Document { Id = documentId, Name = source, Content = sectionContent };
                var subChunks = _textChunkingStrategy.Chunk(dummyDoc);

                // Update metadata for sub-chunks
                foreach (var subChunk in subChunks)
                {
                    subChunk.ChunkIndex = chunkIndex++; // Ensure continuous indexing from the main service
                    subChunk.ChunkType = "SubSection";
                    subChunk.HeadingLevel = headingLevel > 0 ? headingLevel : null;
                    subChunk.SectionPath = sectionPath;
                }
                sectionChunks.AddRange(subChunks);
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Split large section '{headingText}' into {subChunks.Count} sub-chunks.");
            }
            return sectionChunks;
        }
    }
}