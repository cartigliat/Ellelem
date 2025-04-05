// ollamidesk/RAG/Services/Interfaces/IChunkingStrategy.cs
// MODIFIED VERSION - Added StructuredDocument parameter to methods
using System.Collections.Generic;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // <-- ADDED for StructuredDocument

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for a specific document chunking strategy.
    /// </summary>
    public interface IChunkingStrategy
    {
        /// <summary>
        /// Determines if this strategy is applicable to the given document and optional structured data.
        /// </summary>
        /// <param name="document">The document to check.</param>
        /// <param name="structuredDoc">Optional structured document information.</param> /// <--- MODIFIED parameter added
        /// <returns>True if the strategy can be applied, false otherwise.</returns>
        bool CanChunk(Document document, StructuredDocument? structuredDoc); // <-- MODIFIED signature

        /// <summary>
        /// Chunks the document content according to the specific strategy, potentially using structured data.
        /// </summary>
        /// <param name="document">The document to chunk.</param>
        /// <param name="structuredDoc">Optional structured document information to guide chunking.</param> /// <--- MODIFIED parameter added
        /// <returns>A list of document chunks.</returns>
        List<DocumentChunk> Chunk(Document document, StructuredDocument? structuredDoc); // <-- MODIFIED signature
    }
}