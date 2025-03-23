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

                // Determine file size
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;
                string name = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                // Determine document type from extension
                string documentType = GetDocumentTypeFromExtension(extension);

                // Determine if we should load the full content or just a preview
                bool isLargeFile = fileSize > 10 * 1024 * 1024; // 10MB threshold
                bool loadPreviewOnly = isLargeFile && !loadFullContent;

                // Load content based on file size and settings
                string content;
                bool isContentTruncated = false;

                if (loadPreviewOnly)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                        $"Large document detected ({fileSize / (1024.0 * 1024.0):F2} MB), loading preview only");
                    content = await LoadDocumentPreviewAsync(filePath, extension);
                    isContentTruncated = true;
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentManagementService",
                        $"Loading full document content ({fileSize / 1024.0:F2} KB)");
                    content = await LoadDocumentContentAsync(filePath, extension);
                }

                var document = new Document
                {
                    Name = name,
                    FilePath = filePath,
                    Content = content,
                    IsProcessed = false,
                    IsSelected = false,
                    FileSize = fileSize,
                    IsContentTruncated = isContentTruncated,
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

        private async Task<string> LoadDocumentPreviewAsync(string filePath, string extension)
        {
            try
            {
                // Try to use the appropriate processor for a preview
                var processor = _documentProcessorFactory.GetProcessor(extension);

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(stream))
                {
                    const int previewSize = 100 * 1024; // 100KB preview
                    char[] buffer = new char[previewSize];
                    int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);

                    // Add a message indicating the content is truncated
                    string preview = new string(buffer, 0, read);
                    return preview + "\n\n[... Content truncated (large file) ...]";
                }
            }
            catch (NotSupportedException)
            {
                // If no processor is available, provide a generic message
                return $"[Large {extension} file, size: {new FileInfo(filePath).Length / (1024.0 * 1024.0):F2} MB]";
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