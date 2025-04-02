// ollamidesk/RAG/Services/Interfaces/IContentStore.cs
// New file
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Interface for storing and retrieving document content and embeddings.
    /// </summary>
    public interface IContentStore : IDisposable
    {
        /// <summary>
        /// Loads the text content of a document.
        /// </summary>
        Task<string> LoadContentAsync(string documentId);

        /// <summary>
        /// Saves the text content of a document.
        /// </summary>
        Task SaveContentAsync(string documentId, string content);

        /// <summary>
        /// Deletes the text content file for a document.
        /// </summary>
        Task DeleteContentAsync(string documentId);

        /// <summary>
        /// Loads the serialized chunks (including embeddings) for a document.
        /// </summary>
        Task<List<DocumentChunk>?> LoadEmbeddingsAsync(string documentId);

        /// <summary>
        /// Saves the serialized chunks (including embeddings) for a document.
        /// </summary>
        Task SaveEmbeddingsAsync(string documentId, List<DocumentChunk> chunks);

        /// <summary>
        /// Deletes the embeddings file for a document.
        /// </summary>
        Task DeleteEmbeddingsAsync(string documentId);
    }
}