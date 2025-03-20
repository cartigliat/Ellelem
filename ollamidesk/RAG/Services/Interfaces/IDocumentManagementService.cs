using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service for document management operations
    /// </summary>
    public interface IDocumentManagementService
    {
        /// <summary>
        /// Adds a document from a file path
        /// </summary>
        Task<Document> AddDocumentAsync(string filePath, bool loadFullContent = false);

        /// <summary>
        /// Gets a document by ID
        /// </summary>
        Task<Document> GetDocumentAsync(string id);

        /// <summary>
        /// Gets all documents
        /// </summary>
        Task<List<Document>> GetAllDocumentsAsync();

        /// <summary>
        /// Deletes a document by ID
        /// </summary>
        Task DeleteDocumentAsync(string id);

        /// <summary>
        /// Updates document selection state
        /// </summary>
        Task UpdateDocumentSelectionAsync(string id, bool isSelected);
    }
}