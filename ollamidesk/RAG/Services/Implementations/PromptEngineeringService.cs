using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the prompt engineering service
    /// </summary>
    public class PromptEngineeringService : IPromptEngineeringService
    {
        private readonly RagDiagnosticsService _diagnostics;

        public PromptEngineeringService(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics.Log(DiagnosticLevel.Info, "PromptEngineeringService", "Initialized PromptEngineeringService");
        }

        public Task<string> CreateAugmentedPromptAsync(
    string query,
    List<DocumentChunk> relevantChunks)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query cannot be empty", nameof(query));
            }

            _diagnostics.StartOperation("CreateAugmentedPrompt");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "PromptEngineeringService",
                    $"Creating augmented prompt for query: \"{(query.Length > 50 ? query.Substring(0, 47) + "..." : query)}\"");

                if (relevantChunks == null || relevantChunks.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "PromptEngineeringService",
                        "No relevant chunks provided for augmented prompt");
                    return Task.FromResult(query);
                }

                // Build augmented prompt
                var promptBuilder = new StringBuilder();
                promptBuilder.AppendLine("You are an AI assistant using information from documents to answer questions. Use ONLY the following context information to answer the query at the end. If you don't know the answer based on the provided context, say you don't have enough information, but try to be helpful by suggesting what might be relevant.");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Context information:");
                promptBuilder.AppendLine();

                foreach (var chunk in relevantChunks)
                {
                    promptBuilder.AppendLine(FormatChunkForPrompt(chunk));
                    promptBuilder.AppendLine();
                }

                promptBuilder.AppendLine("Query: " + query);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Answer: ");

                string augmentedPrompt = promptBuilder.ToString();

                _diagnostics.Log(DiagnosticLevel.Info, "PromptEngineeringService",
                    $"Generated augmented prompt with {relevantChunks.Count} chunks (total length: {augmentedPrompt.Length} chars)");

                return Task.FromResult(augmentedPrompt);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PromptEngineeringService",
                    $"Failed to generate augmented prompt: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("CreateAugmentedPrompt");
            }
        }

        public string FormatChunkForPrompt(DocumentChunk chunk)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            _diagnostics.Log(DiagnosticLevel.Debug, "PromptEngineeringService",
                $"Formatting chunk {chunk.Id} for prompt");

            // Build the formatted chunk
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"--- Begin Document Chunk from {chunk.Source} ---");
            stringBuilder.AppendLine(chunk.Content);
            stringBuilder.AppendLine("--- End Document Chunk ---");

            return stringBuilder.ToString();
        }
    }
}