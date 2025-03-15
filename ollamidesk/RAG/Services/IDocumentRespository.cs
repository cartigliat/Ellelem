using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services
{
    public interface IDocumentRepository
    {
        Task<List<Document>> GetAllDocumentsAsync();
        Task<Document> GetDocumentByIdAsync(string id);
        Task SaveDocumentAsync(Document document);
        Task DeleteDocumentAsync(string id);
        Task<DocumentChunk> GetChunkByIdAsync(string chunkId);
    }
}