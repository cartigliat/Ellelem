// ollamidesk/RAG/Services/Interfaces/IChunkingService.cs
// MODIFIED VERSION - Added StructuredDocument parameter
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // <-- ADDED for StructuredDocument

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
        /// <param name="structuredDoc">Optional structured document information to guide chunking.</param> /// <--- MODIFIED parameter added
        /// <returns>A list of document chunks.</returns>
        Task<List<DocumentChunk>> ChunkDocumentAsync(Document document, StructuredDocument? structuredDoc = null); // <-- MODIFIED signature
    }
}