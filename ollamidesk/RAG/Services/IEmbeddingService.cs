using System.Threading.Tasks;

namespace ollamidesk.RAG.Services
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
    }
}