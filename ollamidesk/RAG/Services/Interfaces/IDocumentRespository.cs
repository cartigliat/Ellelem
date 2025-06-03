using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Interface for document repository operations
    /// </summary>
    public interface IDocumentRepository
    {
        /// <summary>
        /// Gets all documents (metadata only)
        /// </summary>
        /// <returns>List of documents without content/chunks loaded</returns>
        Task<List<Document>> GetAllDocumentsAsync();

        /// <summary>
        /// Gets a document by ID (metadata only)
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <returns>Document without content/chunks loaded</returns>
        Task<Document> GetDocumentByIdAsync(string id);

        /// <summary>
        /// Loads a document with full content and chunks
        /// </summary>
        /// <param name="documentId">Document ID</param>
        /// <returns>Document with content and chunks loaded</returns>
        Task<Document> LoadFullContentAsync(string documentId);

        /// <summary>
        /// Saves a document (metadata, content, and chunks)
        /// </summary>
        /// <param name="document">Document to save</param>
        Task SaveDocumentAsync(Document document);

        /// <summary>
        /// Deletes a document and all associated data
        /// </summary>
        /// <param name="id">Document ID</param>
        Task DeleteDocumentAsync(string id);

        /// <summary>
        /// Gets a chunk by ID with optional document filtering for security/performance
        /// </summary>
        /// <param name="chunkId">The chunk ID to find</param>
        /// <param name="allowedDocumentIds">Optional list of document IDs to restrict search to. If null, searches all documents.</param>
        /// <returns>The chunk if found and allowed, null otherwise</returns>
        Task<DocumentChunk?> GetChunkByIdAsync(string chunkId, List<string>? allowedDocumentIds = null);

        /// <summary>
        /// Legacy method: Gets a chunk by ID (searches all documents)
        /// </summary>
        /// <param name="chunkId">The chunk ID to find</param>
        /// <returns>The chunk if found, null otherwise</returns>
        Task<DocumentChunk?> GetChunkByIdAsync(string chunkId);
    }
}