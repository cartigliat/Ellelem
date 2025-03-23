// DocumentManagementService.cs - Simplified document loading
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the document management service
    /// </summary>
    public class DocumentManagementService : IDocumentManagementService
    {
        private readonly IDocumentRepository _documentRepository;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly DocumentProcessorFactory _documentProcessorFactory;

        public DocumentManagementService(
            IDocumentRepository documentRepository,
            RagDiagnosticsService diagnostics,
            DocumentProcessorFactory documentProcessorFactory)
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentProcessorFactory = documentProcessorFactory ?? throw new ArgumentNullException(nameof(documentProcessorFactory));

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService", "Initialized DocumentManagementService");
        }

        public async Task<Document> AddDocumentAsync(string filePath, bool loadFullContent = false)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService", $"Adding document: {filePath}");

            try
            {
                _diagnostics.StartOperation("AddDocument");

                // Get file information
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;
                string name = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                // Determine document type from extension
                string documentType = GetDocumentTypeFromExtension(extension);

                // Always load full content
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Loading full document content ({fileSize / 1024.0:F2} KB)");

                string content = await LoadDocumentContentAsync(filePath, extension);

                var document = new Document
                {
                    Name = name,
                    FilePath = filePath,
                    Content = content,
                    IsProcessed = false,
                    IsSelected = false,
                    FileSize = fileSize,
                    DocumentType = documentType
                };

                await _documentRepository.SaveDocumentAsync(document);
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Document added with ID: {document.Id}, Type: {documentType}, Size: {fileSize / 1024.0:F2} KB");

                return document;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentManagementService",
                    $"Failed to add document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("AddDocument");
            }
        }

        private async Task<string> LoadDocumentContentAsync(string filePath, string extension)
        {
            try
            {
                // Get appropriate document processor
                var processor = _documentProcessorFactory.GetProcessor(extension);

                // Extract text from document
                return await processor.ExtractTextAsync(filePath);
            }
            catch (NotSupportedException)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "DocumentManagementService",
                    $"Unknown file type: {extension}, treating as binary");

                return $"[Binary file with extension {extension}, size: {new FileInfo(filePath).Length / 1024.0:F2} KB]";
            }
        }

        private string GetDocumentTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".txt" => "Text",
                ".md" or ".markdown" => "Markdown",
                ".pdf" => "PDF",
                ".docx" => "Word",
                ".doc" => "Word",
                ".rtf" => "Rich Text",
                ".html" or ".htm" => "HTML",
                ".csv" => "CSV",
                ".json" => "JSON",
                ".xml" => "XML",
                ".cs" => "C# Source",
                ".js" => "JavaScript",
                ".py" => "Python",
                ".java" => "Java",
                _ => "Unknown"
            };
        }

        public async Task<Document> GetDocumentAsync(string id)
        {
            try
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentManagementService",
                    $"GetDocumentAsync called for {id}");
                return await _documentRepository.GetDocumentByIdAsync(id);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentManagementService",
                    $"Error getting document {id}: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            try
            {
                _diagnostics.StartOperation("GetAllDocuments");

                var documents = await _documentRepository.GetAllDocumentsAsync();
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Retrieved {documents.Count} documents");

                return documents;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentManagementService",
                    $"Failed to get all documents: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("GetAllDocuments");
            }
        }

        public async Task DeleteDocumentAsync(string id)
        {
            try
            {
                _diagnostics.StartOperation("DeleteDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Deleting document: {id}");

                await _documentRepository.DeleteDocumentAsync(id);

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Document deleted: {id}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentManagementService",
                    $"Failed to delete document: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("DeleteDocument");
            }
        }

        public async Task UpdateDocumentSelectionAsync(string id, bool isSelected)
        {
            try
            {
                var document = await _documentRepository.GetDocumentByIdAsync(id);
                if (document == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentManagementService",
                        $"Document not found for selection update: {id}");
                    return;
                }

                document.IsSelected = isSelected;
                await _documentRepository.SaveDocumentAsync(document);

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                    $"Updated selection state for document {id} to {isSelected}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentManagementService",
                    $"Failed to update document selection: {ex.Message}");
                throw;
            }
        }
    }
}