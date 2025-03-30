using System;
using System.IO;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.Services
{
    /// <summary>
    /// Factory class for creating and managing application configuration objects.
    /// For RAG-specific configuration, use IRagConfigurationService instead.
    /// </summary>
    public class ConfigurationFactory
    {
        /// <summary>
        /// Creates a diagnostics service with default or custom settings.
        /// </summary>
        /// <param name="customSettings">Optional custom diagnostics settings. If null, default settings are used.</param>
        /// <returns>A configured diagnostics service instance</returns>
        public static RagDiagnosticsService CreateDiagnosticsService(DiagnosticsSettings? customSettings = null)
        {
            var settings = customSettings ?? CreateDefaultDiagnosticsSettings();
            return new RagDiagnosticsService(settings);
        }

        /// <summary>
        /// Creates default diagnostics settings.
        /// </summary>
        /// <returns>Default diagnostics settings</returns>
        public static DiagnosticsSettings CreateDefaultDiagnosticsSettings()
        {
            return new DiagnosticsSettings
            {
                EnableDiagnostics = true,
                DiagnosticLevel = "Info",
                LogFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OllamaDesk",
                    "rag_diagnostics.log")
            };
        }

        /// <summary>
        /// Creates Ollama API settings with default or custom values.
        /// </summary>
        /// <param name="overrideDefaults">Action to customize the default settings</param>
        /// <returns>Configured Ollama settings</returns>
        public static OllamaSettings CreateOllamaSettings(Action<OllamaSettings>? overrideDefaults = null)
        {
            var settings = new OllamaSettings
            {
                ApiBaseUrl = "http://localhost:11434",
                DefaultModel = "llama3.2:1b",
                EmbeddingModel = "nomic-embed-text",
                TimeoutSeconds = 60,
                MaxRetries = 3,
                RetryDelayMs = 1000,
                MaxConcurrentRequests = 3,
                SystemPrompt = "You are a helpful AI assistant. If context information is provided, use it to answer the question accurately. " +
                    "If there are multiple relevant pieces of information, synthesize them into a coherent answer. " +
                    "If you don't know the answer based on the provided context, say you don't have enough information."
            };

            overrideDefaults?.Invoke(settings);
            return settings;
        }

        /// <summary>
        /// Creates storage settings with optional custom base path.
        /// </summary>
        /// <param name="basePath">Optional custom base path for storage</param>
        /// <returns>Configured storage settings</returns>
        public static StorageSettings CreateStorageSettings(string? basePath = null)
        {
            var settings = new StorageSettings();
            if (basePath != null)
            {
                settings.BasePath = basePath;
            }
            return settings;
        }

        /// <summary>
        /// Creates a basic application settings object.
        /// Note: RAG configuration should be managed through IRagConfigurationService.
        /// </summary>
        /// <returns>Application settings with basic configuration</returns>
        public static AppSettings CreateAppSettings()
        {
            return new AppSettings
            {
                Ollama = CreateOllamaSettings(),
                Storage = CreateStorageSettings(),
                Diagnostics = CreateDefaultDiagnosticsSettings()
                // RAG settings are initialized with defaults from RagSettings class
                // and should be managed through IRagConfigurationService
            };
        }
    }
}