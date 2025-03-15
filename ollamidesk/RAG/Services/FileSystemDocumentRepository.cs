using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        private bool _isInitialized = false;

        public FileSystemDocumentRepository(string? basePath = null)
        {
            _basePath = basePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OllamaDesk");
            _metadataFile = Path.Combine(_basePath, "library.json");
            _documentsFolder = Path.Combine(_basePath, "documents");
            _embeddingsFolder = Path.Combine(_basePath, "embeddings");

            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(_documentsFolder);
            Directory.CreateDirectory(_embeddingsFolder);
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
                            _documentsCache[doc.Id] = doc;
                        }
                    }
                }
                catch (Exception)
                {
                    // If we can't read the file, start with an empty cache
                }
            }

            _isInitialized = true;
        }

        private async Task SaveMetadataAsync()
        {
            string json = JsonSerializer.Serialize(_documentsCache.Values.ToList());
            await File.WriteAllTextAsync(_metadataFile, json);
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

            throw new KeyNotFoundException($"Document with ID {id} not found");
        }

        public async Task SaveDocumentAsync(Document document)
        {
            await EnsureInitializedAsync();

            // Save document content
            string documentPath = Path.Combine(_documentsFolder, $"{document.Id}.txt");
            await File.WriteAllTextAsync(documentPath, document.Content);

            // Save embeddings if processed
            if (document.IsProcessed && document.Chunks.Count > 0)
            {
                string embeddingsPath = Path.Combine(_embeddingsFolder, $"{document.Id}.json");
                string embeddingsJson = JsonSerializer.Serialize(document.Chunks);
                await File.WriteAllTextAsync(embeddingsPath, embeddingsJson);
            }

            // Update cache
            _documentsCache[document.Id] = document;

            // Update metadata
            await SaveMetadataAsync();
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

            throw new KeyNotFoundException($"Chunk with ID {chunkId} not found");
        }
    }
}