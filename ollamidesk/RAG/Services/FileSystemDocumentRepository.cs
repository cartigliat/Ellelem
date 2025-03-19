using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services
{
    public class FileSystemDocumentRepository : IDocumentRepository
    {
        private readonly string _basePath;
        private readonly string _metadataFile;
        private readonly string _documentsFolder;
        private readonly string _embeddingsFolder;
        private readonly Dictionary<string, Document> _documentsCache = new();
        private readonly RagDiagnosticsService _diagnostics;
        private bool _isInitialized = false;

        public FileSystemDocumentRepository(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null)
                throw new ArgumentNullException(nameof(storageSettings));

            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _basePath = storageSettings.BasePath;
            _metadataFile = storageSettings.MetadataFile;
            _documentsFolder = storageSettings.DocumentsFolder;
            _embeddingsFolder = storageSettings.EmbeddingsFolder;

            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(_documentsFolder);
            Directory.CreateDirectory(_embeddingsFolder);

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                $"Repository initialized with base path: {_basePath}");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
                return;

            if (File.Exists(_metadataFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_metadataFile);
                    var documents = JsonSerializer.Deserialize<List<Document>>(json);

                    if (documents != null)
                    {
                        foreach (var doc in documents)
                        {
                            // For large files, don't load content in metadata
                            if (doc.IsLargeFile)
                            {
                                doc.Content = $"[Large file: {doc.FileSize / (1024.0 * 1024.0):F2} MB - Use LoadFullContentAsync to view]";
                                doc.IsContentTruncated = true;
                            }
                            _documentsCache[doc.Id] = doc;
                        }
                    }

                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                        $"Loaded {_documentsCache.Count} documents from metadata file");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository",
                        $"Error loading document metadata: {ex.Message}");
                    // If we can't read the file, start with an empty cache
                }
            }

            _isInitialized = true;
        }

        private async Task SaveMetadataAsync()
        {
            try
            {
                // Clone documents for serialization, removing content for large files
                var documentsToSave = _documentsCache.Values.Select(doc =>
                {
                    // Create a shallow copy
                    var docCopy = new Document
                    {
                        Id = doc.Id,
                        Name = doc.Name,
                        FilePath = doc.FilePath,
                        DateAdded = doc.DateAdded,
                        IsProcessed = doc.IsProcessed,
                        IsSelected = doc.IsSelected,
                        FileSize = doc.FileSize,
                        IsContentTruncated = doc.IsLargeFile // Always mark large files as truncated in metadata
                    };

                    // Only include content for small files
                    if (!doc.IsLargeFile)
                    {
                        docCopy.Content = doc.Content;
                    }

                    return docCopy;
                }).ToList();

                string json = JsonSerializer.Serialize(documentsToSave);
                await File.WriteAllTextAsync(_metadataFile, json);

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository",
                    "Metadata saved successfully");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository",
                    $"Error saving metadata: {ex.Message}");
            }
        }

        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            await EnsureInitializedAsync();
            return _documentsCache.Values.ToList();
        }

        public async Task<Document> GetDocumentByIdAsync(string id)
        {
            await EnsureInitializedAsync();

            if (_documentsCache.TryGetValue(id, out var document))
            {
                return document;
            }

            _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository",
                $"Document with ID {id} not found");
            throw new KeyNotFoundException($"Document with ID {id} not found");
        }

        public async Task<Document> LoadFullContentAsync(string documentId)
        {
            await EnsureInitializedAsync();

            if (!_documentsCache.TryGetValue(documentId, out var document))
            {
                throw new KeyNotFoundException($"Document with ID {documentId} not found");
            }

            // If it's not a large file or content is already loaded, return as is
            if (!document.IsLargeFile || !document.IsContentTruncated)
            {
                return document;
            }

            try
            {
                _diagnostics.StartOperation("LoadFullDocumentContent");

                if (!File.Exists(document.FilePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository",
                        $"Document file not found: {document.FilePath}");
                    throw new FileNotFoundException($"Document file not found: {document.FilePath}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                    $"Loading full content for large document: {document.Id}, size: {document.FileSize / (1024.0 * 1024.0):F2} MB");

                // For text files, read the actual content
                if (IsTextFile(Path.GetExtension(document.FilePath)))
                {
                    document.Content = await File.ReadAllTextAsync(document.FilePath);
                    document.IsContentTruncated = false;

                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                        $"Loaded full content, length: {document.Content.Length} characters");
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository",
                        $"Cannot load full content for non-text file: {document.FilePath}");
                }

                return document;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemDocumentRepository",
                    $"Error loading full document content: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("LoadFullDocumentContent");
            }
        }

        public async Task SaveDocumentAsync(Document document)
        {
            await EnsureInitializedAsync();

            // For large files, only save content to disk if it's not truncated
            if (!document.IsContentTruncated && !string.IsNullOrEmpty(document.Content))
            {
                string documentPath = Path.Combine(_documentsFolder, $"{document.Id}.txt");
                await File.WriteAllTextAsync(documentPath, document.Content);

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository",
                    $"Saved document content to disk: {document.Id}, size: {document.Content.Length} chars");
            }

            // Save embeddings if processed
            if (document.IsProcessed && document.Chunks.Count > 0)
            {
                string embeddingsPath = Path.Combine(_embeddingsFolder, $"{document.Id}.json");
                string embeddingsJson = JsonSerializer.Serialize(document.Chunks);
                await File.WriteAllTextAsync(embeddingsPath, embeddingsJson);

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository",
                    $"Saved {document.Chunks.Count} chunks for document: {document.Id}");
            }

            // Update cache
            _documentsCache[document.Id] = document;

            // Update metadata
            await SaveMetadataAsync();

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                $"Document saved: {document.Id}, Name: {document.Name}, Size: {document.FileSize / 1024.0:F2} KB");
        }

        public async Task DeleteDocumentAsync(string id)
        {
            await EnsureInitializedAsync();

            if (_documentsCache.TryGetValue(id, out var document))
            {
                // Remove from cache
                _documentsCache.Remove(id);

                // Delete document file
                string documentPath = Path.Combine(_documentsFolder, $"{document.Id}.txt");
                if (File.Exists(documentPath))
                {
                    File.Delete(documentPath);
                }

                // Delete embeddings file
                string embeddingsPath = Path.Combine(_embeddingsFolder, $"{document.Id}.json");
                if (File.Exists(embeddingsPath))
                {
                    File.Delete(embeddingsPath);
                }

                // Update metadata
                await SaveMetadataAsync();

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                    $"Document deleted: {id}");
            }
            else
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository",
                    $"Attempted to delete non-existent document: {id}");
            }
        }

        public async Task<DocumentChunk> GetChunkByIdAsync(string chunkId)
        {
            await EnsureInitializedAsync();

            foreach (var document in _documentsCache.Values)
            {
                if (document.Chunks != null)
                {
                    var chunk = document.Chunks.FirstOrDefault(c => c.Id == chunkId);
                    if (chunk != null)
                    {
                        return chunk;
                    }
                }
            }

            _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository",
                $"Chunk with ID {chunkId} not found");
            throw new KeyNotFoundException($"Chunk with ID {chunkId} not found");
        }

        private bool IsTextFile(string extension)
        {
            // List of common text file extensions
            string[] textExtensions = new[] {
                ".txt", ".md", ".cs", ".json", ".xml", ".html", ".htm", ".css",
                ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".hpp",
                ".sql", ".yaml", ".yml", ".config", ".ini", ".log"
            };

            return textExtensions.Contains(extension.ToLowerInvariant());
        }
    }
}