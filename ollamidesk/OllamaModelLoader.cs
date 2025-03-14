using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace ollamidesk
{
    public static class OllamaModelLoader
    {
        public static IOllamaModel LoadModel(string modelName)
        {
            return new OllamaModel(modelName);
        }
    }

    public class OllamaModel : IOllamaModel
    {
        private readonly string _modelName;
        private readonly HttpClient _httpClient;
        private const string OllamaApiUrl = "http://localhost:11434/api/generate";

        public OllamaModel(string modelName)
        {
            _modelName = modelName;
            _httpClient = new HttpClient();
        }

        public async Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory)
        {
            try
            {
                // Create the request JSON
                var requestData = new
                {
                    model = _modelName,
                    prompt = userInput,
                    stream = false
                };

                // Serialize to JSON
                string jsonContent = System.Text.Json.JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                // Send request to the Ollama API
                var response = await _httpClient.PostAsync(OllamaApiUrl, content);

                // Check if the request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Read and parse JSON response
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                        // Extract just the response text from the JSON
                        if (doc.RootElement.TryGetProperty("response", out var responseElement))
                        {
                            return responseElement.GetString() ?? "No response received";
                        }
                        else
                        {
                            return "Error: Unexpected API response format";
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        return "Error: Invalid JSON response from Ollama API";
                    }
                }
                else
                {
                    return $"Error: API request failed with status {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"An error occurred: {ex.Message}";
            }
        }
    }
}