// ollamidesk/RAG/Services/Interfaces/IDocumentRepository.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    public interface IDocumentRepository
    {
        Task<List<Document>> GetAllDocumentsAsync();
        Task<Document> GetDocumentByIdAsync(string id);
        Task<Document> LoadFullContentAsync(string documentId);
        Task SaveDocumentAsync(Document document);
        Task DeleteDocumentAsync(string id);
        // Corrected the return type to be nullable
        Task<DocumentChunk?> GetChunkByIdAsync(string chunkId);
    }
}