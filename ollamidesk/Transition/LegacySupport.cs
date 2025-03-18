using System;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.Transition
{
    /// <summary>
    /// Provides backward compatibility utilities during the transition to Dependency Injection
    /// </summary>
    public static class LegacySupport
    {
        // Temporary diagnostics service that can be used during transition
        public static RagDiagnosticsService CreateDiagnosticsService()
        {
            var settings = new DiagnosticsSettings
            {
                EnableDiagnostics = true,
                DiagnosticLevel = "Info",
                LogFilePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OllamaDesk",
                    "rag_diagnostics.log")
            };
            
            return new RagDiagnosticsService(settings);
        }
        
        // Create default settings for Ollama
        public static OllamaSettings CreateOllamaSettings()
        {
            return new OllamaSettings
            {
                ApiBaseUrl = "http://localhost:11434",
                DefaultModel = "llama2",
                EmbeddingModel = "nomic-embed-text",
                TimeoutSeconds = 60,
                MaxRetries = 3,
                RetryDelayMs = 1000,
                SystemPrompt = "You are a helpful AI assistant. If context information is provided, use it to answer the question accurately. " +
                    "If there are multiple relevant pieces of information, synthesize them into a coherent answer. " +
                    "If you don't know the answer based on the provided context, say you don't have enough information."
            };
        }
        
        // Create default storage settings
        public static StorageSettings CreateStorageSettings(string? basePath = null)
        {
            var settings = new StorageSettings();
            if (basePath != null)
            {
                settings.BasePath = basePath;
            }
            return settings;
        }
        
        // Create default rag settings
        public static RagSettings CreateRagSettings(
            int chunkSize = 500,
            int chunkOverlap = 100,
            int maxRetrievedChunks = 5,
            float minSimilarityScore = 0.1f)
        {
            return new RagSettings
            {
                ChunkSize = chunkSize,
                ChunkOverlap = chunkOverlap,
                MaxRetrievedChunks = maxRetrievedChunks,
                MinSimilarityScore = minSimilarityScore
            };
        }
    }
}