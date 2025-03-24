using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.Services
{
    public class FileSystemVectorStore : IVectorStore
    {
        private readonly List<DocumentChunk> _chunks = new();
        private readonly string _basePath;
        private readonly string _vectorsFolder;
        private readonly object _lock = new();
        private readonly RagDiagnosticsService _diagnostics;
        private bool _isInitialized = false;

        public FileSystemVectorStore(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null)
                throw new ArgumentNullException(nameof(storageSettings));

            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _basePath = storageSettings.BasePath;
            _vectorsFolder = storageSettings.VectorsFolder;

            Directory.CreateDirectory(_vectorsFolder);

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemVectorStore",
                $"Vector store initialized with folder: {_vectorsFolder}");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
                return;

            lock (_lock)
            {
                if (_isInitialized)
                    return;

                _chunks.Clear();

                // Load all vector files from the directory
                var files = Directory.GetFiles(_vectorsFolder, "*.vectors.json");
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var fileChunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json);
                        if (fileChunks != null)
                        {
                            _chunks.AddRange(fileChunks);
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "FileSystemVectorStore",
                            $"Error loading vectors from {file}: {ex.Message}");
                    }
                }

                _isInitialized = true;
                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemVectorStore",
                    $"Loaded {_chunks.Count} vectors from {files.Length} files");
            }

            // Return a completed task since this method is now synchronized
            // We can add actual async file loading in the future if needed
            await Task.CompletedTask;
        }

        public async Task AddVectorsAsync(List<DocumentChunk> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                return;

            await EnsureInitializedAsync();
            _diagnostics.StartOperation("AddVectors");

            try
            {
                // Group chunks by document ID
                var docGroups = chunks.GroupBy(c => c.DocumentId);

                lock (_lock)
                {
                    foreach (var group in docGroups)
                    {
                        string documentId = group.Key;

                        // Remove existing chunks for this document
                        _chunks.RemoveAll(c => c.DocumentId == documentId);

                        // Add the new chunks
                        _chunks.AddRange(group);

                        // Save to file
                        string filePath = Path.Combine(_vectorsFolder, $"{documentId}.vectors.json");
                        string json = JsonSerializer.Serialize(group.ToList());
                        File.WriteAllText(filePath, json);
                    }
                }

                _diagnostics.Log(DiagnosticLevel.Info, "FileSystemVectorStore",
                    $"Added {chunks.Count} vectors for {docGroups.Count()} documents");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemVectorStore",
                    $"Error adding vectors: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("AddVectors");
            }
        }

        public async Task RemoveVectorsAsync(string documentId)
        {
            await EnsureInitializedAsync();
            _diagnostics.StartOperation("RemoveVectors");

            try
            {
                lock (_lock)
                {
                    // Remove from memory
                    int removed = _chunks.RemoveAll(c => c.DocumentId == documentId);

                    // Remove file
                    string filePath = Path.Combine(_vectorsFolder, $"{documentId}.vectors.json");
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    _diagnostics.Log(DiagnosticLevel.Info, "FileSystemVectorStore",
                        $"Removed {removed} vectors for document {documentId}");
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemVectorStore",
                    $"Error removing vectors: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("RemoveVectors");
            }
        }

        public async Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(float[] queryVector, int limit = 5)
        {
            await EnsureInitializedAsync();
            _diagnostics.StartOperation("VectorSearch");

            try
            {
                if (queryVector == null || queryVector.Length == 0)
                    return new List<(DocumentChunk, float)>();

                List<(DocumentChunk Chunk, float Score)> results;
                lock (_lock)
                {
                    results = _chunks
                        .Select(chunk => (Chunk: chunk, Score: CosineSimilarity(queryVector, chunk.Embedding)))
                        .OrderByDescending(x => x.Score)
                        .Take(limit)
                        .ToList();
                }

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemVectorStore",
                    $"Search returned {results.Count} results with top score of {(results.Count > 0 ? results[0].Score.ToString("F4") : "N/A")}");

                return results;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemVectorStore",
                    $"Error during vector search: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("VectorSearch");
            }
        }

        public async Task<List<(DocumentChunk Chunk, float Score)>> SearchInDocumentsAsync(
            float[] queryVector,
            List<string> documentIds,
            int limit = 5)
        {
            if (queryVector == null || queryVector.Length == 0 || documentIds == null || documentIds.Count == 0)
                return new List<(DocumentChunk, float)>();

            await EnsureInitializedAsync();
            _diagnostics.StartOperation("VectorSearchInDocuments");

            try
            {
                List<(DocumentChunk Chunk, float Score)> results;
                lock (_lock)
                {
                    // First filter by document IDs, then calculate similarity
                    results = _chunks
                        .Where(chunk => documentIds.Contains(chunk.DocumentId))
                        .Select(chunk => (Chunk: chunk, Score: CosineSimilarity(queryVector, chunk.Embedding)))
                        .OrderByDescending(x => x.Score)
                        .Take(limit)
                        .ToList();
                }

                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemVectorStore",
                    $"Document-first search returned {results.Count} results from {documentIds.Count} documents with top score of {(results.Count > 0 ? results[0].Score.ToString("F4") : "N/A")}");

                return results;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemVectorStore",
                    $"Error during document-filtered vector search: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("VectorSearchInDocuments");
            }
        }

        private float CosineSimilarity(float[] v1, float[] v2)
        {
            if (v1 == null || v2 == null || v1.Length != v2.Length || v1.Length == 0)
                return 0;

            float dotProduct = 0;
            float magnitude1 = 0;
            float magnitude2 = 0;

            for (int i = 0; i < v1.Length; i++)
            {
                dotProduct += v1[i] * v2[i];
                magnitude1 += v1[i] * v1[i];
                magnitude2 += v2[i] * v2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (magnitude1 * magnitude2);
        }
    }
}