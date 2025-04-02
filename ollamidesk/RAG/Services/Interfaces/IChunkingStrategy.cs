// ollamidesk/RAG/Services/Interfaces/IChunkingStrategy.cs
using System.Collections.Generic;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for a specific document chunking strategy.
    /// </summary>
    public interface IChunkingStrategy
    {
        /// <summary>
        /// Determines if this strategy is applicable to the given document.
        /// </summary>
        /// <param name="document">The document to check.</param>
        /// <returns>True if the strategy can be applied, false otherwise.</returns>
        bool CanChunk(Document document);

        /// <summary>
        /// Chunks the document content according to the specific strategy.
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <returns>A list of document chunks.</returns>
        List<DocumentChunk> Chunk(Document document);
    }
}