// IRagProvider.cs
using ollamidesk.RAG.Services;

namespace ollamidesk.RAG
{
    /// <summary>
    /// Interface for classes that provide access to a RAG service
    /// </summary>
    public interface IRagProvider
    {
        /// <summary>
        /// Gets the RAG service instance
        /// </summary>
        /// <returns>The RAG service</returns>
        RagService GetRagService();
    }
}