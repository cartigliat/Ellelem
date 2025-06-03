using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Interface for vector storage operations
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Adds vectors for document chunks to the store
        /// </summary>
        /// <param name="chunks">List of document chunks with embeddings</param>
        Task AddVectorsAsync(List<DocumentChunk> chunks);

        /// <summary>
        /// Removes all vectors for a specific document
        /// </summary>
        /// <param name="documentId">ID of the document to remove</param>
        Task RemoveVectorsAsync(string documentId);

        /// <summary>
        /// Searches for similar vectors across all documents
        /// </summary>
        /// <param name="queryVector">Query embedding vector</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <returns>List of chunks with similarity scores</returns>
        Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(float[] queryVector, int limit = 5);

        /// <summary>
        /// Searches for similar vectors within specific documents
        /// </summary>
        /// <param name="queryVector">Query embedding vector</param>
        /// <param name="documentIds">List of document IDs to search within</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <returns>List of chunks with similarity scores</returns>
        Task<List<(DocumentChunk Chunk, float Score)>> SearchInDocumentsAsync(
            float[] queryVector, List<string> documentIds, int limit = 5);

        /// <summary>
        /// Retrieves a specific chunk by its ID
        /// </summary>
        /// <param name="chunkId">Unique identifier of the chunk</param>
        /// <returns>The document chunk if found, null otherwise</returns>
        Task<DocumentChunk?> GetChunkByIdAsync(string chunkId);
    }
}