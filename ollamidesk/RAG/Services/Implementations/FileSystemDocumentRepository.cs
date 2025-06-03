// ollamidesk/RAG/Services/Implementations/FileSystemDocumentRepository.cs
// Refactored to use IMetadataStore and IContentStore
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Exceptions; // Added for exceptions


namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implements IDocumentRepository by coordinating metadata and content storage providers.
    /// </summary>
    public class FileSystemDocumentRepository : IDocumentRepository
    {
        private readonly IMetadataStore _metadataStore;
        private readonly IContentStore _contentStore;
        private readonly RagDiagnosticsService _diagnostics;

        public FileSystemDocumentRepository(
            IMetadataStore metadataStore,
            IContentStore contentStore,
            RagDiagnosticsService diagnostics)
        {
            _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
            _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", "Repository initialized, coordinating metadata and content stores.");
        }

        // Converts metadata to a basic Document object (without content/chunks)
        private Document ConvertMetadataToDocument(DocumentMetadata meta)
        {
            return new Document
            {
                Id = meta.Id,
                Name = meta.Name,
                FilePath = meta.FilePath,
                DateAdded = meta.DateAdded,
                IsProcessed = meta.IsProcessed,
                IsSelected = meta.IsSelected,
                FileSize = meta.FileSize,
                DocumentType = meta.DocumentType,
                // Content and Chunks are not loaded by default
            };
        }

        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            _diagnostics.StartOperation("Repo.GetAllDocuments");
            try
            {
                var allMetadata = await _metadataStore.LoadMetadataAsync().ConfigureAwait(false);
                var documents = allMetadata.Values
                    .Select(ConvertMetadataToDocument)
                    .ToList();
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Retrieved {documents.Count} documents (metadata only).");
                return documents;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error getting all documents: {ex.Message}");
                throw; // Re-throw for higher layers to handle
            }
            finally
            {
                _diagnostics.EndOperation("Repo.GetAllDocuments");
            }
        }

        public async Task<Document> GetDocumentByIdAsync(string id)
        {
            _diagnostics.StartOperation("Repo.GetDocumentById");
            try
            {
                var metadata = await _metadataStore.GetMetadataByIdAsync(id).ConfigureAwait(false);
                if (metadata == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository", $"Document metadata not found for ID {id}");
                    throw new KeyNotFoundException($"Document with ID {id} not found.");
                }
                // Return document based on metadata; content is loaded separately
                var document = ConvertMetadataToDocument(metadata);
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Retrieved document metadata for ID {id}.");
                return document;
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error getting document by ID {id}: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Repo.GetDocumentById");
            }
        }

        public async Task<Document> LoadFullContentAsync(string documentId)
        {
            _diagnostics.StartOperation("Repo.LoadFullContent");
            try
            {
                // 1. Get Metadata
                var metadata = await _metadataStore.GetMetadataByIdAsync(documentId).ConfigureAwait(false);
                if (metadata == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository", $"Cannot load content, metadata not found for ID {documentId}");
                    throw new KeyNotFoundException($"Document with ID {documentId} not found.");
                }

                // 2. Create basic Document object
                var document = ConvertMetadataToDocument(metadata);

                // 3. Load Content
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Loading content for document {documentId} via ContentStore.");
                document.Content = await _contentStore.LoadContentAsync(documentId).ConfigureAwait(false);

                // 4. Load Embeddings/Chunks if processed
                if (metadata.HasEmbeddings) // Use flag from metadata
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Loading embeddings for document {documentId} via ContentStore.");
                    document.Chunks = await _contentStore.LoadEmbeddingsAsync(documentId).ConfigureAwait(false) ?? new List<DocumentChunk>();
                }
                else
                {
                    document.Chunks = new List<DocumentChunk>(); // Ensure Chunks is not null
                }

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Loaded full content and {document.Chunks.Count} chunks for document {documentId}.");
                return document;
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error loading full content for ID {documentId}: {ex.Message}");
                throw new DocumentProcessingException($"Failed to load full content for document {documentId}.", ex);
            }
            finally
            {
                _diagnostics.EndOperation("Repo.LoadFullContent");
            }
        }

        public async Task SaveDocumentAsync(Document document)
        {
            _diagnostics.StartOperation("Repo.SaveDocument");
            try
            {
                // 1. Create/Update Metadata
                var metadata = new DocumentMetadata
                {
                    Id = document.Id,
                    Name = document.Name,
                    FilePath = document.FilePath,
                    DateAdded = document.DateAdded,
                    IsProcessed = document.IsProcessed,
                    IsSelected = document.IsSelected,
                    FileSize = document.FileSize,
                    DocumentType = document.DocumentType,
                    HasEmbeddings = document.IsProcessed && (document.Chunks?.Count > 0) // Set flag based on state
                };
                await _metadataStore.SaveMetadataAsync(metadata).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Saved metadata for document {document.Id}.");


                // 2. Save Content if present
                if (!string.IsNullOrEmpty(document.Content))
                {
                    await _contentStore.SaveContentAsync(document.Id, document.Content).ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Saved content for document {document.Id}.");
                }

                // 3. Save Embeddings if processed
                if (metadata.HasEmbeddings && document.Chunks != null) // Check flag and chunks list
                {
                    await _contentStore.SaveEmbeddingsAsync(document.Id, document.Chunks).ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Saved {document.Chunks.Count} embeddings for document {document.Id}.");
                }
                else if (document.IsProcessed && (!metadata.HasEmbeddings || document.Chunks == null || document.Chunks.Count == 0))
                {
                    // If marked processed but no chunks to save, maybe delete old embeddings file?
                    _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository", $"Document {document.Id} is processed but has no chunks to save. Deleting any existing embeddings file.");
                    await _contentStore.DeleteEmbeddingsAsync(document.Id).ConfigureAwait(false);
                }

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Successfully saved document {document.Id}.");

            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error saving document {document.Id}: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Repo.SaveDocument");
            }
        }

        public async Task DeleteDocumentAsync(string id)
        {
            _diagnostics.StartOperation("Repo.DeleteDocument");
            bool contentDeleted = false;
            bool embeddingsDeleted = false;

            try
            {
                // Step 1: Delete Content
                try
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Attempting to delete content for document {id}.");
                    await _contentStore.DeleteContentAsync(id).ConfigureAwait(false);
                    contentDeleted = true;
                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Successfully deleted content for document {id}.");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Critical, "FileSystemDocumentRepository", $"CRITICAL: Failed to delete content for document {id}. Metadata and embeddings will NOT be deleted to prevent orphaned data. Error: {ex.Message}");
                    throw; // Re-throw original or a new specific exception
                }

                // Step 2: Delete Embeddings (only if content deletion was successful)
                try
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Attempting to delete embeddings for document {id}.");
                    await _contentStore.DeleteEmbeddingsAsync(id).ConfigureAwait(false);
                    embeddingsDeleted = true;
                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Successfully deleted embeddings for document {id}.");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Critical, "FileSystemDocumentRepository", $"CRITICAL: Failed to delete embeddings for document {id} (content was deleted). Metadata will NOT be deleted. Error: {ex.Message}");
                    throw; // Re-throw
                }

                // Step 3: Delete Metadata (only if content and embeddings deletion were successful)
                try
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Attempting to delete metadata for document {id}.");
                    await _metadataStore.DeleteMetadataAsync(id).ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Successfully deleted metadata for document {id}.");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error deleting metadata for document {id} (content and embeddings were deleted). Error: {ex.Message}");
                    throw; // Re-throw
                }

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository", $"Successfully deleted all components for document {id}.");
            }
            catch (Exception) // This will catch re-thrown exceptions from the steps above
            {
                // Logging already done in specific catch blocks, but we can add a general error if needed
                // For example, if the logic itself within this try block had an issue (unlikely here)
                // _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"An unexpected error occurred during the deletion process for document {id}: {ex.Message}");
                if (!contentDeleted)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Deletion process for document {id} halted: Content deletion failed.");
                }
                else if (!embeddingsDeleted)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Deletion process for document {id} halted: Embeddings deletion failed (content was deleted).");
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Deletion process for document {id} failed at metadata deletion (content and embeddings were deleted).");
                }
                throw; // Ensure the exception is propagated
            }
            finally
            {
                _diagnostics.EndOperation("Repo.DeleteDocument");
            }
        }

        public async Task<DocumentChunk?> GetChunkByIdAsync(string chunkId)
        {
            _diagnostics.StartOperation("Repo.GetChunkById");
            try
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository", $"GetChunkByIdAsync ({chunkId}) may be inefficient - loading all metadata.");
                // This is inefficient: requires loading all metadata, then potentially loading embedding files one by one.
                // A better approach would involve a different storage mechanism if fast chunk lookup is critical.
                var allMetadata = await _metadataStore.LoadMetadataAsync().ConfigureAwait(false);

                foreach (var meta in allMetadata.Values)
                {
                    if (meta.HasEmbeddings)
                    {
                        var chunks = await _contentStore.LoadEmbeddingsAsync(meta.Id).ConfigureAwait(false);
                        var foundChunk = chunks?.FirstOrDefault(c => c.Id == chunkId);
                        if (foundChunk != null)
                        {
                            _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository", $"Found chunk {chunkId} in document {meta.Id}");
                            return foundChunk;
                        }
                    }
                }
                _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository", $"Chunk {chunkId} not found after checking all documents.");
                return null; // Or throw KeyNotFoundException
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository", $"Error getting chunk by ID {chunkId}: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Repo.GetChunkById");
            }
        }

        // Removed EnsureInitializedAsync, SaveMetadataAsync, file locking logic, Dispose
        // These responsibilities are now in the specific store implementations.
    }
}