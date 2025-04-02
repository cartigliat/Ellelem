// ollamidesk/OllamaModel.cs
// Refactored to use IOllamaApiClient
using System;
using System.Collections.Generic;
using System.Linq; // Added for LINQ usage
using System.Text; // Added for StringBuilder
using System.Threading.Tasks;
using ollamidesk.Configuration; // Keep for settings access if needed indirectly
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces; // Added for IOllamaApiClient
using ollamidesk.RAG.Exceptions; // Added for specific exceptions


namespace ollamidesk
{
    public class OllamaModel : IOllamaModel
    {
        private readonly string _modelName;
        private readonly string _systemPrompt;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IOllamaApiClient _apiClient; // <-- Dependency Changed
        private readonly OllamaSettings _settings; // Keep settings if needed for options


        public OllamaModel(
            string modelName,
            OllamaSettings settings, // Keep settings
            IOllamaApiClient apiClient, // <-- Inject Api Client
            RagDiagnosticsService diagnostics)
        {
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings)); // Store settings
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient)); // Store injected client
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _systemPrompt = _settings.SystemPrompt; // Get system prompt from settings

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Model initialized: {modelName} using IOllamaApiClient");
            // Removed HttpClientFactory, max concurrent requests log (handled by client)
        }

        // Simplified: Constructs payload and calls API client
        public async Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory)
        {
            _diagnostics.StartOperation("Model.GenerateResponse");
            try
            {
                string prompt = ConstructPrompt(userInput, loadedDocument, chatHistory);

                // Use settings for API options
                var requestData = new
                {
                    model = _modelName,
                    prompt = prompt,
                    system = _systemPrompt,
                    options = new
                    {
                        temperature = 0.7, // Example options from original code
                        top_p = 0.9
                    },
                    stream = false
                };


                _diagnostics.Log(DiagnosticLevel.Debug, "OllamaModel", $"Calling API Client GenerateAsync for model {_modelName}. Prompt length: {prompt.Length}");
                // Delegate the actual API call
                string responseText = await _apiClient.GenerateAsync(requestData, _modelName).ConfigureAwait(false);

                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Response received from API client for model {_modelName}. Length: {responseText.Length}");
                return responseText;

            }
            catch (DocumentProcessingException ex) // Catch specific API client errors
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"API Client error generating response: {ex.Message}");
                // Re-throw or return formatted error message
                return $"Error communicating with Ollama: {ex.Message}";
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"Unexpected error in GenerateResponseAsync: {ex.Message}");
                return $"An unexpected error occurred: {ex.Message}";
            }
            finally
            {
                _diagnostics.EndOperation("Model.GenerateResponse");
            }
        }

        // Simplified: Constructs payload with context and calls API client
        public async Task<string> GenerateResponseWithContextAsync(string userInput, List<string> chatHistory, List<DocumentChunk> relevantChunks)
        {
            _diagnostics.StartOperation("Model.GenerateResponseWithContext");
            try
            {
                string prompt = ConstructPromptWithContext(userInput, chatHistory, relevantChunks);

                // Use settings for API options
                var requestData = new
                {
                    model = _modelName,
                    prompt = prompt,
                    system = _systemPrompt, // Use the class system prompt
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9
                    },
                    stream = false
                };

                _diagnostics.Log(DiagnosticLevel.Debug, "OllamaModel", $"Calling API Client GenerateAsync for model {_modelName} with context. Prompt length: {prompt.Length}");
                // Delegate the actual API call
                string responseText = await _apiClient.GenerateAsync(requestData, _modelName).ConfigureAwait(false);

                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Response received from API client for model {_modelName} with context. Length: {responseText.Length}");
                return responseText;
            }
            catch (DocumentProcessingException ex) // Catch specific API client errors
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"API Client error generating response with context: {ex.Message}");
                return $"Error communicating with Ollama (with context): {ex.Message}";
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"Unexpected error in GenerateResponseWithContextAsync: {ex.Message}");
                return $"An unexpected error occurred while processing with context: {ex.Message}";
            }
            finally
            {
                _diagnostics.EndOperation("Model.GenerateResponseWithContext");
            }
        }

        // Helper to construct the basic prompt
        private string ConstructPrompt(string userInput, string? loadedDocument, List<string>? chatHistory)
        {
            // This logic remains similar to original, focusing on prompt building
            var promptBuilder = new StringBuilder();

            // Add document context if available
            if (!string.IsNullOrEmpty(loadedDocument))
            {
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Adding document context (length: {loadedDocument.Length} chars)");
                promptBuilder.AppendLine("--- Document Context ---");
                promptBuilder.AppendLine(loadedDocument);
                promptBuilder.AppendLine("--- End Context ---");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Question based on the context above and chat history below:");
                promptBuilder.AppendLine(userInput);

            }
            else
            {
                promptBuilder.AppendLine("Question:");
                promptBuilder.AppendLine(userInput);
            }


            // Add chat history for context (limited history)
            if (chatHistory != null && chatHistory.Count > 0)
            {
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Including {chatHistory.Count} chat history messages for context");
                promptBuilder.AppendLine("\n--- Chat History (Recent first) ---");
                // Include limited history, e.g., last 5 turns (10 messages)
                foreach (var message in chatHistory.Take(10).Reverse()) // Take last 10, reverse to show oldest first in prompt context
                {
                    promptBuilder.AppendLine(message); // Assuming history contains formatted "User: ..." / "Assistant: ..." lines
                }
                promptBuilder.AppendLine("--- End History ---");

            }

            return promptBuilder.ToString();
        }

        // Helper to construct the prompt with RAG context
        private string ConstructPromptWithContext(string userInput, List<string>? chatHistory, List<DocumentChunk> relevantChunks)
        {
            // This logic remains similar to original, focusing on prompt building
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("Use the following context information derived from relevant document sections to answer the query. Focus primarily on the provided context.");
            promptBuilder.AppendLine("--- Context Chunks ---");
            if (relevantChunks != null && relevantChunks.Any())
            {
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Building context with {relevantChunks.Count} chunks");
                foreach (var chunk in relevantChunks)
                {
                    promptBuilder.AppendLine($"Source: {chunk.Source} (Chunk {chunk.ChunkIndex})");
                    // Add section path if available
                    if (!string.IsNullOrWhiteSpace(chunk.SectionPath))
                    {
                        promptBuilder.AppendLine($"Section: {chunk.SectionPath}");
                    }
                    promptBuilder.AppendLine("Content:");
                    promptBuilder.AppendLine(chunk.Content);
                    promptBuilder.AppendLine("---");
                }
            }
            else
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel", "GenerateResponseWithContext called but no relevant chunks provided.");
                promptBuilder.AppendLine("[No specific document context chunks found]");
            }
            promptBuilder.AppendLine("--- End Context Chunks ---");
            promptBuilder.AppendLine();


            // Add chat history for context (limited history)
            if (chatHistory != null && chatHistory.Count > 0)
            {
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Including {chatHistory.Count} chat history messages for context");
                promptBuilder.AppendLine("\n--- Chat History (Recent first) ---");
                // Include limited history, e.g., last 5 turns (10 messages)
                foreach (var message in chatHistory.Take(10).Reverse())
                {
                    promptBuilder.AppendLine(message);
                }
                promptBuilder.AppendLine("--- End History ---");
                promptBuilder.AppendLine();
            }


            promptBuilder.AppendLine("Based on the context and history (if provided), answer the following query:");
            promptBuilder.AppendLine($"Query: {userInput}");

            string fullContext = promptBuilder.ToString();
            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Constructed prompt with context. Length: {fullContext.Length} characters");
            return fullContext;
        }


        // TestConnection is now delegated to the API client
        public async Task<bool> TestConnectionAsync()
        {
            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel", $"Delegating connection test for model {_modelName} to API Client.");
            return await _apiClient.TestConnectionAsync(_modelName).ConfigureAwait(false);
        }


        // --- REMOVED SendRequestToOllamaAsync ---
        // Logic moved to OllamaApiClient.SendRequestInternalAsync
    }
}