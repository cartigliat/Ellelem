using System;
using System.Net.Http;
using Microsoft.Extensions.Http;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk
{
    public class OllamaModelLoader
    {
        private readonly OllamaSettings _settings;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IHttpClientFactory _httpClientFactory;

        public OllamaModelLoader(
            OllamaSettings settings,
            RagDiagnosticsService diagnostics,
            IHttpClientFactory httpClientFactory)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

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

            return new OllamaModel(modelName, _settings, _httpClientFactory, _diagnostics);
        }
    }
}