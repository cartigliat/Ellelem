using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;

namespace ollamidesk.RAG.Services
{
    public class SqliteVectorStore : IVectorStore, IDisposable
    {
        private readonly string _dbPath;
        private readonly RagDiagnosticsService _diagnostics;
        private SQLiteConnection? _connection;
        private bool _isInitialized = false;
        private bool _disposedValue;

        public SqliteVectorStore(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null)
                throw new ArgumentNullException(nameof(storageSettings));

            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Create vectors directory if it doesn't exist
            string vectorsFolder = storageSettings.VectorsFolder;
            Directory.CreateDirectory(vectorsFolder);

            // Set up database path
            _dbPath = Path.Combine(vectorsFolder, "vectors.db");

            _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                $"Vector store initialized with database: {_dbPath}");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
                return;

            _diagnostics.StartOperation("InitializeVectorStore");

            try
            {
                // Create the connection string
                string connectionString = $"Data Source={_dbPath};Version=3;";

                // Create the connection
                _connection = new SQLiteConnection(connectionString);
                await _connection.OpenAsync();

                // Create tables if they don't exist
                using (var command = _connection.CreateCommand())
                {
                    // Documents table
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Documents (
                            DocumentId TEXT PRIMARY KEY,
                            Name TEXT NOT NULL
                        )";
                    await command.ExecuteNonQueryAsync();

                    // Chunks table with vector storage
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Chunks (
                            ChunkId TEXT PRIMARY KEY,
                            DocumentId TEXT NOT NULL,
                            Content TEXT NOT NULL,
                            ChunkIndex INTEGER NOT NULL,
                            Source TEXT NOT NULL,
                            VectorJson TEXT NOT NULL,
                            FOREIGN KEY (DocumentId) REFERENCES Documents(DocumentId) ON DELETE CASCADE
                        )";
                    await command.ExecuteNonQueryAsync();

                    // Enable foreign keys
                    command.CommandText = "PRAGMA foreign_keys = ON";
                    await command.ExecuteNonQueryAsync();

                    // Create index on DocumentId for faster lookups
                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON Chunks(DocumentId)";
                    await command.ExecuteNonQueryAsync();
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore", "Database initialized successfully");

                // Check if we need to migrate data from file-based storage
                await MigrateFromFileStorageIfNeededAsync();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                    $"Error initializing database: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("InitializeVectorStore");
            }
        }

        private async Task MigrateFromFileStorageIfNeededAsync()
        {
            try
            {
                // Check if we have existing vector files to migrate
                string? vectorsFolder = Path.GetDirectoryName(_dbPath);
                if (string.IsNullOrEmpty(vectorsFolder))
                    return;

                var vectorFiles = Directory.GetFiles(vectorsFolder, "*.vectors.json");
                if (vectorFiles.Length == 0)
                    return;

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                    $"Found {vectorFiles.Length} vector files to migrate");

                // Check if we have already migrated data
                if (_connection != null)
                {
                    using (var command = _connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM Chunks";
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());

                        if (count > 0)
                        {
                            _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                                "Database already contains vectors, skipping migration");
                            return;
                        }
                    }
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                        "Connection is null during migration");
                    return;
                }

                _diagnostics.StartOperation("MigrateVectors");

                // Migrate each file
                foreach (var file in vectorFiles)
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(file);
                        var chunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json);

                        if (chunks == null || chunks.Count == 0)
                            continue;

                        // Get document ID from the first chunk
                        string documentId = chunks[0].DocumentId;

                        // Add document
                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = @"
                                INSERT OR IGNORE INTO Documents (DocumentId, Name) 
                                VALUES (@DocumentId, @Name)";
                            command.Parameters.AddWithValue("@DocumentId", documentId);
                            command.Parameters.AddWithValue("@Name", Path.GetFileNameWithoutExtension(file));
                            await command.ExecuteNonQueryAsync();
                        }

                        // Add chunks
                        await AddVectorsInternalAsync(chunks);

                        _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                            $"Migrated {chunks.Count} vectors from file: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                            $"Error migrating file {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                _diagnostics.EndOperation("MigrateVectors");
                _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore", "Vector migration completed");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                    $"Error during migration: {ex.Message}");
            }
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

                foreach (var group in docGroups)
                {
                    string documentId = group.Key;

                    // Add or update document entry
                    if (_connection != null)
                    {
                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = @"
                                INSERT OR REPLACE INTO Documents (DocumentId, Name) 
                                VALUES (@DocumentId, @Name)";
                            command.Parameters.AddWithValue("@DocumentId", documentId);
                            command.Parameters.AddWithValue("@Name", group.First().Source);
                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    // Add chunks
                    await AddVectorsInternalAsync(group.ToList());
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                    $"Added {chunks.Count} vectors for {docGroups.Count()} documents");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                    $"Error adding vectors: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("AddVectors");
            }
        }

        private async Task AddVectorsInternalAsync(List<DocumentChunk> chunks)
        {
            if (chunks == null || chunks.Count == 0 || _connection == null)
                return;

            // First remove any existing chunks for this document to avoid duplicates
            string documentId = chunks[0].DocumentId;
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Chunks WHERE DocumentId = @DocumentId";
                command.Parameters.AddWithValue("@DocumentId", documentId);
                await command.ExecuteNonQueryAsync();
            }

            // Use a transaction for better performance
            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    using (var command = _connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT INTO Chunks (ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson)
                            VALUES (@ChunkId, @DocumentId, @Content, @ChunkIndex, @Source, @VectorJson)";

                        // Create parameters once
                        var pChunkId = command.CreateParameter();
                        pChunkId.ParameterName = "@ChunkId";
                        command.Parameters.Add(pChunkId);

                        var pDocumentId = command.CreateParameter();
                        pDocumentId.ParameterName = "@DocumentId";
                        command.Parameters.Add(pDocumentId);

                        var pContent = command.CreateParameter();
                        pContent.ParameterName = "@Content";
                        command.Parameters.Add(pContent);

                        var pChunkIndex = command.CreateParameter();
                        pChunkIndex.ParameterName = "@ChunkIndex";
                        command.Parameters.Add(pChunkIndex);

                        var pSource = command.CreateParameter();
                        pSource.ParameterName = "@Source";
                        command.Parameters.Add(pSource);

                        var pVectorJson = command.CreateParameter();
                        pVectorJson.ParameterName = "@VectorJson";
                        command.Parameters.Add(pVectorJson);

                        // Add each chunk
                        foreach (var chunk in chunks)
                        {
                            // Serialize the vector to JSON
                            string vectorJson = JsonSerializer.Serialize(chunk.Embedding);

                            // Set parameter values
                            pChunkId.Value = chunk.Id;
                            pDocumentId.Value = chunk.DocumentId;
                            pContent.Value = chunk.Content;
                            pChunkIndex.Value = chunk.ChunkIndex;
                            pSource.Value = chunk.Source;
                            pVectorJson.Value = vectorJson;

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public async Task RemoveVectorsAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
                return;

            await EnsureInitializedAsync();
            _diagnostics.StartOperation("RemoveVectors");

            try
            {
                if (_connection == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                        "Connection is null when trying to remove vectors");
                    return;
                }

                // First count how many chunks will be removed
                int chunksToRemove = 0;
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM Chunks WHERE DocumentId = @DocumentId";
                    command.Parameters.AddWithValue("@DocumentId", documentId);
                    chunksToRemove = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Use a transaction for both operations
                using (var transaction = _connection.BeginTransaction())
                {
                    try
                    {
                        // Delete chunks
                        using (var command = _connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "DELETE FROM Chunks WHERE DocumentId = @DocumentId";
                            command.Parameters.AddWithValue("@DocumentId", documentId);
                            await command.ExecuteNonQueryAsync();
                        }

                        // Delete document
                        using (var command = _connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "DELETE FROM Documents WHERE DocumentId = @DocumentId";
                            command.Parameters.AddWithValue("@DocumentId", documentId);
                            await command.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore",
                    $"Removed {chunksToRemove} vectors for document {documentId}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
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
            if (queryVector == null || queryVector.Length == 0)
                return new List<(DocumentChunk, float)>();

            await EnsureInitializedAsync();
            _diagnostics.StartOperation("VectorSearch");

            try
            {
                var results = new List<(DocumentChunk, float)>();

                if (_connection == null)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                        "Connection is null during search");
                    return results;
                }

                // Get all chunks and calculate similarity in memory
                // Note: This is a simplified approach. For production use with large datasets,
                // consider using SQLite extensions like sqlite-vss or implementing an indexing strategy
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = "SELECT ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson FROM Chunks";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Create chunk
                            var chunk = new DocumentChunk
                            {
                                Id = reader.GetString(0),
                                DocumentId = reader.GetString(1),
                                Content = reader.GetString(2),
                                ChunkIndex = reader.GetInt32(3),
                                Source = reader.GetString(4)
                            };

                            // Deserialize vector
                            string vectorJson = reader.GetString(5);
                            float[]? chunkEmbedding = JsonSerializer.Deserialize<float[]>(vectorJson);
                            chunk.Embedding = chunkEmbedding ?? Array.Empty<float>();

                            // Calculate similarity score
                            float score = CosineSimilarity(queryVector, chunk.Embedding);

                            // Add to results
                            results.Add((chunk, score));
                        }
                    }
                }

                // Sort and limit results
                results = results
                    .OrderByDescending(x => x.Item2)
                    .Take(limit)
                    .ToList();

                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteVectorStore",
                    $"Search returned {results.Count} results with top score of {(results.Count > 0 ? results[0].Item2.ToString("F4") : "N/A")}");

                return results;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore",
                    $"Error during vector search: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("VectorSearch");
            }
        }

        private float CosineSimilarity(float[] v1, float[]? v2)
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

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _connection?.Close();
                    _connection?.Dispose();
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