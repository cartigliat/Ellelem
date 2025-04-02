// Modified OllamaModel.cs with improved async patterns
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;

namespace ollamidesk
{
    public class OllamaModel : IOllamaModel
    {
        private readonly string _modelName;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;
        private readonly string _systemPrompt;
        private readonly RagDiagnosticsService _diagnostics;

        // Add semaphore for throttling concurrent requests
        private readonly SemaphoreSlim _requestSemaphore;
        private readonly int _maxConcurrentRequests;

        public OllamaModel(
            string modelName,
            OllamaSettings settings,
            IHttpClientFactory httpClientFactory,
            RagDiagnosticsService diagnostics)
        {
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));

            _apiUrl = settings.ApiGenerateEndpoint;
            _maxRetries = settings.MaxRetries;
            _retryDelayMs = settings.RetryDelayMs;
            _systemPrompt = settings.SystemPrompt;

            // In OllamaModel constructor:
            _maxConcurrentRequests = settings.MaxConcurrentRequests; // Directly use the setting
            if (_maxConcurrentRequests <= 0) // Add a check for invalid configuration
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel", $"Invalid MaxConcurrentRequests ({_maxConcurrentRequests}). Using default value of 3.");
                _maxConcurrentRequests = 3;
            }
            _requestSemaphore = new SemaphoreSlim(_maxConcurrentRequests, _maxConcurrentRequests);

            // Use the HttpClientFactory to create a client
            _httpClient = httpClientFactory.CreateClient("OllamaApi");

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                $"Model initialized: {modelName} with HttpClientFactory, max concurrent requests: {_maxConcurrentRequests}");
        }

        public async Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory)
        {
            _diagnostics.StartOperation("GenerateResponse");

            try
            {
                // Prepare the prompt with context
                string prompt = userInput;

                // Add document context if available
                if (!string.IsNullOrEmpty(loadedDocument))
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                        $"Adding document context (length: {loadedDocument.Length} chars)");
                    prompt = $"Context information:\n{loadedDocument}\n\nQuestion: {userInput}";
                }

                // Add chat history for context
                if (chatHistory != null && chatHistory.Count > 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
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

                return await SendRequestToOllamaAsync(jsonContent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel", $"Error generating response: {ex.Message}");
                return $"An error occurred: {ex.Message}";
            }
            finally
            {
                _diagnostics.EndOperation("GenerateResponse");
            }
        }

        public async Task<string> GenerateResponseWithContextAsync(string userInput, List<string> chatHistory, List<DocumentChunk> relevantChunks)
        {
            _diagnostics.StartOperation("GenerateResponseWithContext");

            try
            {
                var contextBuilder = new StringBuilder();

                // Add relevant chunks
                if (relevantChunks != null && relevantChunks.Any())
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
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
                    _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                        "GenerateResponseWithContext called but no relevant chunks provided");
                    contextBuilder.Append(userInput);
                }

                // Log the full context being sent
                string fullContext = contextBuilder.ToString();
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
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

                return await SendRequestToOllamaAsync(jsonContent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Error generating response with context: {ex.Message}");
                return $"An error occurred while processing your question with document context: {ex.Message}";
            }
            finally
            {
                _diagnostics.EndOperation("GenerateResponseWithContext");
            }
        }

        private async Task<string> SendRequestToOllamaAsync(string jsonContent)
        {
            _diagnostics.StartOperation("OllamaApiRequest");

            // Flag to track if we've acquired the semaphore
            bool semaphoreAcquired = false;

            try
            {
                // Throttle concurrent requests using the semaphore
                await _requestSemaphore.WaitAsync().ConfigureAwait(false);
                semaphoreAcquired = true;

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                _diagnostics.LogApiRequest("OllamaModel", _apiUrl,
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) + "..." : jsonContent);

                for (int attempt = 1; attempt <= _maxRetries; attempt++)
                {
                    try
                    {
                        // Send request to the Ollama API
                        var response = await _httpClient.PostAsync(_apiUrl, content).ConfigureAwait(false);

                        // Check if the request was successful
                        if (response.IsSuccessStatusCode)
                        {
                            // Read and parse JSON response
                            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            _diagnostics.LogApiResponse("OllamaModel", _apiUrl,
                                jsonResponse.Length > 500 ? jsonResponse.Substring(0, 500) + "..." : jsonResponse,
                                true);

                            try
                            {
                                using var doc = JsonDocument.Parse(jsonResponse);
                                // Extract just the response text from the JSON
                                if (doc.RootElement.TryGetProperty("response", out var responseElement))
                                {
                                    string responseText = responseElement.GetString() ?? "No response received";
                                    _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                                        $"Response generated successfully (length: {responseText.Length} chars)");
                                    return responseText;
                                }
                                else
                                {
                                    _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                                        "Unexpected API response format - no 'response' field");
                                    return "Error: Unexpected API response format";
                                }
                            }
                            catch (JsonException ex)
                            {
                                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                                    $"Invalid JSON response: {ex.Message}");
                                return "Error: Invalid JSON response from Ollama API";
                            }
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            _diagnostics.LogApiResponse("OllamaModel", _apiUrl, errorContent, false);

                            // Only retry for server errors (5xx)
                            if ((int)response.StatusCode >= 500 && attempt < _maxRetries)
                            {
                                _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                                    $"Server error on attempt {attempt}, will retry after delay: {response.StatusCode}");
                                await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
                                continue;
                            }

                            return $"Error: API request failed with status {response.StatusCode}";
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        if (attempt < _maxRetries)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                                $"Request timeout on attempt {attempt}, will retry");
                            await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
                            continue;
                        }
                        return "Error: Request to Ollama API timed out after multiple attempts";
                    }
                    catch (Exception ex) when (attempt < _maxRetries)
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "OllamaModel",
                            $"Error on attempt {attempt}, will retry: {ex.Message}");
                        await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
                    }
                }

                return "Error: Failed to get a response from Ollama API after multiple attempts";
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Unhandled exception in SendRequestToOllamaAsync: {ex.Message}");
                return $"An error occurred: {ex.Message}";
            }
            finally
            {
                // Release the semaphore only if we acquired it
                if (semaphoreAcquired)
                {
                    _requestSemaphore.Release();
                }

                _diagnostics.EndOperation("OllamaApiRequest");
            }
        }

        // Helper method for testing the API connection
        public async Task<bool> TestConnectionAsync()
        {
            _diagnostics.StartOperation("TestModelConnection");

            // Flag to track if we've acquired the semaphore
            bool semaphoreAcquired = false;

            try
            {
                // Use the semaphore for throttling connection tests too
                await _requestSemaphore.WaitAsync().ConfigureAwait(false);
                semaphoreAcquired = true;

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

                var response = await _httpClient.PostAsync(_apiUrl, content).ConfigureAwait(false);

                bool success = response.IsSuccessStatusCode;
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaModel",
                    $"Connection test result: {(success ? "SUCCESS" : "FAILED")} - Status: {response.StatusCode}");

                return success;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaModel",
                    $"Connection test failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                // Release the semaphore only if we acquired it
                if (semaphoreAcquired)
                {
                    _requestSemaphore.Release();
                }

                _diagnostics.EndOperation("TestModelConnection");
            }
        }
    }
}