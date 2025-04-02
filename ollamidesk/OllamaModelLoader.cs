// ollamidesk/OllamaModelLoader.cs
using System;
// using System.Net.Http; // No longer needed here
// using Microsoft.Extensions.Http; // No longer needed here (unless used elsewhere)
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces; // <-- Added for IOllamaApiClient

namespace ollamidesk
{
    // Note: Consider if OllamaModelLoader is still needed now that OllamaModelFactory exists
    // and is registered with DI. The factory might be sufficient.
    public class OllamaModelLoader
    {
        private readonly OllamaSettings _settings;
        private readonly RagDiagnosticsService _diagnostics;
        // private readonly IHttpClientFactory _httpClientFactory; // <-- REMOVED
        private readonly IOllamaApiClient _apiClient; // <-- ADDED

        public OllamaModelLoader(
            OllamaSettings settings,
            RagDiagnosticsService diagnostics,
            // IHttpClientFactory httpClientFactory) // <-- REMOVED Parameter
            IOllamaApiClient apiClient) // <-- ADDED Parameter
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            // _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)); // <-- REMOVED Assignment
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient)); // <-- ADDED Assignment


            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModelLoader",
                "OllamaModelLoader initialized with dependency injection");
        }

        public IOllamaModel LoadModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                throw new ArgumentException("Model name cannot be empty", nameof(modelName));
            }

            _diagnostics.Log(DiagnosticLevel.Info, "OllamaModelLoader",
                $"Loading model: {modelName}");

            // Pass the injected _apiClient instead of _httpClientFactory
            // return new OllamaModel(modelName, _settings, _httpClientFactory, _diagnostics); // <-- OLD
            return new OllamaModel(modelName, _settings, _apiClient, _diagnostics); // <-- CORRECTED
        }
    }
}