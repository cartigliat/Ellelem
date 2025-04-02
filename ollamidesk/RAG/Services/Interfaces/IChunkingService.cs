// ollamidesk/RAG/Services/Interfaces/IChunkingService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service responsible for chunking document content based on different strategies.
    /// </summary>
    public interface IChunkingService
    {
        /// <summary>
        /// Chunks the content of a document based on its structure or type.
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <returns>A list of document chunks.</returns>
        Task<List<DocumentChunk>> ChunkDocumentAsync(Document document);
    }
}