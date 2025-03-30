using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    public class FileSystemDocumentRepository : IDocumentRepository, IDisposable
    {
        private readonly string _basePath;
        private readonly string _metadataFile;
        private readonly string _documentsFolder;
        private readonly string _embeddingsFolder;
        private readonly Dictionary<string, Document> _documentsCache = new();
        private readonly RagDiagnosticsService _diagnostics;
        private bool _isInitialized = false;

        // Thread safety additions
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileSpecificLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

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

        // Helper to get or create a lock for a specific file
        private SemaphoreSlim GetFileLock(string filePath)
        {
            return _fileSpecificLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
                return;

            await _initializationLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                if (File.Exists(_metadataFile))
                {
                    try
                    {
                        // Get file lock for reading
                        var fileLock = GetFileLock(_metadataFile);
                        await fileLock.WaitAsync();
                        string json;

                        try
                        {
                            json = await File.ReadAllTextAsync(_metadataFile);
                        }
                        finally
                        {
                            fileLock.Release();
                        }

                        var documents = JsonSerializer.Deserialize<List<Document>>(json);

                        if (documents != null)
                        {
                            await _cacheLock.WaitAsync();
                            try
                            {
                                _documentsCache.Clear();
                                foreach (var doc in documents)
                                {
                                    // We now always store full content
                                    _documentsCache[doc.Id] = doc;
                                }
                            }
                            finally
                            {
                                _cacheLock.Release();
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
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task SaveMetadataAsync()
        {
            try
            {
                List<Document> documentsToSave;

                await _cacheLock.WaitAsync();
                try
                {
                    // Clone documents for serialization
                    documentsToSave = _documentsCache.Values.Select(doc =>
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
                            DocumentType = doc.DocumentType
                        };

                        // Always include content
                        docCopy.Content = doc.Content;

                        return docCopy;
                    }).ToList();
                }
                finally
                {
                    _cacheLock.Release();
                }

                string json = JsonSerializer.Serialize(documentsToSave);

                // Get file lock for writing
                var fileLock = GetFileLock(_metadataFile);
                await fileLock.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(_metadataFile, json);
                }
                finally
                {
                    fileLock.Release();
                }

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

            await _cacheLock.WaitAsync();
            try
            {
                return _documentsCache.Values.ToList();
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<Document> GetDocumentByIdAsync(string id)
        {
            await EnsureInitializedAsync();

            await _cacheLock.WaitAsync();
            try
            {
                if (_documentsCache.TryGetValue(id, out var document))
                {
                    return document;
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemDocumentRepository",
                $"Document with ID {id} not found");
            throw new KeyNotFoundException($"Document with ID {id} not found");
        }

        public async Task<Document> LoadFullContentAsync(string documentId)
        {
            await EnsureInitializedAsync();

            Document? document = null; // Fix: Make document nullable
            await _cacheLock.WaitAsync();
            try
            {
                if (!_documentsCache.TryGetValue(documentId, out var docRef))
                {
                    throw new KeyNotFoundException($"Document with ID {documentId} not found");
                }
                document = docRef; // Assign to our nullable variable
            }
            finally
            {
                _cacheLock.Release();
            }

            // Check for null after lock is released
            if (document == null)
            {
                throw new InvalidOperationException($"Document with ID {documentId} was unexpectedly null after retrieval");
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
                    $"Loading full content for document: {document.Id}, size: {document.FileSize / (1024.0 * 1024.0):F2} MB");

                // For text files, read the actual content
                if (IsTextFile(Path.GetExtension(document.FilePath)))
                {
                    var fileLock = GetFileLock(document.FilePath);
                    await fileLock.WaitAsync();
                    try
                    {
                        document.Content = await File.ReadAllTextAsync(document.FilePath);
                    }
                    finally
                    {
                        fileLock.Release();
                    }

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

            // Always save content to disk
            if (!string.IsNullOrEmpty(document.Content))
            {
                string documentPath = Path.Combine(_documentsFolder, $"{document.Id}.txt");

                // Ensure directory exists
                string? directory = Path.GetDirectoryName(documentPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var fileLock = GetFileLock(documentPath);
                await fileLock.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(documentPath, document.Content);
                }
                finally
                {
                    fileLock.Release();
                }

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository",
                    $"Saved document content to disk: {document.Id}, size: {document.Content.Length} chars");
            }

            // Save embeddings if processed
            if (document.IsProcessed && document.Chunks.Count > 0)
            {
                string embeddingsPath = Path.Combine(_embeddingsFolder, $"{document.Id}.json");
                string embeddingsJson = JsonSerializer.Serialize(document.Chunks);

                var fileLock = GetFileLock(embeddingsPath);
                await fileLock.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(embeddingsPath, embeddingsJson);
                }
                finally
                {
                    fileLock.Release();
                }

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemDocumentRepository",
                    $"Saved {document.Chunks.Count} chunks for document: {document.Id}");
            }

            // Update cache
            await _cacheLock.WaitAsync();
            try
            {
                _documentsCache[document.Id] = document;
            }
            finally
            {
                _cacheLock.Release();
            }

            // Update metadata
            await SaveMetadataAsync();

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemDocumentRepository",
                $"Document saved: {document.Id}, Name: {document.Name}, Size: {document.FileSize / 1024.0:F2} KB");
        }

        public async Task DeleteDocumentAsync(string id)
        {
            await EnsureInitializedAsync();

            Document? document = null;

            await _cacheLock.WaitAsync();
            try
            {
                if (_documentsCache.TryGetValue(id, out var docRef))
                {
                    document = docRef;
                    // Remove from cache
                    _documentsCache.Remove(id);
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            if (document != null)
            {
                // Delete document file
                string documentPath = Path.Combine(_documentsFolder, $"{document.Id}.txt");
                if (File.Exists(documentPath))
                {
                    var fileLock = GetFileLock(documentPath);
                    await fileLock.WaitAsync();
                    try
                    {
                        File.Delete(documentPath);
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                }

                // Delete embeddings file
                string embeddingsPath = Path.Combine(_embeddingsFolder, $"{document.Id}.json");
                if (File.Exists(embeddingsPath))
                {
                    var fileLock = GetFileLock(embeddingsPath);
                    await fileLock.WaitAsync();
                    try
                    {
                        File.Delete(embeddingsPath);
                    }
                    finally
                    {
                        fileLock.Release();
                    }
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

            await _cacheLock.WaitAsync();
            try
            {
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
            }
            finally
            {
                _cacheLock.Release();
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

        // Cleanup resources
        public void Dispose()
        {
            _initializationLock?.Dispose();
            _cacheLock?.Dispose();

            // Dispose all file locks
            foreach (var fileLock in _fileSpecificLocks.Values)
            {
                fileLock?.Dispose();
            }
            _fileSpecificLocks.Clear();
        }
    }
}