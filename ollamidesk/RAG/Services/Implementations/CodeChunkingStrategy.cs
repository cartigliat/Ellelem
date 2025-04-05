// ollamidesk/RAG/Services/Implementations/CodeChunkingStrategy.cs
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
    /// Strategy for chunking code documents based on blocks, definitions, or lines. (Refined Heuristics + Context)
    /// </summary>
    public class CodeChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;

        // Context prefix format
        private const string ContextPrefix = "Context: ";
        private const string ContextSuffix = "\n...\n"; // Indicate context is partial if chunk is split

        private static readonly System.Text.RegularExpressions.Regex DefinitionRegex = new System.Text.RegularExpressions.Regex(
            @"^" // Start of line
            + @"(?<indent>\s*)" // Capture indentation
            + @"(?:(?:public|private|protected|internal|static|async|virtual|override|sealed)\s+)*" // Modifiers
            + @"(?:(?:class|struct|interface|enum|record|delegate|void)\s+|(?:\w+<.*?>|\w+)\s+)" // Type or keyword (including generics)
            + @"(?<name>\w+)" // Name
            + @"(?:<.*?>)?\s*" // Optional generic parameters for the name
            + @"(?<signature>\(.*?\))\s*" // Parameters
            + @"(?:[:]\s*(?:\w+<.*?>|\w+))?\s*" // Optional inheritance or return type hint
            + @"\{", // Opening brace
            RegexOptions.Multiline);

        private static readonly System.Text.RegularExpressions.Regex CodeBlockRegex = new System.Text.RegularExpressions.Regex(@"```([\s\S]*?)```", RegexOptions.Multiline);

        public CodeChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Determines if this strategy can chunk based on code-like content heuristics.
        /// </summary>
        /// <param name="document">The document to check.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>True if the document content looks like code, false otherwise.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public bool CanChunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy ignores structuredDoc and checks for code patterns.
            string contentSample = document.Content.Length > 1000 ? document.Content.Substring(0, 1000) : document.Content;
            return contentSample.Contains("```") ||
                   Regex.IsMatch(contentSample, @"\b(class|struct|interface|enum|def|function|public|private|protected|internal|static|namespace|using|import)\b");
        }

        /// <summary>
        /// Chunks the document based on code syntax (definitions, blocks, lines).
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <param name="structuredDoc">Optional structured document information (ignored by this strategy).</param> /// <--- MODIFIED parameter added
        /// <returns>A list of document chunks based on code structure.</returns>
        // <<< MODIFIED: Method signature updated >>>
        public List<DocumentChunk> Chunk(Document document, StructuredDocument? structuredDoc)
        {
            // This strategy ignores the structuredDoc parameter.
            _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Executing Chunk for {document.Id}. StructuredDoc is ignored.");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;
            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;

            // --- Logic for finding DefinitionRegex and CodeBlockRegex remains the same ---
            var definitionMatches = DefinitionRegex.Matches(content).Cast<Match>().Select(m => new { Match = m, Type = "Definition" });
            var codeBlockMatches = CodeBlockRegex.Matches(content).Cast<Match>().Select(m => new { Match = m, Type = "CodeBlock" });

            var allMatches = definitionMatches.Concat(codeBlockMatches)
                                            .OrderBy(m => m.Match.Index)
                                            .ToList();
            int currentPosition = 0;

            foreach (var matchInfo in allMatches)
            {
                Match match = matchInfo.Match;
                string matchType = matchInfo.Type;

                // Process text before the current match
                if (match.Index > currentPosition)
                {
                    string precedingText = content.Substring(currentPosition, match.Index - currentPosition);
                    if (!string.IsNullOrWhiteSpace(precedingText))
                    {
                        // Split plain text parts (no specific context header here)
                        chunks.AddRange(SplitPlainTextChunk(precedingText, documentId, source, ref chunkIndex, "CodeText", null, null));
                        _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added preceding text chunk(s) before match at {match.Index}");
                    }
                }

                string blockContent;
                string? definitionSignature = null;
                string? sectionPath = null; // Use definition name as path

                if (matchType == "Definition")
                {
                    int endPosition = FindMatchingBrace(content, match.Index + match.Length - 1);
                    definitionSignature = match.Value.TrimEnd('{').Trim(); // Capture signature
                    sectionPath = match.Groups["name"]?.Value; // Capture name

                    if (endPosition != -1)
                    {
                        blockContent = content.Substring(match.Index, endPosition - match.Index);
                    }
                    else // Could not find matching brace
                    {
                        int nextMatchStart = allMatches.FirstOrDefault(m => m.Match.Index > match.Index)?.Match.Index ?? content.Length;
                        blockContent = content.Substring(match.Index, nextMatchStart - match.Index);
                        _diagnostics.Log(DiagnosticLevel.Warning, "CodeChunkingStrategy", $"Could not find matching brace for definition at {match.Index}. Taking content up to index {nextMatchStart}. Signature: {definitionSignature}");
                    }
                }
                else // CodeBlock
                {
                    blockContent = match.Value;
                    // Potentially extract language from ```lang for sectionPath/metadata?
                    // Example: sectionPath = ExtractLangFromFence(match);
                }

                // Split the identified block if it's too large, prepending context (signature or block context)
                chunks.AddRange(SplitCodeBlockChunk(blockContent, documentId, source, ref chunkIndex, matchType, sectionPath, definitionSignature));
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added {matchType} chunk(s) starting at {match.Index}, original length {blockContent.Length}");

                // Ensure currentPosition advances correctly
                currentPosition = match.Index + blockContent.Length;
                if (matchType == "Definition" && blockContent.Length < match.Value.Length)
                {
                    currentPosition = Math.Max(currentPosition, match.Index + match.Value.Length);
                }
            }

            // Process any remaining text after the last match
            if (currentPosition < content.Length)
            {
                string remainingText = content.Substring(currentPosition);
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    chunks.AddRange(SplitPlainTextChunk(remainingText, documentId, source, ref chunkIndex, "CodeText", null, null));
                    _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added final remaining text chunk(s), length {remainingText.Length}");
                }
            }

            _diagnostics.Log(DiagnosticLevel.Info, "CodeChunkingStrategy", $"Finished for {documentId}, created {chunks.Count} chunks using code strategy.");
            return chunks;
        }

        // Helper methods (SplitCodeBlockChunk, SplitPlainTextChunk, CreateChunk, FindMatchingBrace)
        // remain the same as the corrected version you already have, including context prepending.
        // Make sure they are included here.

        // Example (ensure full methods from previous version are here):
        private List<DocumentChunk> SplitCodeBlockChunk(string blockContent, string documentId, string source, ref int chunkIndex, string chunkType, string? sectionPath, string? contextSignature)
        {
            var resultChunks = new List<DocumentChunk>();
            if (string.IsNullOrWhiteSpace(blockContent)) return resultChunks;

            int maxChunkSize = _configService.ChunkSize;
            int overlap = _configService.ChunkOverlap;
            string contextHeader = "";
            if (!string.IsNullOrWhiteSpace(contextSignature))
                contextHeader = $"{ContextPrefix}{contextSignature}{ContextSuffix}";
            else if (!string.IsNullOrWhiteSpace(sectionPath))
                contextHeader = $"{ContextPrefix}Code block near '{sectionPath}'{ContextSuffix}";
            int contextHeaderLength = contextHeader.Length;
            int maxContentLength = maxChunkSize - contextHeaderLength;

            if (maxContentLength <= 10)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "CodeChunkingStrategy", $"Context header for '{sectionPath ?? "block"}' too long, skipping chunk creation.");
                return resultChunks;
            }

            if (blockContent.Length <= maxContentLength)
            {
                resultChunks.Add(CreateChunk(documentId, contextHeader + blockContent, chunkIndex++, source, chunkType, sectionPath));
            }
            else
            {
                // ... Splitting logic with overlap as implemented before ...
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Splitting large {chunkType} '{sectionPath ?? ""}', content length {blockContent.Length}");
                string[] lines = blockContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var currentSubChunkContent = new StringBuilder();
                int subChunkCount = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (currentSubChunkContent.Length + line.Length + 1 > maxContentLength && currentSubChunkContent.Length > 0)
                    {
                        resultChunks.Add(CreateChunk(documentId, contextHeader + currentSubChunkContent.ToString().TrimEnd(), chunkIndex++, source, $"{chunkType}Part", sectionPath));
                        currentSubChunkContent.Clear();
                        subChunkCount++;
                        // Add overlap logic here if needed
                    }
                    if (currentSubChunkContent.Length + line.Length + 1 <= maxContentLength)
                    {
                        currentSubChunkContent.AppendLine(line);
                    }
                    else // Line itself is too long
                    {
                        if (currentSubChunkContent.Length > 0)
                        {
                            resultChunks.Add(CreateChunk(documentId, contextHeader + currentSubChunkContent.ToString().TrimEnd(), chunkIndex++, source, $"{chunkType}Part", sectionPath));
                            subChunkCount++;
                        }
                        currentSubChunkContent.Clear();
                        string truncatedLine = line.Length > maxContentLength ? line.Substring(0, maxContentLength) : line;
                        resultChunks.Add(CreateChunk(documentId, contextHeader + truncatedLine, chunkIndex++, source, $"{chunkType}Part(LongLine)", sectionPath));
                        subChunkCount++;
                    }
                }
                if (currentSubChunkContent.Length > 0)
                {
                    resultChunks.Add(CreateChunk(documentId, contextHeader + currentSubChunkContent.ToString().TrimEnd(), chunkIndex++, source, $"{chunkType}Part", sectionPath));
                    subChunkCount++;
                }
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Split large {chunkType} into {subChunkCount} sub-chunks.");
            }
            return resultChunks;
        }

        private List<DocumentChunk> SplitPlainTextChunk(string text, string documentId, string source, ref int chunkIndex, string chunkType, string? sectionPath, string? contextSignature)
        {
            var resultChunks = new List<DocumentChunk>();
            // ... (Implementation as provided previously, including context header logic) ...
            if (string.IsNullOrWhiteSpace(text)) return resultChunks;

            int maxChunkSize = _configService.ChunkSize;
            int overlap = _configService.ChunkOverlap;
            string contextHeader = ""; // Build context header if applicable (likely not for plain text between code)
            int contextHeaderLength = contextHeader.Length;
            int maxContentLength = maxChunkSize - contextHeaderLength;

            if (maxContentLength <= 10) return resultChunks;

            if (text.Length <= maxContentLength)
            {
                resultChunks.Add(CreateChunk(documentId, contextHeader + text, chunkIndex++, source, chunkType, sectionPath));
            }
            else
            {
                int start = 0;
                while (start < text.Length)
                {
                    int length = Math.Min(maxContentLength, text.Length - start);
                    string contentPart = text.Substring(start, length);
                    resultChunks.Add(CreateChunk(documentId, contextHeader + contentPart, chunkIndex++, source, $"{chunkType}Part", sectionPath));
                    int step = maxContentLength - overlap;
                    if (step <= 0) step = Math.Max(1, maxContentLength / 2);
                    start += step;
                    if (start >= text.Length || step <= 0) break;
                }
            }
            return resultChunks;
        }


        private DocumentChunk CreateChunk(string documentId, string content, int index, string source, string type, string? path)
        {
            return new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = content,
                ChunkIndex = index,
                Source = source,
                ChunkType = type,
                SectionPath = path ?? string.Empty
                // Note: HeadingLevel is not typically set by CodeChunkingStrategy
            };
        }

        private int FindMatchingBrace(string text, int startIndex)
        {
            // ... (Implementation as provided previously) ...
            if (startIndex < 0 || startIndex >= text.Length) return -1;
            int openingBraceIndex = text.IndexOf('{', startIndex);
            if (openingBraceIndex == -1 || openingBraceIndex > startIndex + 150) return -1;
            startIndex = openingBraceIndex;

            int braceLevel = 0; bool inString = false; bool inChar = false; bool inLineComment = false; bool inBlockComment = false;
            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i]; char prevC = (i > 0) ? text[i - 1] : '\0';
                if (!inString && !inChar) { if (!inBlockComment && c == '/' && prevC == '/') { inLineComment = true; continue; } if (c == '\n') { inLineComment = false; continue; } if (!inLineComment && c == '*' && prevC == '/') { inBlockComment = true; continue; } if (c == '/' && prevC == '*') { inBlockComment = false; continue; } }
                if (inLineComment || inBlockComment) continue;
                if (c == '"' && prevC != '\\') inString = !inString; if (c == '\'' && prevC != '\\') inChar = !inChar; if (inString || inChar) continue;
                if (c == '{') braceLevel++;
                else if (c == '}') { if (braceLevel == 0) return -1; braceLevel--; if (braceLevel == 0) return i + 1; }
            }
            return -1;
        }
    }
}