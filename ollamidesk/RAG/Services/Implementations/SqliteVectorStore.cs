// ollamidesk/RAG/Services/Implementations/SqliteVectorStore.cs
// Refactored to use ISqliteConnectionProvider
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.Configuration; // May not be needed directly
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces; // Use interfaces
using System.Data; // Required for CommandBehavior

namespace ollamidesk.RAG.Services
{
    public class SqliteVectorStore : IVectorStore
    {
        private readonly ISqliteConnectionProvider _connectionProvider; // Use provider
        private readonly RagDiagnosticsService _diagnostics;
        // Removed connection, initialization fields, and semaphores - handled by provider


        // Inject the connection provider instead of StorageSettings
        public SqliteVectorStore(ISqliteConnectionProvider connectionProvider, RagDiagnosticsService diagnostics)
        {
            _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore", "Vector store initialized using ISqliteConnectionProvider.");
        }

        // Removed EnsureInitializedAsync and MigrateFromFileStorageIfNeededAsync
        // Initialization is now handled by calling _connectionProvider.InitializeDatabaseAsync() at startup (via DI setup maybe)

        public async Task AddVectorsAsync(List<DocumentChunk> chunks)
        {
            if (chunks == null || chunks.Count == 0) return;

            _diagnostics.StartOperation("Store.AddVectors");
            SQLiteConnection connection = await _connectionProvider.GetConnectionAsync().ConfigureAwait(false);

            // Group by document for efficient transaction handling
            var docGroups = chunks.GroupBy(c => c.DocumentId);

            foreach (var group in docGroups)
            {
                string documentId = group.Key;
                string documentName = group.First().Source ?? documentId; // Get name from first chunk or use ID

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Add/Update Document Entry
                        using (var cmdDoc = connection.CreateCommand())
                        {
                            cmdDoc.Transaction = transaction;
                            cmdDoc.CommandText = "INSERT OR REPLACE INTO Documents (DocumentId, Name) VALUES (@DocumentId, @Name)";
                            cmdDoc.Parameters.AddWithValue("@DocumentId", documentId);
                            cmdDoc.Parameters.AddWithValue("@Name", documentName);
                            await cmdDoc.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        // 2. Delete existing chunks for this document to prevent duplicates/orphans
                        using (var cmdDelete = connection.CreateCommand())
                        {
                            cmdDelete.Transaction = transaction;
                            cmdDelete.CommandText = "DELETE FROM Chunks WHERE DocumentId = @DocumentId";
                            cmdDelete.Parameters.AddWithValue("@DocumentId", documentId);
                            int deletedRows = await cmdDelete.ExecuteNonQueryAsync().ConfigureAwait(false);
                            if (deletedRows > 0)
                            {
                                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteVectorStore", $"Deleted {deletedRows} existing chunks for document {documentId} before adding new ones.");
                            }
                        }

                        // 3. Insert new chunks
                        using (var cmdChunk = connection.CreateCommand())
                        {
                            cmdChunk.Transaction = transaction;
                            cmdChunk.CommandText = @"
                                INSERT INTO Chunks (ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson)
                                VALUES (@ChunkId, @DocumentId, @Content, @ChunkIndex, @Source, @VectorJson)";

                            // Prepare parameters once
                            cmdChunk.Parameters.Add(new SQLiteParameter("@ChunkId", DbType.String));
                            cmdChunk.Parameters.Add(new SQLiteParameter("@DocumentId", DbType.String) { Value = documentId }); // Set once per doc
                            cmdChunk.Parameters.Add(new SQLiteParameter("@Content", DbType.String));
                            cmdChunk.Parameters.Add(new SQLiteParameter("@ChunkIndex", DbType.Int32));
                            cmdChunk.Parameters.Add(new SQLiteParameter("@Source", DbType.String));
                            cmdChunk.Parameters.Add(new SQLiteParameter("@VectorJson", DbType.String)); // Or DbType.Blob if using BLOB


                            foreach (var chunk in group)
                            {
                                if (chunk.Embedding == null || chunk.Embedding.Length == 0)
                                {
                                    _diagnostics.Log(DiagnosticLevel.Warning, "SqliteVectorStore", $"Skipping chunk {chunk.Id} for document {documentId} due to missing embedding.");
                                    continue;
                                }

                                string vectorJson = JsonSerializer.Serialize(chunk.Embedding);

                                cmdChunk.Parameters["@ChunkId"].Value = chunk.Id ?? Guid.NewGuid().ToString(); // Ensure ID
                                cmdChunk.Parameters["@Content"].Value = chunk.Content;
                                cmdChunk.Parameters["@ChunkIndex"].Value = chunk.ChunkIndex;
                                cmdChunk.Parameters["@Source"].Value = chunk.Source ?? documentName;
                                cmdChunk.Parameters["@VectorJson"].Value = vectorJson; // Store as JSON string

                                await cmdChunk.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }

                        transaction.Commit();
                        _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore", $"Successfully added/updated {group.Count()} vectors for document {documentId}.");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore", $"Error adding vectors for document {documentId} (rolled back): {ex.Message}");
                        throw; // Re-throw error after rollback
                    }
                } // End transaction
            } // End foreach group

            _diagnostics.EndOperation("Store.AddVectors");
        }

        public async Task RemoveVectorsAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId)) return;
            _diagnostics.StartOperation("Store.RemoveVectors");
            SQLiteConnection connection = await _connectionProvider.GetConnectionAsync().ConfigureAwait(false);

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    int deletedChunks = 0;
                    // Delete chunks first due to foreign key constraint
                    using (var cmdChunks = connection.CreateCommand())
                    {
                        cmdChunks.Transaction = transaction;
                        cmdChunks.CommandText = "DELETE FROM Chunks WHERE DocumentId = @DocumentId";
                        cmdChunks.Parameters.AddWithValue("@DocumentId", documentId);
                        deletedChunks = await cmdChunks.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    // Delete document entry
                    using (var cmdDoc = connection.CreateCommand())
                    {
                        cmdDoc.Transaction = transaction;
                        cmdDoc.CommandText = "DELETE FROM Documents WHERE DocumentId = @DocumentId";
                        cmdDoc.Parameters.AddWithValue("@DocumentId", documentId);
                        await cmdDoc.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    transaction.Commit();
                    _diagnostics.Log(DiagnosticLevel.Info, "SqliteVectorStore", $"Removed document {documentId} and {deletedChunks} associated chunks.");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore", $"Error removing vectors for document {documentId} (rolled back): {ex.Message}");
                    throw;
                }
            }
            _diagnostics.EndOperation("Store.RemoveVectors");
        }

        // Search methods now get connection from provider and handle deserialization here
        public async Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(float[] queryVector, int limit = 5)
        {
            if (queryVector == null || queryVector.Length == 0) return new List<(DocumentChunk, float)>();

            _diagnostics.StartOperation("Store.Search");
            var results = new List<(DocumentChunk Chunk, float Score)>();
            SQLiteConnection connection = await _connectionProvider.GetConnectionAsync().ConfigureAwait(false);

            try
            {
                using (var command = connection.CreateCommand())
                {
                    // Select necessary fields including the VectorJson
                    command.CommandText = "SELECT ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson FROM Chunks";

                    using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            string vectorJson = reader.GetString(5);
                            float[]? chunkEmbedding = JsonSerializer.Deserialize<float[]>(vectorJson);

                            if (chunkEmbedding != null && chunkEmbedding.Length > 0)
                            {
                                float score = CosineSimilarity(queryVector, chunkEmbedding);
                                results.Add((new DocumentChunk
                                {
                                    Id = reader.GetString(0),
                                    DocumentId = reader.GetString(1),
                                    Content = reader.GetString(2),
                                    ChunkIndex = reader.GetInt32(3),
                                    Source = reader.GetString(4),
                                    Embedding = chunkEmbedding // Keep embedding in chunk if needed later
                                }, score));
                            }
                        }
                    }
                }

                // Sort and limit in memory
                results = results.OrderByDescending(x => x.Score).Take(limit).ToList();
                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteVectorStore", $"Search returned {results.Count} results. Top score: {(results.Count > 0 ? results[0].Score.ToString("F4") : "N/A")}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore", $"Error during vector search: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Store.Search");
            }
            return results;
        }

        public async Task<List<(DocumentChunk Chunk, float Score)>> SearchInDocumentsAsync(
           float[] queryVector, List<string> documentIds, int limit = 5)
        {
            if (queryVector == null || queryVector.Length == 0 || documentIds == null || documentIds.Count == 0)
                return new List<(DocumentChunk, float)>();

            _diagnostics.StartOperation("Store.SearchInDocuments");
            var results = new List<(DocumentChunk Chunk, float Score)>();
            SQLiteConnection connection = await _connectionProvider.GetConnectionAsync().ConfigureAwait(false);

            try
            {
                // Use parameterized query for document IDs
                var parameters = documentIds.Select((id, index) => $"@p{index}").ToArray();
                string docIdPlaceholders = string.Join(",", parameters);


                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson FROM Chunks WHERE DocumentId IN ({docIdPlaceholders})";
                    // Add parameters
                    for (int i = 0; i < documentIds.Count; i++)
                    {
                        command.Parameters.AddWithValue(parameters[i], documentIds[i]);
                    }

                    using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            string vectorJson = reader.GetString(5);
                            float[]? chunkEmbedding = JsonSerializer.Deserialize<float[]>(vectorJson);

                            if (chunkEmbedding != null && chunkEmbedding.Length > 0)
                            {
                                float score = CosineSimilarity(queryVector, chunkEmbedding);
                                results.Add((new DocumentChunk
                                {
                                    Id = reader.GetString(0),
                                    DocumentId = reader.GetString(1),
                                    Content = reader.GetString(2),
                                    ChunkIndex = reader.GetInt32(3),
                                    Source = reader.GetString(4),
                                    Embedding = chunkEmbedding
                                }, score));
                            }
                        }
                    }
                }

                results = results.OrderByDescending(x => x.Score).Take(limit).ToList();
                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteVectorStore", $"Document-filtered search returned {results.Count} results from {documentIds.Count} documents. Top score: {(results.Count > 0 ? results[0].Score.ToString("F4") : "N/A")}");

            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteVectorStore", $"Error during document-filtered vector search: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Store.SearchInDocuments");
            }
            return results;
        }


        // CosineSimilarity is a stateless utility function, keep it here.
        private float CosineSimilarity(float[] v1, float[] v2)
        {
            if (v1 == null || v2 == null || v1.Length != v2.Length || v1.Length == 0) return 0;
            double dotProduct = 0.0;
            double magnitude1 = 0.0;
            double magnitude2 = 0.0;
            for (int i = 0; i < v1.Length; i++)
            {
                dotProduct += v1[i] * v2[i];
                magnitude1 += v1[i] * v1[i];
                magnitude2 += v2[i] * v2[i];
            }
            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);
            if (magnitude1 == 0 || magnitude2 == 0) return 0;
            return (float)(dotProduct / (magnitude1 * magnitude2));
        }

        // Removed Dispose method - connection lifecycle managed by SqliteConnectionProvider
    }
}