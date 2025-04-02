// ollamidesk/RAG/Services/Interfaces/IMetadataStore.cs
// New file
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Interface for storing and retrieving document metadata.
    /// </summary>
    public interface IMetadataStore : IDisposable
    {
        /// <summary>
        /// Loads all document metadata.
        /// </summary>
        Task<Dictionary<string, DocumentMetadata>> LoadMetadataAsync();

        /// <summary>
        /// Saves the complete collection of document metadata.
        /// </summary>
        Task SaveMetadataAsync(Dictionary<string, DocumentMetadata> metadata);

        /// <summary>
        /// Gets metadata for a single document.
        /// </summary>
        Task<DocumentMetadata?> GetMetadataByIdAsync(string documentId);

        /// <summary>
        /// Adds or updates metadata for a single document.
        /// </summary>
        Task SaveMetadataAsync(DocumentMetadata metadata);

        /// <summary>
        /// Deletes metadata for a single document.
        /// </summary>
        Task DeleteMetadataAsync(string documentId);
    }
}