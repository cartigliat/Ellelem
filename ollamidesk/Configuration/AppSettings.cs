using System;
using System.IO;

namespace ollamidesk.Configuration
{
    /// <summary>
    /// Root configuration class for the application.
    /// Note: For RAG-specific configuration, IRagConfigurationService should be used
    /// as the central configuration point rather than directly modifying AppSettings.
    /// Default values can be created through ConfigurationFactory if needed.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Ollama API configuration settings
        /// </summary>
        public OllamaSettings Ollama { get; set; } = new OllamaSettings();

        /// <summary>
        /// RAG functionality configuration
        /// These settings should preferably be managed through IRagConfigurationService
        /// </summary>
        public RagSettings Rag { get; set; } = new RagSettings();

        /// <summary>
        /// Storage location configuration
        /// </summary>
        public StorageSettings Storage { get; set; } = new StorageSettings();

        /// <summary>
        /// Diagnostics and logging configuration
        /// </summary>
        public DiagnosticsSettings Diagnostics { get; set; } = new DiagnosticsSettings();
    }

    /// <summary>
    /// Configuration settings for Ollama API
    /// </summary>
    public class OllamaSettings
    {
        public string ApiBaseUrl { get; set; } = "http://localhost:11434";
        public string DefaultModel { get; set; } = "llama3.2:1b";
        public string EmbeddingModel { get; set; } = "nomic-embed-text";
        public int TimeoutSeconds { get; set; } = 60;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum number of concurrent requests to the API
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 3;

        /// <summary>
        /// API endpoint for generation requests
        /// </summary>
        public string ApiGenerateEndpoint => $"{ApiBaseUrl}/api/generate";

        /// <summary>
        /// API endpoint for embedding generation
        /// </summary>
        public string ApiEmbeddingsEndpoint => $"{ApiBaseUrl}/api/embeddings";

        /// <summary>
        /// Default system prompt for the model
        /// </summary>
        public string SystemPrompt { get; set; } = "You are a helpful AI assistant. If context information is provided, use it to answer the question accurately. " +
            "If there are multiple relevant pieces of information, synthesize them into a coherent answer. " +
            "If you don't know the answer based on the provided context, say you don't have enough information.";
    }

    /// <summary>
    /// Configuration settings for RAG functionality.
    /// This class defines default values, but at runtime these should be
    /// managed through IRagConfigurationService.
    /// </summary>
    public class RagSettings
    {
        /// <summary>
        /// Size of text chunks in characters
        /// </summary>
        public int ChunkSize { get; set; } = 400;

        /// <summary>
        /// Overlap between consecutive chunks in characters
        /// </summary>
        public int ChunkOverlap { get; set; } = 50;

        /// <summary>
        /// Maximum number of chunks to retrieve for a query
        /// </summary>
        public int MaxRetrievedChunks { get; set; } = 4;

        /// <summary>
        /// Minimum similarity score for a chunk to be considered relevant
        /// </summary>
        public float MinSimilarityScore { get; set; } = 0.3f;
    }

    /// <summary>
    /// Configuration settings for storage
    /// </summary>
    public class StorageSettings
    {
        private string _basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OllamaDesk");

        /// <summary>
        /// Base storage path for all application data
        /// </summary>
        public string BasePath
        {
            get => _basePath;
            set => _basePath = value;
        }

        /// <summary>
        /// Path for document storage
        /// </summary>
        public string DocumentsFolder => Path.Combine(BasePath, "documents");

        /// <summary>
        /// Path for vector data storage
        /// </summary>
        public string VectorsFolder => Path.Combine(BasePath, "vectors");

        /// <summary>
        /// Path for embeddings storage
        /// </summary>
        public string EmbeddingsFolder => Path.Combine(BasePath, "embeddings");

        /// <summary>
        /// Path to document metadata file
        /// </summary>
        public string MetadataFile => Path.Combine(BasePath, "library.json");
    }

    /// <summary>
    /// Configuration settings for diagnostics
    /// </summary>
    public class DiagnosticsSettings
    {
        /// <summary>
        /// Whether diagnostics logging is enabled
        /// </summary>
        public bool EnableDiagnostics { get; set; } = true;

        /// <summary>
        /// Minimum log level: Debug, Info, Warning, Error, or Critical
        /// </summary>
        public string DiagnosticLevel { get; set; } = "Info";

        /// <summary>
        /// Path to log file
        /// </summary>
        public string LogFilePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OllamaDesk",
            "rag_diagnostics.log");
    }
}