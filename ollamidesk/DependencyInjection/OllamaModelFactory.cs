// ollamidesk/DependencyInjection/OllamaModelFactory.cs
using System;
// using System.Net.Http; // No longer needed here
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces; // <-- Added for IOllamaApiClient

namespace ollamidesk.Services
{
    /// <summary>
    /// Factory for creating OllamaModel instances
    /// </summary>
    public class OllamaModelFactory
    {
        private readonly OllamaSettings _settings;
        private readonly RagDiagnosticsService _diagnostics;
        // private readonly IHttpClientFactory _httpClientFactory; // <-- REMOVED
        private readonly IOllamaApiClient _apiClient; // <-- ADDED

        /// <summary>
        /// Initializes a new instance of the OllamaModelFactory class
        /// </summary>
        /// <param name="settings">Ollama API settings</param>
        /// <param name="diagnostics">Diagnostics service</param>
        /// <param name="apiClient">Ollama API client service</param> // <-- UPDATED Parameter
        public OllamaModelFactory(
            OllamaSettings settings,
            RagDiagnosticsService diagnostics,
            // IHttpClientFactory httpClientFactory) // <-- REMOVED Parameter
            IOllamaApiClient apiClient) // <-- ADDED Parameter
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            // _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)); // <-- REMOVED Assignment
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient)); // <-- ADDED Assignment

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModelFactory",
                $"Factory initialized with API URL: {_settings.ApiBaseUrl}");
        }

        /// <summary>
        /// Creates an OllamaModel with the specified model name
        /// </summary>
        /// <param name="modelName">The name of the model to create</param>
        /// <returns>An IOllamaModel instance</returns>
        public IOllamaModel CreateModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                throw new ArgumentException("Model name cannot be empty", nameof(modelName));
            }

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModelFactory",
                $"Creating model instance: {modelName}");

            // Pass the injected _apiClient instead of _httpClientFactory
            // return new OllamaModel(modelName, _settings, _httpClientFactory, _diagnostics); // <-- OLD
            return new OllamaModel(modelName, _settings, _apiClient, _diagnostics); // <-- CORRECTED
        }

        /// <summary>
        /// Creates an OllamaModel with the default model name from settings
        /// </summary>
        /// <returns>An IOllamaModel instance</returns>
        public IOllamaModel CreateDefaultModel()
        {
            string defaultModel = _settings.DefaultModel;

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModelFactory",
                $"Creating default model instance: {defaultModel}");

            return CreateModel(defaultModel); // This now correctly uses the updated CreateModel above
        }
    }
}