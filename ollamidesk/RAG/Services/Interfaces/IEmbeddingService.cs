using System.Threading.Tasks;

namespace ollamidesk.RAG.Services.Interfaces
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
    }
}