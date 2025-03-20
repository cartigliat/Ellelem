using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service for prompt engineering
    /// </summary>
    public interface IPromptEngineeringService
    {
        /// <summary>
        /// Creates an augmented prompt from a query and relevant chunks
        /// </summary>
        Task<string> CreateAugmentedPromptAsync(
            string query,
            List<DocumentChunk> relevantChunks);

        /// <summary>
        /// Formats a chunk for inclusion in a prompt
        /// </summary>
        string FormatChunkForPrompt(DocumentChunk chunk);
    }
}