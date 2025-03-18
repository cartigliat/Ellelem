using System;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.Services
{
    /// <summary>
    /// Factory for creating OllamaModel instances
    /// </summary>
    public class OllamaModelFactory
    {
        private readonly OllamaSettings _settings;
        private readonly RagDiagnosticsService _diagnostics;

        /// <summary>
        /// Initializes a new instance of the OllamaModelFactory class
        /// </summary>
        /// <param name="settings">Ollama API settings</param>
        /// <param name="diagnostics">Diagnostics service</param>
        public OllamaModelFactory(OllamaSettings settings, RagDiagnosticsService diagnostics)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

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

            return new OllamaModel(modelName, _settings, _diagnostics);
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

            return CreateModel(defaultModel);
        }
    }
}