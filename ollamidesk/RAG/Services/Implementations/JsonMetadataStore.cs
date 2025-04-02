// ollamidesk/RAG/Services/Implementations/JsonMetadataStore.cs
// New file
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Stores document metadata in a single JSON file (library.json).
    /// </summary>
    public class JsonMetadataStore : IMetadataStore
    {
        private readonly string _metadataFile;
        private readonly RagDiagnosticsService _diagnostics;
        private Dictionary<string, DocumentMetadata> _metadataCache = new();
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // Single lock for the metadata file
        private bool _disposedValue;


        public JsonMetadataStore(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null) throw new ArgumentNullException(nameof(storageSettings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _metadataFile = storageSettings.MetadataFile;

            // Ensure base directory exists (needed for the file lock)
            Directory.CreateDirectory(Path.GetDirectoryName(_metadataFile)!);
            _diagnostics.Log(DiagnosticLevel.Info, "JsonMetadataStore", $"Store initialized for file: {_metadataFile}");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;

            await _initializationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized) return; // Double check after lock

                await LoadFromFileAsync().ConfigureAwait(false);
                _isInitialized = true;
                _diagnostics.Log(DiagnosticLevel.Info, "JsonMetadataStore", $"Initialization complete. Cache contains {_metadataCache.Count} metadata entries.");
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task LoadFromFileAsync()
        {
            await _fileLock.WaitAsync().ConfigureAwait(false); // Lock the metadata file
            try
            {
                if (!File.Exists(_metadataFile))
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "JsonMetadataStore", $"Metadata file not found: {_metadataFile}. Starting fresh.");
                    _metadataCache = new Dictionary<string, DocumentMetadata>();
                    return;
                }

                try
                {
                    string json = await File.ReadAllTextAsync(_metadataFile).ConfigureAwait(false);
                    var loadedData = JsonSerializer.Deserialize<List<DocumentMetadata>>(json);
                    _metadataCache = loadedData?.ToDictionary(m => m.Id, m => m) ?? new Dictionary<string, DocumentMetadata>();
                    _diagnostics.Log(DiagnosticLevel.Debug, "JsonMetadataStore", $"Loaded {_metadataCache.Count} metadata entries from file.");
                }
                catch (JsonException ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "JsonMetadataStore", $"Error deserializing metadata file {_metadataFile}: {ex.Message}. Starting with empty cache.");
                    _metadataCache = new Dictionary<string, DocumentMetadata>();
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "JsonMetadataStore", $"Error reading metadata file {_metadataFile}: {ex.Message}. Starting with empty cache.");
                    _metadataCache = new Dictionary<string, DocumentMetadata>();
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<Dictionary<string, DocumentMetadata>> LoadMetadataAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            // Return a copy to prevent external modification of the cache
            return new Dictionary<string, DocumentMetadata>(_metadataCache);
        }

        public async Task SaveMetadataAsync(Dictionary<string, DocumentMetadata> metadata)
        {
            await EnsureInitializedAsync().ConfigureAwait(false); // Ensure cache is loaded before overwriting
            _diagnostics.Log(DiagnosticLevel.Debug, "JsonMetadataStore", $"Saving {metadata.Count} metadata entries to file.");
            _metadataCache = new Dictionary<string, DocumentMetadata>(metadata); // Update cache
            await SaveToFileAsync().ConfigureAwait(false);
        }

        public async Task<DocumentMetadata?> GetMetadataByIdAsync(string documentId)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            _metadataCache.TryGetValue(documentId, out var metadata);
            return metadata; // Returns null if not found
        }

        public async Task SaveMetadataAsync(DocumentMetadata metadata)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            _metadataCache[metadata.Id] = metadata; // Add or update
            await SaveToFileAsync().ConfigureAwait(false);
            _diagnostics.Log(DiagnosticLevel.Debug, "JsonMetadataStore", $"Saved metadata for document ID: {metadata.Id}");
        }

        public async Task DeleteMetadataAsync(string documentId)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            if (_metadataCache.Remove(documentId))
            {
                await SaveToFileAsync().ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "JsonMetadataStore", $"Deleted metadata for document ID: {documentId}");
            }
            else
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "JsonMetadataStore", $"Attempted to delete non-existent metadata for document ID: {documentId}");
            }
        }

        private async Task SaveToFileAsync()
        {
            await _fileLock.WaitAsync().ConfigureAwait(false); // Lock the metadata file
            try
            {
                // Serialize the current cache content
                string json = JsonSerializer.Serialize(_metadataCache.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_metadataFile, json).ConfigureAwait(false);
                _diagnostics.Log(DiagnosticLevel.Debug, "JsonMetadataStore", $"Metadata file saved: {_metadataFile}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "JsonMetadataStore", $"Error saving metadata to file {_metadataFile}: {ex.Message}");
                // Optionally re-throw or handle more gracefully
            }
            finally
            {
                _fileLock.Release();
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _initializationLock?.Dispose();
                    _fileLock?.Dispose();
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