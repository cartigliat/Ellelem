using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk
{
    public interface IOllamaModel
    {
        Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory);
        Task<string> GenerateResponseWithContextAsync(string userInput, List<string> chatHistory, List<DocumentChunk> relevantChunks);
    }
}