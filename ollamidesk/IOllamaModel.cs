using System.Collections.Generic;
using System.Threading.Tasks;

namespace ollamidesk
{
    public interface IOllamaModel
    {
        Task<string> GenerateResponseAsync(string userInput, string loadedDocument, List<string> chatHistory);
    }
}