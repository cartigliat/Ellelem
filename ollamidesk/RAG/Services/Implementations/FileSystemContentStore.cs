// ollamidesk/RAG/Services/Implementations/FileSystemContentStore.cs
// New file
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Stores document content and embeddings in separate files on the file system.
    /// </summary>
    public class FileSystemContentStore : IContentStore
    {
        private readonly string _documentsFolder;
        private readonly string _embeddingsFolder;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(); // Lock per document ID
        private bool _disposedValue;

        public FileSystemContentStore(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null) throw new ArgumentNullException(nameof(storageSettings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _documentsFolder = storageSettings.DocumentsFolder;
            _embeddingsFolder = storageSettings.EmbeddingsFolder;

            // Ensure directories exist
            Directory.CreateDirectory(_documentsFolder);
            Directory.CreateDirectory(_embeddingsFolder);

            _diagnostics.Log(DiagnosticLevel.Info, "FileSystemContentStore", $"Store initialized. Documents: '{_documentsFolder}', Embeddings: '{_embeddingsFolder}'");
        }

        private SemaphoreSlim GetFileLock(string documentId)
        {
            // Use a lock specific to the document ID for content/embedding files
            return _fileLocks.GetOrAdd(documentId, _ => new SemaphoreSlim(1, 1));
        }

        private string GetContentPath(string documentId) => Path.Combine(_documentsFolder, $"{documentId}.txt");
        private string GetEmbeddingsPath(string documentId) => Path.Combine(_embeddingsFolder, $"{documentId}.json");


        public async Task<string> LoadContentAsync(string documentId)
        {
            string filePath = GetContentPath(documentId);
            var fileLock = GetFileLock(documentId);
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "FileSystemContentStore", $"Content file not found for ID {documentId} at {filePath}");
                    return string.Empty; // Or throw? Returning empty might be safer.
                }
                return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error loading content for ID {documentId}: {ex.Message}");
                throw; // Re-throw exceptions during load
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveContentAsync(string documentId, string content)
        {
            string filePath = GetContentPath(documentId);
            var fileLock = GetFileLock(documentId);
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.WriteAllTextAsync(filePath, content).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemContentStore", $"Saved content for ID {documentId} ({content.Length} chars)");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error saving content for ID {documentId}: {ex.Message}");
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task DeleteContentAsync(string documentId)
        {
            string filePath = GetContentPath(documentId);
            var fileLock = GetFileLock(documentId);
            await fileLock.WaitAsync().ConfigureAwait(false); // Ensure no writes are happening
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemContentStore", $"Deleted content file for ID {documentId}");
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error deleting content for ID {documentId}: {ex.Message}");
                // Log error, but might not need to re-throw depending on desired behavior
            }
            finally
            {
                fileLock.Release();
                // Attempt to remove the lock from the dictionary after deletion
                _fileLocks.TryRemove(documentId, out _);
            }
            await Task.CompletedTask; // Keep async signature consistent
        }

        public async Task<List<DocumentChunk>?> LoadEmbeddingsAsync(string documentId)
        {
            string filePath = GetEmbeddingsPath(documentId);
            var fileLock = GetFileLock(documentId); // Use same lock as content
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemContentStore", $"Embeddings file not found for ID {documentId} at {filePath}");
                    return null; // Return null if no embeddings file exists
                }
                string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<DocumentChunk>>(json);
            }
            catch (JsonException ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error deserializing embeddings for ID {documentId}: {ex.Message}");
                return null; // Return null on deserialization error
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error loading embeddings for ID {documentId}: {ex.Message}");
                throw; // Re-throw other exceptions
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveEmbeddingsAsync(string documentId, List<DocumentChunk> chunks)
        {
            string filePath = GetEmbeddingsPath(documentId);
            var fileLock = GetFileLock(documentId);
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string json = JsonSerializer.Serialize(chunks); // Consider options { WriteIndented = true } for debugging
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemContentStore", $"Saved {chunks?.Count ?? 0} chunks/embeddings for ID {documentId}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error saving embeddings for ID {documentId}: {ex.Message}");
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task DeleteEmbeddingsAsync(string documentId)
        {
            string filePath = GetEmbeddingsPath(documentId);
            var fileLock = GetFileLock(documentId);
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _diagnostics.Log(DiagnosticLevel.Debug, "FileSystemContentStore", $"Deleted embeddings file for ID {documentId}");
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "FileSystemContentStore", $"Error deleting embeddings for ID {documentId}: {ex.Message}");
                // Log error, but might not need to re-throw
            }
            finally
            {
                fileLock.Release();
                // Attempt to remove the lock from the dictionary after deletion
                _fileLocks.TryRemove(documentId, out _);
            }
            await Task.CompletedTask; // Keep async signature consistent
        }


        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Dispose all file locks
                    foreach (var kvp in _fileLocks)
                    {
                        kvp.Value?.Dispose();
                    }
                    _fileLocks.Clear();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}