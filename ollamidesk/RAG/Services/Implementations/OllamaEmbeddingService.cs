using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces; // Add this import

namespace ollamidesk.RAG.Services.Implementations
{
    public class OllamaEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;
        private readonly string _apiUrl;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;
        private readonly RagDiagnosticsService _diagnostics;

        public OllamaEmbeddingService(
            OllamaSettings settings,
            IHttpClientFactory httpClientFactory,
            RagDiagnosticsService diagnostics)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));

            _modelName = settings.EmbeddingModel;
            _apiUrl = settings.ApiEmbeddingsEndpoint;
            _maxRetries = settings.MaxRetries;
            _retryDelayMs = settings.RetryDelayMs;

            // Use the HttpClientFactory to create a client
            _httpClient = httpClientFactory.CreateClient("OllamaApi");

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaEmbeddingService",
                $"Initialized with model: {_modelName}, API URL: {_apiUrl}");
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be empty or whitespace", nameof(text));
            }

            _diagnostics.StartOperation("GenerateEmbedding");

            try
            {
                // Trim text to avoid issues with very long text
                string trimmedText = text;
                if (text.Length > 8192)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                        $"Text length ({text.Length}) exceeds 8192 characters. Trimming to 8192 characters.");
                    trimmedText = text.Substring(0, 8192);
                }

                var requestData = new { model = _modelName, prompt = trimmedText };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _diagnostics.LogApiRequest("EmbeddingService", _apiUrl, jsonContent);

                for (int attempt = 1; attempt <= _maxRetries; attempt++)
                {
                    try
                    {
                        // Send the request
                        var response = await _httpClient.PostAsync(_apiUrl, content);

                        // Parse and return response on success
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonResponse = await response.Content.ReadAsStringAsync();
                            _diagnostics.LogApiResponse("EmbeddingService", _apiUrl,
                                jsonResponse.Length > 1000 ? jsonResponse.Substring(0, 1000) + "..." : jsonResponse,
                                true);

                            using var doc = JsonDocument.Parse(jsonResponse);

                            if (doc.RootElement.TryGetProperty("embedding", out var embeddingElement))
                            {
                                var embedding = new List<float>();
                                foreach (var item in embeddingElement.EnumerateArray())
                                {
                                    embedding.Add(item.GetSingle());
                                }

                                float[] embeddingArray = embedding.ToArray();
                                _diagnostics.LogEmbeddingVector("EmbeddingService", text, embeddingArray);
                                return embeddingArray;
                            }

                            throw new Exception("No embedding found in response");
                        }

                        // Handle API errors
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _diagnostics.LogApiResponse("EmbeddingService", _apiUrl, errorContent, false);

                        // Only retry for server errors (5xx)
                        if ((int)response.StatusCode >= 500 && attempt < _maxRetries)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                                $"Server error on attempt {attempt}, will retry after delay: {response.StatusCode}");
                            await Task.Delay(_retryDelayMs * attempt);
                            continue;
                        }

                        throw new HttpRequestException(
                            $"API request failed with status {response.StatusCode}: {errorContent}");
                    }
                    catch (TaskCanceledException)
                    {
                        if (attempt < _maxRetries)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                                $"Request timeout on attempt {attempt}, will retry");
                            await Task.Delay(_retryDelayMs * attempt);
                            continue;
                        }
                        throw new TimeoutException("Embedding API request timed out after multiple attempts");
                    }
                    catch (Exception ex) when (ex is not HttpRequestException && attempt < _maxRetries)
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                            $"Error on attempt {attempt}, will retry: {ex.Message}");
                        await Task.Delay(_retryDelayMs * attempt);
                    }
                }

                throw new Exception("Failed to generate embedding after maximum retry attempts");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "EmbeddingService",
                    $"Failed to generate embedding: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("GenerateEmbedding");
            }
        }

        // Helper method for testing the API connection
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _diagnostics.StartOperation("TestEmbeddingConnection");

                var requestData = new { model = _modelName, prompt = "test" };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_apiUrl, content);

                bool success = response.IsSuccessStatusCode;
                _diagnostics.Log(DiagnosticLevel.Info, "EmbeddingService",
                    $"Connection test result: {(success ? "SUCCESS" : "FAILED")} - Status: {response.StatusCode}");

                return success;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "EmbeddingService",
                    $"Connection test failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                _diagnostics.EndOperation("TestEmbeddingConnection");
            }
        }
    }
}