using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service for retrieving relevant chunks
    /// </summary>
    public interface IRetrievalService
    {
        /// <summary>
        /// Retrieves chunks relevant to a query
        /// </summary>
        Task<List<(DocumentChunk Chunk, float Score)>> RetrieveRelevantChunksAsync(
            string query,
            List<string> documentIds,
            int maxResults = 4);

        /// <summary>
        /// Calculates the relevance score between a query and a chunk
        /// </summary>
        Task<float> CalculateRelevanceScoreAsync(string query, DocumentChunk chunk);
    }
}