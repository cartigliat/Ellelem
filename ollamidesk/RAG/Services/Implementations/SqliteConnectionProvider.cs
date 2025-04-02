// ollamidesk/RAG/Services/Implementations/SqliteConnectionProvider.cs
// New file
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models; // Needed for DocumentChunk during migration
using ollamidesk.RAG.Services.Interfaces;


namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Manages the SQLite database connection, initialization, and migration for the vector store.
    /// </summary>
    public class SqliteConnectionProvider : ISqliteConnectionProvider
    {
        private readonly string _dbPath;
        private readonly string _oldVectorsFolder; // For migration check
        private readonly RagDiagnosticsService _diagnostics;
        private SQLiteConnection? _connection;
        private bool _isInitialized = false;
        private bool _disposedValue;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1); // Guards initialization

        public SqliteConnectionProvider(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null) throw new ArgumentNullException(nameof(storageSettings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Database path setup
            string vectorsFolder = storageSettings.VectorsFolder; // Usually %LOCALAPPDATA%\OllamaDesk\vectors
            Directory.CreateDirectory(vectorsFolder);
            _dbPath = Path.Combine(vectorsFolder, "vectors.db");

            // Path to check for old *.vectors.json files for migration
            _oldVectorsFolder = storageSettings.EmbeddingsFolder; // Migration source

            _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", $"Provider initialized for database: {_dbPath}");
        }

        public async Task<SQLiteConnection> GetConnectionAsync()
        {
            // Ensure initialization is complete before returning connection
            if (!_isInitialized)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "SqliteConnectionProvider", "GetConnectionAsync called before explicit initialization. Ensure InitializeDatabaseAsync is called at startup.");
                await InitializeDatabaseAsync().ConfigureAwait(false); // Ensure init if called early
            }

            // If connection is null or closed after initialization (shouldn't happen with singleton normally)
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "SqliteConnectionProvider", "Connection was null or closed. Re-initializing.");
                // Re-run initialization logic safely
                await InitializeDatabaseAsync().ConfigureAwait(false);
                if (_connection == null) // If still null after re-init
                {
                    throw new InvalidOperationException("Failed to establish SQLite connection.");
                }
            }

            return _connection;
        }

        public async Task InitializeDatabaseAsync()
        {
            // Prevent concurrent initialization attempts
            if (_isInitialized) return;

            await _initializationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (_isInitialized) return;

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Starting database initialization...");

                string connectionString = $"Data Source={_dbPath};Version=3;Journal Mode=WAL;"; // Use WAL mode for better concurrency

                // Ensure connection is created and opened only once
                if (_connection == null)
                {
                    _connection = new SQLiteConnection(connectionString);
                    await _connection.OpenAsync().ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Debug, "SqliteConnectionProvider", "SQLite connection opened.");
                }
                else if (_connection.State != System.Data.ConnectionState.Open)
                {
                    await _connection.OpenAsync().ConfigureAwait(false);
                    _diagnostics.Log(DiagnosticLevel.Debug, "SqliteConnectionProvider", "Existing SQLite connection re-opened.");
                }


                // Create tables if they don't exist
                using (var command = _connection.CreateCommand())
                {
                    // Use TEXT primary key for GUIDs
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Documents (
                            DocumentId TEXT PRIMARY KEY NOT NULL,
                            Name TEXT NOT NULL
                        );";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    // Changed VectorJson to BLOB for potentially better performance/storage
                    // If sticking with JSON, keep as TEXT NOT NULL
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Chunks (
                            ChunkId TEXT PRIMARY KEY NOT NULL,
                            DocumentId TEXT NOT NULL,
                            Content TEXT NOT NULL,
                            ChunkIndex INTEGER NOT NULL,
                            Source TEXT NOT NULL,
                            VectorJson TEXT NOT NULL,
                            FOREIGN KEY (DocumentId) REFERENCES Documents(DocumentId) ON DELETE CASCADE
                        );";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    command.CommandText = "PRAGMA foreign_keys = ON;";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON Chunks(DocumentId);";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Database schema verified/created.");

                // Perform migration check
                await MigrateFromFileStorageIfNeededAsync().ConfigureAwait(false);

                _isInitialized = true; // Mark as initialized successfully
                _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Database initialization complete.");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Critical, "SqliteConnectionProvider", $"CRITICAL Error initializing database: {ex.Message}");
                // Clean up potentially bad connection state
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
                _isInitialized = false; // Ensure it retries if called again
                throw; // Re-throw critical error
            }
            finally
            {
                _initializationLock.Release();
            }
        }


        private async Task MigrateFromFileStorageIfNeededAsync()
        {
            // Logic moved from SqliteVectorStore, uses _connection directly
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteConnectionProvider", "Migration check skipped: Database connection is not open.");
                return;
            }

            // Check if migration is needed (DB empty, old files exist)
            using (var cmdCheck = _connection.CreateCommand())
            {
                cmdCheck.CommandText = "SELECT COUNT(*) FROM Chunks";
                long count = (long)(await cmdCheck.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
                if (count > 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "SqliteConnectionProvider", "Database already contains chunks, skipping migration.");
                    return; // Already has data
                }
            }

            if (!Directory.Exists(_oldVectorsFolder))
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteConnectionProvider", "Old embeddings folder not found, skipping migration.");
                return; // No source folder
            }

            var vectorFiles = Directory.GetFiles(_oldVectorsFolder, "*.json"); // Assuming embeddings were saved as .json
            if (vectorFiles.Length == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "SqliteConnectionProvider", "No old embedding files found, skipping migration.");
                return; // No files to migrate
            }

            _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", $"Found {vectorFiles.Length} potential embedding files to migrate from '{_oldVectorsFolder}'.");
            _diagnostics.StartOperation("MigrateEmbeddings");

            using (var transaction = _connection.BeginTransaction())
            {
                int migratedFileCount = 0;
                long totalMigratedChunks = 0;
                try
                {
                    // Prepare commands once
                    using var cmdDoc = _connection.CreateCommand();
                    cmdDoc.Transaction = transaction;
                    cmdDoc.CommandText = "INSERT OR IGNORE INTO Documents (DocumentId, Name) VALUES (@DocumentId, @Name)";
                    cmdDoc.Parameters.AddWithValue("@DocumentId", null);
                    cmdDoc.Parameters.AddWithValue("@Name", null);

                    using var cmdChunk = _connection.CreateCommand();
                    cmdChunk.Transaction = transaction;
                    cmdChunk.CommandText = @"
                            INSERT INTO Chunks (ChunkId, DocumentId, Content, ChunkIndex, Source, VectorJson)
                            VALUES (@ChunkId, @DocumentId, @Content, @ChunkIndex, @Source, @VectorJson)";
                    cmdChunk.Parameters.AddWithValue("@ChunkId", null);
                    cmdChunk.Parameters.AddWithValue("@DocumentId", null);
                    cmdChunk.Parameters.AddWithValue("@Content", null);
                    cmdChunk.Parameters.AddWithValue("@ChunkIndex", null);
                    cmdChunk.Parameters.AddWithValue("@Source", null);
                    cmdChunk.Parameters.AddWithValue("@VectorJson", null);


                    foreach (var file in vectorFiles)
                    {
                        string documentId = Path.GetFileNameWithoutExtension(file); // Infer ID from filename
                        try
                        {
                            string json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                            var chunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json);

                            if (chunks == null || chunks.Count == 0) continue;

                            // Add document entry (using file name as document name for migration)
                            cmdDoc.Parameters["@DocumentId"].Value = documentId;
                            cmdDoc.Parameters["@Name"].Value = documentId; // Or try to infer a better name?
                            await cmdDoc.ExecuteNonQueryAsync().ConfigureAwait(false);

                            // Add chunks
                            foreach (var chunk in chunks)
                            {
                                // Ensure chunk has necessary IDs if old format was different
                                chunk.DocumentId = documentId; // Ensure consistent ID
                                if (string.IsNullOrEmpty(chunk.Id)) chunk.Id = Guid.NewGuid().ToString();

                                string vectorJson = JsonSerializer.Serialize(chunk.Embedding);

                                cmdChunk.Parameters["@ChunkId"].Value = chunk.Id;
                                cmdChunk.Parameters["@DocumentId"].Value = chunk.DocumentId;
                                cmdChunk.Parameters["@Content"].Value = chunk.Content;
                                cmdChunk.Parameters["@ChunkIndex"].Value = chunk.ChunkIndex;
                                cmdChunk.Parameters["@Source"].Value = chunk.Source ?? documentId; // Fallback source
                                cmdChunk.Parameters["@VectorJson"].Value = vectorJson;
                                await cmdChunk.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                            _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", $"Migrated {chunks.Count} chunks for document {documentId} from file: {Path.GetFileName(file)}");
                            migratedFileCount++;
                            totalMigratedChunks += chunks.Count;
                        }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "SqliteConnectionProvider", $"Error migrating file {Path.GetFileName(file)}: {ex.Message}");
                            // Continue to next file
                        }
                    }
                    transaction.Commit();
                    _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", $"Migration committed. Migrated {totalMigratedChunks} chunks from {migratedFileCount} files.");

                    // Optionally delete old files after successful migration
                    // foreach (var file in vectorFiles) { try { File.Delete(file); } catch { /* Ignore */ } }

                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _diagnostics.Log(DiagnosticLevel.Error, "SqliteConnectionProvider", $"Migration failed and rolled back: {ex.Message}");
                    throw; // Re-throw transaction error
                }
            } // End transaction

            _diagnostics.EndOperation("MigrateEmbeddings");
        }


        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Disposing connection provider.");
                    _connection?.Close();
                    _connection?.Dispose();
                    _connection = null;
                    _initializationLock?.Dispose();
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