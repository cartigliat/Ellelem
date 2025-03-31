using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;

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

        // Add semaphore for throttling concurrent embedding requests
        private readonly SemaphoreSlim _requestSemaphore;
        private readonly int _maxConcurrentRequests = 3; // Default limit for concurrent embedding requests

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

            // Initialize request throttling
            _maxConcurrentRequests = settings.MaxConcurrentRequests > 0 ?
                settings.MaxConcurrentRequests : 3; // Default to 3 if not specified
            _requestSemaphore = new SemaphoreSlim(_maxConcurrentRequests, _maxConcurrentRequests);

            // Use the HttpClientFactory to create a client
            _httpClient = httpClientFactory.CreateClient("OllamaApi");

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaEmbeddingService",
                $"Initialized with model: {_modelName}, API URL: {_apiUrl}, max concurrent requests: {_maxConcurrentRequests}");
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be empty or whitespace", nameof(text));
            }

            _diagnostics.StartOperation("GenerateEmbedding");

            // Flag to track if we've acquired the semaphore
            bool semaphoreAcquired = false;

            try
            {
                // Wait for a semaphore slot
                await _requestSemaphore.WaitAsync().ConfigureAwait(false);
                semaphoreAcquired = true;

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
                        var response = await _httpClient.PostAsync(_apiUrl, content).ConfigureAwait(false);

                        // Parse and return response on success
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
                        string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        _diagnostics.LogApiResponse("EmbeddingService", _apiUrl, errorContent, false);

                        // Only retry for server errors (5xx)
                        if ((int)response.StatusCode >= 500 && attempt < _maxRetries)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                                $"Server error on attempt {attempt}, will retry after delay: {response.StatusCode}");
                            await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
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
                            await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
                            continue;
                        }
                        throw new TimeoutException("Embedding API request timed out after multiple attempts");
                    }
                    catch (Exception ex) when (ex is not HttpRequestException && attempt < _maxRetries)
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                            $"Error on attempt {attempt}, will retry: {ex.Message}");
                        await Task.Delay(_retryDelayMs * attempt).ConfigureAwait(false);
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
                // Release the semaphore only if we acquired it
                if (semaphoreAcquired)
                {
                    _requestSemaphore.Release();
                }

                _diagnostics.EndOperation("GenerateEmbedding");
            }
        }

        // Helper method for testing the API connection
        public async Task<bool> TestConnectionAsync()
        {
            _diagnostics.StartOperation("TestEmbeddingConnection");

            // Flag to track if we've acquired the semaphore
            bool semaphoreAcquired = false;

            try
            {
                // Try to acquire semaphore with a timeout to avoid blocking for too long
                await _requestSemaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                semaphoreAcquired = true;

                var requestData = new { model = _modelName, prompt = "test" };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_apiUrl, content).ConfigureAwait(false);

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
                // Release the semaphore only if we acquired it
                if (semaphoreAcquired)
                {
                    _requestSemaphore.Release();
                }

                _diagnostics.EndOperation("TestEmbeddingConnection");
            }
        }

        // Add IDisposable implementation to clean up resources
        public void Dispose()
        {
            _requestSemaphore?.Dispose();
        }
    }
}