using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;

namespace ollamidesk
{
    public class OllamaModel : IOllamaModel
    {
        private readonly string _modelName;
        private readonly HttpClient _httpClient;
        private const string OllamaApiUrl = "http://localhost:11434/api/generate";
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;
        private readonly string _systemPrompt;

        public OllamaModel(string modelName)
        {
            _modelName = modelName;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Increase timeout for larger responses

            _systemPrompt = "You are a helpful AI assistant. If context information is provided, use it to answer the question accurately. " +
                "If there are multiple relevant pieces of information, synthesize them into a coherent answer. " +
                "If you don't know the answer based on the provided context, say you don't have enough information.";
        }

        public async Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory)
        {
            var diagnostics = RagDiagnostics.Instance;
            diagnostics.StartOperation("GenerateResponse");

            try
            {
                // Prepare the prompt with context
                string prompt = userInput;

                // Add document context if available
                if (!string.IsNullOrEmpty(loadedDocument))
                {
                    diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                        $"Adding document context (length: {loadedDocument.Length} chars)");
                    prompt = $"Context information:\n{loadedDocument}\n\nQuestion: {userInput}";
                }

                // Add chat history for context
                if (chatHistory != null && chatHistory.Count > 0)
                {
                    diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                        $"Including {chatHistory.Count} chat history messages for context");
                }

                // Create the request JSON
                var requestData = new
                {
                    model = _modelName,
                    prompt = prompt,
                    system = _systemPrompt,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9
                    },
                    stream = false
                };

                // Serialize to JSON
                string jsonContent = JsonSerializer.Serialize(requestData);

                return await SendRequestToOllamaAsync(jsonContent);
            }
            catch (Exception ex)
            {
                diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"Error generating response: {ex.Message}");
                return $"An error occurred: {ex.Message}";
            }
            finally
            {
                diagnostics.EndOperation("GenerateResponse");
            }
        }

        public async Task<string> GenerateResponseWithContextAsync(string userInput, List<string> chatHistory, List<DocumentChunk> relevantChunks)
        {
            var diagnostics = RagDiagnostics.Instance;
            diagnostics.StartOperation("GenerateResponseWithContext");

            try
            {
                var contextBuilder = new StringBuilder();

                // Add relevant chunks
                if (relevantChunks != null && relevantChunks.Any())
                {
                    diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                        $"Building context with {relevantChunks.Count} chunks");

                    contextBuilder.AppendLine("Context information:");

                    foreach (var chunk in relevantChunks)
                    {
                        contextBuilder.AppendLine($"--- From {chunk.Source} ---");
                        contextBuilder.AppendLine(chunk.Content);
                        contextBuilder.AppendLine("---");
                    }

                    contextBuilder.AppendLine("\nQuestion: " + userInput);
                }
                else
                {
                    diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                        "GenerateResponseWithContext called but no relevant chunks provided");
                    contextBuilder.Append(userInput);
                }

                // Log the full context being sent
                string fullContext = contextBuilder.ToString();
                diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                    $"Full context length: {fullContext.Length} characters");

                // Create the request
                var requestData = new
                {
                    model = _modelName,
                    prompt = fullContext,
                    system = _systemPrompt,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9
                    },
                    stream = false
                };

                // Serialize to JSON
                string jsonContent = JsonSerializer.Serialize(requestData);

                return await SendRequestToOllamaAsync(jsonContent);
            }
            catch (Exception ex)
            {
                diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Error generating response with context: {ex.Message}");
                return $"An error occurred while processing your question with document context: {ex.Message}";
            }
            finally
            {
                diagnostics.EndOperation("GenerateResponseWithContext");
            }
        }

        private async Task<string> SendRequestToOllamaAsync(string jsonContent)
        {
            var diagnostics = RagDiagnostics.Instance;
            diagnostics.StartOperation("OllamaApiRequest");

            try
            {
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                diagnostics.LogApiRequest("OllamaModel", OllamaApiUrl,
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) + "..." : jsonContent);

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        // Send request to the Ollama API
                        var response = await _httpClient.PostAsync(OllamaApiUrl, content);

                        // Check if the request was successful
                        if (response.IsSuccessStatusCode)
                        {
                            // Read and parse JSON response
                            string jsonResponse = await response.Content.ReadAsStringAsync();
                            diagnostics.LogApiResponse("OllamaModel", OllamaApiUrl,
                                jsonResponse.Length > 500 ? jsonResponse.Substring(0, 500) + "..." : jsonResponse,
                                true);

                            try
                            {
                                using var doc = JsonDocument.Parse(jsonResponse);
                                // Extract just the response text from the JSON
                                if (doc.RootElement.TryGetProperty("response", out var responseElement))
                                {
                                    string responseText = responseElement.GetString() ?? "No response received";
                                    diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                                        $"Response generated successfully (length: {responseText.Length} chars)");
                                    return responseText;
                                }
                                else
                                {
                                    diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                                        "Unexpected API response format - no 'response' field");
                                    return "Error: Unexpected API response format";
                                }
                            }
                            catch (JsonException ex)
                            {
                                diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                                    $"Invalid JSON response: {ex.Message}");
                                return "Error: Invalid JSON response from Ollama API";
                            }
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            diagnostics.LogApiResponse("OllamaModel", OllamaApiUrl, errorContent, false);

                            // Only retry for server errors (5xx)
                            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
                            {
                                diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                                    $"Server error on attempt {attempt}, will retry after delay: {response.StatusCode}");
                                await Task.Delay(RetryDelayMs * attempt);
                                continue;
                            }

                            return $"Error: API request failed with status {response.StatusCode}";
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        if (attempt < MaxRetries)
                        {
                            diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                                $"Request timeout on attempt {attempt}, will retry");
                            await Task.Delay(RetryDelayMs * attempt);
                            continue;
                        }
                        return "Error: Request to Ollama API timed out after multiple attempts";
                    }
                    catch (Exception ex) when (attempt < MaxRetries)
                    {
                        diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                            $"Error on attempt {attempt}, will retry: {ex.Message}");
                        await Task.Delay(RetryDelayMs * attempt);
                    }
                }

                return "Error: Failed to get a response from Ollama API after multiple attempts";
            }
            catch (Exception ex)
            {
                diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Unhandled exception in SendRequestToOllamaAsync: {ex.Message}");
                return $"An error occurred: {ex.Message}";
            }
            finally
            {
                diagnostics.EndOperation("OllamaApiRequest");
            }
        }

        // Helper method for testing the API connection
        public async Task<bool> TestConnectionAsync()
        {
            var diagnostics = RagDiagnostics.Instance;
            diagnostics.StartOperation("TestModelConnection");

            try
            {
                // Create a simple request to test connection
                var requestData = new
                {
                    model = _modelName,
                    prompt = "Hello",
                    system = "Respond with 'Connected'",
                    options = new { num_predict = 10 }, // Request a very short response
                    stream = false
                };

                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(OllamaApiUrl, content);

                bool success = response.IsSuccessStatusCode;
                diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                    $"Connection test result: {(success ? "SUCCESS" : "FAILED")} - Status: {response.StatusCode}");

                return success;
            }
            catch (Exception ex)
            {
                diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Connection test failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                diagnostics.EndOperation("TestModelConnection");
            }
        }
    }
}