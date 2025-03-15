using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services
{
    public interface IVectorStore
    {
        Task AddVectorsAsync(List<DocumentChunk> chunks);
        Task RemoveVectorsAsync(string documentId);
        Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(float[] queryVector, int limit = 5);
    }
}