using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    public interface IDocumentRepository
    {
        Task<List<Document>> GetAllDocumentsAsync();
        Task<Document> GetDocumentByIdAsync(string id);
        Task<Document> LoadFullContentAsync(string documentId); // New method for large file handling
        Task SaveDocumentAsync(Document document);
        Task DeleteDocumentAsync(string id);
        Task<DocumentChunk> GetChunkByIdAsync(string chunkId);
    }
}