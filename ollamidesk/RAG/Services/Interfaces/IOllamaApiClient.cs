// ollamidesk/RAG/Services/Interfaces/IOllamaApiClient.cs
using System.Threading.Tasks;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for a client interacting with the Ollama HTTP API.
    /// </summary>
    public interface IOllamaApiClient
    {
        /// <summary>
        /// Sends a generation request to the Ollama API.
        /// </summary>
        /// <param name="requestPayload">The JSON serializable request object.</param>
        /// <param name="modelName">The specific model name being targeted (for logging/context).</param>
        /// <returns>The string response content from the model.</returns>
        Task<string> GenerateAsync(object requestPayload, string modelName);

        // /// <summary>
        // /// Sends an embedding request (Consider adding if OllamaModel needs this directly,
        // /// otherwise OllamaEmbeddingService handles its own calls).
        // /// </summary>
        // Task<float[]> EmbedAsync(object requestPayload, string modelName);

        /// <summary>
        /// Tests the connection to the Ollama API (specifically the generate endpoint).
        /// </summary>
        /// <param name="modelName">A model name to use for the test request.</param>
        /// <returns>True if the connection test is successful, false otherwise.</returns>
        Task<bool> TestConnectionAsync(string modelName);
    }
}