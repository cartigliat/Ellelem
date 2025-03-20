using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service for document processing operations
    /// </summary>
    public interface IDocumentProcessingService
    {
        /// <summary>
        /// Processes a document (chunking and embedding)
        /// </summary>
        Task<Document> ProcessDocumentAsync(Document document);

        /// <summary>
        /// Chunks a document into smaller pieces
        /// </summary>
        Task<List<DocumentChunk>> ChunkDocumentAsync(Document document);

        /// <summary>
        /// Loads the full content of a document
        /// </summary>
        Task<Document> LoadFullContentAsync(Document document);
    }
}