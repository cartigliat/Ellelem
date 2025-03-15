using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.Services
{
    public class OllamaEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;
        private const string OllamaApiUrl = "http://localhost:11434/api/embeddings";
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;

        public OllamaEmbeddingService(string modelName = "nomic-embed-text")
        {
            _modelName = modelName;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Increase timeout
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be empty or whitespace", nameof(text));
            }

            var diagnostics = RagDiagnostics.Instance;
            diagnostics.StartOperation("GenerateEmbedding");

            try
            {
                // Trim text to avoid issues with very long text
                string trimmedText = text;
                if (text.Length > 8192)
                {
                    diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                        $"Text length ({text.Length}) exceeds 8192 characters. Trimming to 8192 characters.");
                    trimmedText = text.Substring(0, 8192);
                }

                var requestData = new { model = _modelName, prompt = trimmedText };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                diagnostics.LogApiRequest("EmbeddingService", OllamaApiUrl, jsonContent);

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        // Send the request
                        var response = await _httpClient.PostAsync(OllamaApiUrl, content);

                        // Parse and return response on success
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonResponse = await response.Content.ReadAsStringAsync();
                            diagnostics.LogApiResponse("EmbeddingService", OllamaApiUrl,
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
                                diagnostics.LogEmbeddingVector("EmbeddingService", text, embeddingArray);
                                return embeddingArray;
                            }

                            throw new Exception("No embedding found in response");
                        }

                        // Handle API errors
                        string errorContent = await response.Content.ReadAsStringAsync();
                        diagnostics.LogApiResponse("EmbeddingService", OllamaApiUrl, errorContent, false);

                        // Only retry for server errors (5xx)
                        if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
                        {
                            diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                                $"Server error on attempt {attempt}, will retry after delay: {response.StatusCode}");
                            await Task.Delay(RetryDelayMs * attempt);
                            continue;
                        }

                        throw new HttpRequestException(
                            $"API request failed with status {response.StatusCode}: {errorContent}");
                    }
                    catch (TaskCanceledException)
                    {
                        if (attempt < MaxRetries)
                        {
                            diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                                $"Request timeout on attempt {attempt}, will retry");
                            await Task.Delay(RetryDelayMs * attempt);
                            continue;
                        }
                        throw new TimeoutException("Embedding API request timed out after multiple attempts");
                    }
                    catch (Exception ex) when (ex is not HttpRequestException && attempt < MaxRetries)
                    {
                        diagnostics.Log(DiagnosticLevel.Warning, "EmbeddingService",
                            $"Error on attempt {attempt}, will retry: {ex.Message}");
                        await Task.Delay(RetryDelayMs * attempt);
                    }
                }

                throw new Exception("Failed to generate embedding after maximum retry attempts");
            }
            catch (Exception ex)
            {
                diagnostics.Log(DiagnosticLevel.Error, "EmbeddingService",
                    $"Failed to generate embedding: {ex.Message}");
                throw;
            }
            finally
            {
                diagnostics.EndOperation("GenerateEmbedding");
            }
        }

        // Helper method for testing the API connection
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var diagnostics = RagDiagnostics.Instance;
                diagnostics.StartOperation("TestEmbeddingConnection");

                var requestData = new { model = _modelName, prompt = "test" };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(OllamaApiUrl, content);

                bool success = response.IsSuccessStatusCode;
                diagnostics.Log(DiagnosticLevel.Info, "EmbeddingService",
                    $"Connection test result: {(success ? "SUCCESS" : "FAILED")} - Status: {response.StatusCode}");

                return success;
            }
            catch (Exception ex)
            {
                RagDiagnostics.Instance.Log(DiagnosticLevel.Error, "EmbeddingService",
                    $"Connection test failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                RagDiagnostics.Instance.EndOperation("TestEmbeddingConnection");
            }
        }
    }
}