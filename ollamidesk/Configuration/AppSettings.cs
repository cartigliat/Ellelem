using System;
using System.IO;

namespace ollamidesk.Configuration
{
    /// <summary>
    /// Root configuration class for the application
    /// </summary>
    public class AppSettings
    {
        public OllamaSettings Ollama { get; set; } = new OllamaSettings();
        public RagSettings Rag { get; set; } = new RagSettings();
        public StorageSettings Storage { get; set; } = new StorageSettings();
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

        public string ApiGenerateEndpoint => $"{ApiBaseUrl}/api/generate";
        public string ApiEmbeddingsEndpoint => $"{ApiBaseUrl}/api/embeddings";

        public string SystemPrompt { get; set; } = "You are a helpful AI assistant. If context information is provided, use it to answer the question accurately. " +
            "If there are multiple relevant pieces of information, synthesize them into a coherent answer. " +
            "If you don't know the answer based on the provided context, say you don't have enough information.";
    }

    /// <summary>
    /// Configuration settings for RAG functionality
    /// </summary>
    public class RagSettings
    {
        public int ChunkSize { get; set; } = 500;
        public int ChunkOverlap { get; set; } = 100;
        public int MaxRetrievedChunks { get; set; } = 4;
        public float MinSimilarityScore { get; set; } = 0.1f;
    }

    /// <summary>
    /// Configuration settings for storage
    /// </summary>
    public class StorageSettings
    {
        private string _basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OllamaDesk");

        public string BasePath
        {
            get => _basePath;
            set => _basePath = value;
        }

        public string DocumentsFolder => Path.Combine(BasePath, "documents");
        public string VectorsFolder => Path.Combine(BasePath, "vectors");
        public string EmbeddingsFolder => Path.Combine(BasePath, "embeddings");
        public string MetadataFile => Path.Combine(BasePath, "library.json");
    }

    /// <summary>
    /// Configuration settings for diagnostics
    /// </summary>
    public class DiagnosticsSettings
    {
        public bool EnableDiagnostics { get; set; } = true;
        public string DiagnosticLevel { get; set; } = "Info";
        public string LogFilePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OllamaDesk",
            "rag_diagnostics.log");
    }
}