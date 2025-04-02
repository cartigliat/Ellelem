// ollamidesk/RAG/Services/Implementations/CodeChunkingStrategy.cs
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
    /// Strategy for chunking code documents based on blocks, definitions, or lines.
    /// </summary>
    public class CodeChunkingStrategy : IChunkingStrategy
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;

        public CodeChunkingStrategy(IRagConfigurationService configService, RagDiagnosticsService diagnostics)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public bool CanChunk(Document document)
        {
            // Heuristic: Check for common code block markers or keywords
            return document.Content.Contains("```") ||
                   Regex.IsMatch(document.Content, @"\b(class|struct|interface|enum|def|function|public|private|protected|internal|static)\b");
        }

        public List<DocumentChunk> Chunk(Document document)
        {
            _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Executing Chunk for {document.Id}");
            string content = document.Content;
            string documentId = document.Id;
            string source = document.Name;

            // (Logic moved from ChunkingService.ChunkCodeDocument)
            var chunks = new List<DocumentChunk>();
            int chunkIndex = 0;
            int currentPosition = 0;

            // Regex to find ``` blocks or common definition keywords
            // Improved regex to better capture definition starts and handle generics/return types
            var blockRegex = new Regex(@"```([\s\S]*?)```|(?:(?:public|private|protected|internal|static|async|virtual|override|sealed)\s+)*?(?:class|struct|interface|enum|record|delegate|void|def|function|\w+<.*?>|\w+)\s+\w+(?:<.*?>)?\s*\(.*?\)\s*(?:[:]\s*\w+<.*?>|\w+)?\s*\{", RegexOptions.Multiline);

            Match match = blockRegex.Match(content);

            while (match.Success)
            {
                if (match.Index > currentPosition)
                {
                    string precedingText = content.Substring(currentPosition, match.Index - currentPosition).Trim();
                    if (!string.IsNullOrWhiteSpace(precedingText))
                    {
                        chunks.AddRange(SplitCodeOrTextChunkByLines(precedingText, documentId, source, ref chunkIndex, "CodeOrText"));
                        _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added preceding text chunk before match at {match.Index}");
                    }
                }

                string matchedBlock = match.Value;
                string chunkType = "CodeBlock"; // Default

                if (!matchedBlock.StartsWith("```")) // It's a definition
                {
                    chunkType = "Definition";
                    int braceLevel = 0;
                    int endPosition = -1;
                    bool foundOpenBrace = false;
                    for (int i = match.Index + match.Length - 1; i < content.Length; i++) // Start search near the initial {
                    {
                        if (content[i] == '{') { braceLevel++; foundOpenBrace = true; }
                        else if (content[i] == '}') { braceLevel--; }

                        if (foundOpenBrace && braceLevel == 0) { endPosition = i + 1; break; }
                    }

                    if (endPosition != -1 && endPosition > match.Index)
                    {
                        matchedBlock = content.Substring(match.Index, endPosition - match.Index);
                    }
                    else // Couldn't find matching brace, treat as simple block or take up to next significant point
                    {
                        // Use a less aggressive approach if brace matching fails
                        int nextMatchStart = match.NextMatch().Success ? match.NextMatch().Index : content.Length;
                        int nextNewLine = content.IndexOf('\n', match.Index + match.Length);
                        if (nextNewLine == -1) nextNewLine = content.Length;
                        endPosition = Math.Min(nextMatchStart, nextNewLine); // Take up to next match or newline
                        matchedBlock = content.Substring(match.Index, Math.Max(1, endPosition - match.Index)); // Ensure positive length
                        _diagnostics.Log(DiagnosticLevel.Warning, "CodeChunkingStrategy", $"Could not find matching brace for definition at {match.Index}. Taking content up to index {endPosition}.");
                    }
                }
                else // It's a ``` code block
                {
                    // The regex already captured the content inside Group 1, but value includes ```
                    // matchedBlock remains the full ```...``` block here.
                }

                chunks.AddRange(SplitCodeOrTextChunkByLines(matchedBlock.Trim(), documentId, source, ref chunkIndex, chunkType));
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added {chunkType} chunk, length {matchedBlock.Length}");


                currentPosition = match.Index + matchedBlock.Length;
                match = match.NextMatch();
            }

            if (currentPosition < content.Length)
            {
                string remainingText = content.Substring(currentPosition).Trim();
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    chunks.AddRange(SplitCodeOrTextChunkByLines(remainingText, documentId, source, ref chunkIndex, "CodeOrText"));
                    _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Added final remaining text chunk, length {remainingText.Length}");
                }
            }
            _diagnostics.Log(DiagnosticLevel.Info, "CodeChunkingStrategy", $"Finished for {documentId}, created {chunks.Count} chunks.");
            return chunks;
        }

        // Helper moved from ChunkingService
        private List<DocumentChunk> SplitCodeOrTextChunkByLines(string text, string documentId, string source, ref int chunkIndex, string chunkType)
        {
            var resultChunks = new List<DocumentChunk>();
            if (string.IsNullOrWhiteSpace(text)) return resultChunks;

            if (text.Length <= _configService.ChunkSize)
            {
                resultChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = text,
                    ChunkIndex = chunkIndex++,
                    Source = source,
                    ChunkType = chunkType
                });
            }
            else
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Splitting {chunkType} chunk by lines (length {text.Length} > {_configService.ChunkSize})");
                string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var currentSubChunk = new StringBuilder();
                int subChunkCount = 0;

                foreach (var line in lines)
                {
                    if (currentSubChunk.Length + line.Length + 1 > _configService.ChunkSize && currentSubChunk.Length > 0)
                    {
                        resultChunks.Add(new DocumentChunk
                        {
                            Id = Guid.NewGuid().ToString(),
                            DocumentId = documentId,
                            Content = currentSubChunk.ToString().TrimEnd(),
                            ChunkIndex = chunkIndex++,
                            Source = source,
                            ChunkType = $"{chunkType}Part"
                        });
                        currentSubChunk.Clear();
                        subChunkCount++;
                        // No overlap for code splitting for simplicity
                    }
                    currentSubChunk.AppendLine(line);
                }

                if (currentSubChunk.Length > 0)
                {
                    resultChunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid().ToString(),
                        DocumentId = documentId,
                        Content = currentSubChunk.ToString().TrimEnd(),
                        ChunkIndex = chunkIndex++,
                        Source = source,
                        ChunkType = $"{chunkType}Part"
                    });
                    subChunkCount++;
                }
                _diagnostics.Log(DiagnosticLevel.Debug, "CodeChunkingStrategy", $"Split into {subChunkCount} sub-chunks by line.");
            }
            return resultChunks;
        }
    }
}