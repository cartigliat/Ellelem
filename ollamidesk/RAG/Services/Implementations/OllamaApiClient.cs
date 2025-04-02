// ollamidesk/RAG/Services/Implementations/OllamaApiClient.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces; // Added interface namespace
using ollamidesk.RAG.Exceptions; // Added for specific exceptions

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation for interacting with the Ollama HTTP API.
    /// Handles request sending, response parsing, error handling, and throttling.
    /// </summary>
    public class OllamaApiClient : IOllamaApiClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly OllamaSettings _settings;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly SemaphoreSlim _requestSemaphore;
        private readonly string _generateEndpoint;
        private bool _disposedValue;


        public OllamaApiClient(
            IHttpClientFactory httpClientFactory,
            OllamaSettings settings,
            RagDiagnosticsService diagnostics)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));

            _httpClient = httpClientFactory.CreateClient("OllamaApi"); // Use named client
            _generateEndpoint = _settings.ApiGenerateEndpoint; // Store specific endpoint

            // Configure throttling semaphore based on settings
            int maxConcurrent = _settings.MaxConcurrentRequests > 0 ? _settings.MaxConcurrentRequests : 3; // Default to 3 if invalid
            _requestSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            _diagnostics.Log(DiagnosticLevel.Info, "OllamaApiClient", $"Initialized with Generate Endpoint: {_generateEndpoint}, Max Concurrent Requests: {maxConcurrent}");
        }

        public async Task<string> GenerateAsync(object requestPayload, string modelName)
        {
            string jsonPayload = JsonSerializer.Serialize(requestPayload);
            return await SendRequestInternalAsync(_generateEndpoint, jsonPayload, modelName);
        }

        public async Task<bool> TestConnectionAsync(string modelName)
        {
            _diagnostics.StartOperation("ApiClient.TestConnection");
            bool semaphoreAcquired = false;
            try
            {
                // Use a short timeout for connection test semaphore
                semaphoreAcquired = await _requestSemaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (!semaphoreAcquired)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "OllamaApiClient", "TestConnection: Timeout waiting for semaphore.");
                    return false;
                }

                // Simple request payload for testing
                var testPayload = new { model = modelName, prompt = "Hi", stream = false, options = new { num_predict = 1 } };
                string jsonContent = JsonSerializer.Serialize(testPayload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Use a shorter timeout for the actual HTTP request during test
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); // 15 second timeout for test
                var response = await _httpClient.PostAsync(_generateEndpoint, content, cts.Token).ConfigureAwait(false);

                bool success = response.IsSuccessStatusCode;
                _diagnostics.Log(DiagnosticLevel.Info, "OllamaApiClient",
                    $"Connection test result for model '{modelName}': {(success ? "SUCCESS" : "FAILED")} - Status: {response.StatusCode}");
                return success;
            }
            catch (OperationCanceledException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", $"Connection test for model '{modelName}' timed out.");
                return false;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient",
                    $"Connection test for model '{modelName}' failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                if (semaphoreAcquired) _requestSemaphore.Release();
                _diagnostics.EndOperation("ApiClient.TestConnection");
            }
        }


        private async Task<string> SendRequestInternalAsync(string endpointUrl, string jsonPayload, string modelName)
        {
            _diagnostics.StartOperation($"ApiClient.SendRequest ({modelName})");
            bool semaphoreAcquired = false;
            try
            {
                // Wait for semaphore slot
                await _requestSemaphore.WaitAsync().ConfigureAwait(false);
                semaphoreAcquired = true;
                _diagnostics.Log(DiagnosticLevel.Debug, "OllamaApiClient", $"Semaphore acquired for {modelName}");


                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                _diagnostics.LogApiRequest("OllamaApiClient", endpointUrl, jsonPayload.Length > 500 ? jsonPayload.Substring(0, 500) + "..." : jsonPayload);


                // The retry logic is handled by Polly policies configured on the HttpClient
                // No need for manual retry loop here if HttpClientConfiguration is set up correctly.

                HttpResponseMessage response;
                try
                {
                    // Make the API call - rely on HttpClientFactory policies for retries/timeouts set in HttpClientConfiguration
                    response = await _httpClient.PostAsync(endpointUrl, content).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex) // Catches timeouts if configured correctly
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", $"Request to {endpointUrl} timed out for model {modelName}. Exception: {ex.Message}");
                    throw new DocumentProcessingException($"Request to Ollama timed out for model {modelName}.", ex);
                }
                catch (HttpRequestException ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", $"HTTP request error for {modelName} to {endpointUrl}: {ex.Message}");
                    throw new DocumentProcessingException($"Ollama API request failed for model {modelName}.", ex);
                }


                string responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _diagnostics.LogApiResponse("OllamaApiClient", endpointUrl, responseString.Length > 500 ? responseString.Substring(0, 500) + "..." : responseString, true);
                    try
                    {
                        using var doc = JsonDocument.Parse(responseString);
                        if (doc.RootElement.TryGetProperty("response", out var responseElement))
                        {
                            string responseText = responseElement.GetString() ?? string.Empty;
                            _diagnostics.Log(DiagnosticLevel.Debug, "OllamaApiClient", $"Successfully parsed response for {modelName}. Length: {responseText.Length}");
                            return responseText;
                        }
                        else
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", "API response missing 'response' field.");
                            throw new DocumentProcessingException("Ollama API response format error: Missing 'response' field.");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", $"Invalid JSON response for {modelName}: {ex.Message}");
                        throw new DocumentProcessingException("Invalid JSON response from Ollama API.", ex);
                    }
                }
                else
                {
                    // Handle API error responses
                    _diagnostics.LogApiResponse("OllamaApiClient", endpointUrl, responseString, false);
                    _diagnostics.Log(DiagnosticLevel.Error, "OllamaApiClient", $"API error for {modelName}. Status: {response.StatusCode}. Response: {responseString}");
                    throw new DocumentProcessingException($"Ollama API request failed for model {modelName} with status {response.StatusCode}. Response: {responseString}");
                }
            }
            catch (Exception ex) when (ex is not DocumentProcessingException)
            {
                // Catch any other unexpected errors
                _diagnostics.Log(DiagnosticLevel.Critical, "OllamaApiClient", $"Unexpected error sending request for {modelName} to {endpointUrl}: {ex.Message}");
                throw new DocumentProcessingException($"Unexpected error communicating with Ollama for model {modelName}.", ex);
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _requestSemaphore.Release();
                    _diagnostics.Log(DiagnosticLevel.Debug, "OllamaApiClient", $"Semaphore released for {modelName}");
                }
                _diagnostics.EndOperation($"ApiClient.SendRequest ({modelName})");
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _requestSemaphore?.Dispose();
                    // _httpClient is managed by HttpClientFactory, no need to dispose here
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}