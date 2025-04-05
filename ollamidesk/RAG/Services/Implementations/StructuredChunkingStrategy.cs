// ollamidesk/RAG/Services/Implementations/StructuredChunkingStrategy.cs
// MODIFIED VERSION - Updated signatures to match IChunkingStrategy
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services; // Assuming IRagConfigurationService is here
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // <-- ADDED for StructuredDocument

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Strategy for chunking documents based on structural elements like headers (Markdown/similar),
    /// identified via Regex in the raw text content.
    /// </summary>
    public class StructuredChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly TextChunkingStrategy _textChunkingStrategy; // For splitting large sections

        // Context prefix format (used internally by this strategy based on Regex matches)
        private const string ContextPrefix = "Context: ";
        private const string ContextSuffix = "\n\n";

        public StructuredChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics, TextChunkingStrategy textChunkingStrategy)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _textChunkingStrategy = textChunkingStrategy ?? throw new ArgumentNullException(nameof(textChunkingStrategy));
        }

        /// <summary>
        /// Determines if this strategy can chunk based on finding Markdown-style headers in the content.
        /// </summary>
        /// <param name="document">The document to check.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>True if Markdown headers are found in the document content, false otherwise.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public bool CanChunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy ignores structuredDoc and checks for Markdown patterns in document.Content.
            // It should run *before* HierarchicalChunkingStrategy if the goal is to prioritize
            // Regex-based Markdown over pre-parsed structure for .md files, or *after* if the opposite.
            // Current DI registration likely places this after HierarchicalChunkingStrategy.
            return Regex.IsMatch(document.Content, @"^#{1,6}\s+.+$", RegexOptions.Multiline);
        }

        /// <summary>
        /// Chunks the document based on Markdown headers found via Regex in the raw text content.
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>A list of document chunks based on Markdown headers.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public List<DocumentChunk> Chunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy ignores the structuredDoc parameter and works only on document.Content using Regex.
            _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Executing Chunk for {document.Id}. StructuredDoc is ignored.");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;

            var headerMatches = Regex.Matches(content, @"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);

            if (headerMatches.Count == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "StructuredChunkingStrategy", $"No headers found via Regex in {documentId} despite CanChunk=true. Falling back via service layer if needed.");
                return new List<DocumentChunk>(); // Let ChunkingService handle fallback
            }

            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;
            var sectionStack = new Stack<(int Level, string Heading)>(); // Use stack for hierarchical paths based on Regex matches

            // --- Process content before the first header ---
            if (headerMatches[0].Index > 0)
            {
                string initialContent = content.Substring(0, headerMatches[0].Index).Trim();
                if (!string.IsNullOrWhiteSpace(initialContent))
                {
                    // No specific section path for preface
                    chunks.AddRange(SplitSectionIfTooLarge(initialContent, documentId, source, ref chunkIndex, "Preface", 0, "", sectionStack));
                    _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Created preface chunk(s) for {documentId}");
                }
            }

            // --- Process content under each header ---
            for (int i = 0; i < headerMatches.Count; i++)
            {
                Match headerMatch = headerMatches[i];
                int level = headerMatch.Groups[1].Value.Length;
                string headingText = headerMatch.Groups[2].Value.Trim();

                // Update section stack for hierarchy
                while (sectionStack.Count > 0 && sectionStack.Peek().Level >= level)
                {
                    sectionStack.Pop();
                }
                sectionStack.Push((level, headingText));
                string currentSectionPath = string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading)); // Build full path from Regex matches

                int startPos = headerMatch.Index; // Include header in the section content
                int endPos = (i < headerMatches.Count - 1) ? headerMatches[i + 1].Index : content.Length;
                string sectionContent = content.Substring(startPos, endPos - startPos).Trim();

                if (string.IsNullOrWhiteSpace(sectionContent)) continue;

                chunks.AddRange(SplitSectionIfTooLarge(sectionContent, documentId, source, ref chunkIndex, headingText, level, currentSectionPath, sectionStack));
            }
            _diagnostics.Log(DiagnosticLevel.Info, "StructuredChunkingStrategy", $"Finished for {documentId}, created {chunks.Count} chunks using Regex-based structure.");
            return chunks;
        }

        // Helper method SplitSectionIfTooLarge remains the same internally,
        // prepending the sectionPath derived from Regex matches, NOT from structuredDoc.
        private List<DocumentChunk> SplitSectionIfTooLarge(string sectionContent, string documentId, string source, ref int chunkIndex, string headingText, int headingLevel, string sectionPath, Stack<(int Level, string Heading)> sectionStack)
        {
            var sectionChunks = new List<DocumentChunk>();
            int maxChunkSize = _configService.ChunkSize;
            // Context header uses sectionPath derived from Regex matches
            string contextHeader = string.IsNullOrWhiteSpace(sectionPath) ? "" : $"{ContextPrefix}{sectionPath}{ContextSuffix}";
            int contextHeaderLength = contextHeader.Length;
            int maxContentLength = maxChunkSize - contextHeaderLength;

            if (maxContentLength <= 10)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "StructuredChunkingStrategy", $"Context header for Regex section '{sectionPath}' is too long, skipping content.");
                return sectionChunks;
            }

            // Check if the content fits within the size limit (with some buffer perhaps)
            if (sectionContent.Length <= maxContentLength) // Simplified check
            {
                string finalContent = contextHeader + sectionContent;
                sectionChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = finalContent, // Prepend context derived from Regex
                    ChunkIndex = chunkIndex++,
                    Source = source,
                    ChunkType = headingLevel > 0 ? "Section" : "ParagraphGroup",
                    HeadingLevel = headingLevel > 0 ? headingLevel : null,
                    SectionPath = sectionPath // Path derived from Regex
                });
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Created single chunk for Regex section '{headingText}', prepended path '{sectionPath}', total length {finalContent.Length}");
            }
            else // Section too large, split using text chunking strategy
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Regex section '{headingText}' is too large ({sectionContent.Length} chars vs {maxContentLength}), splitting further using text chunking.");

                var dummyDoc = new Document { Id = documentId, Name = source, Content = sectionContent };
                // Use TextChunkingStrategy (which needs updated signature but ignores structuredDoc)
                var subChunks = _textChunkingStrategy.Chunk(dummyDoc, null); // Pass null for structuredDoc

                // Update metadata and prepend context for sub-chunks
                foreach (var subChunk in subChunks)
                {
                    subChunk.ChunkIndex = chunkIndex++; // Ensure continuous indexing
                    subChunk.ChunkType = "SubSection"; // Mark as part of a larger section
                    subChunk.HeadingLevel = headingLevel > 0 ? headingLevel : null;
                    subChunk.SectionPath = sectionPath; // Path derived from Regex
                    // Prepend the context path (derived from Regex) to each sub-chunk
                    subChunk.Content = contextHeader + subChunk.Content;
                }
                sectionChunks.AddRange(subChunks);
                _diagnostics.Log(DiagnosticLevel.Debug, "StructuredChunkingStrategy", $"Split large Regex section '{headingText}' into {subChunks.Count} sub-chunks, prepended path '{sectionPath}' to each.");
            }
            return sectionChunks;
        }
    }
}