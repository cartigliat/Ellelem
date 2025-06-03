// ollamidesk/RAG/Services/Implementations/SqliteConnectionProvider.cs
using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Provides SQLite database connections with proper lifecycle management
    /// </summary>
    public class SqliteConnectionProvider : ISqliteConnectionProvider
    {
        private readonly string _databasePath;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly SemaphoreSlim _connectionSemaphore;
        private SQLiteConnection? _connection;
        private bool _isInitialized = false;
        private bool _disposed = false;
        private readonly object _lockObject = new object();

        public SqliteConnectionProvider(StorageSettings storageSettings, RagDiagnosticsService diagnostics)
        {
            if (storageSettings == null) throw new ArgumentNullException(nameof(storageSettings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _databasePath = Path.Combine(storageSettings.BasePath, "vectors.db");
            _connectionSemaphore = new SemaphoreSlim(1, 1);

            _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", $"Provider initialized with database path: {_databasePath}");
        }

        public async Task<SQLiteConnection> GetConnectionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqliteConnectionProvider));

            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                {
                    await CreateConnectionAsync().ConfigureAwait(false);
                }
                return _connection!;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        public async Task InitializeDatabaseAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqliteConnectionProvider));

            if (_isInitialized) return;

            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized) return; // Double-check after acquiring lock

                _diagnostics.StartOperation("Database.Initialize");

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

                // Create connection
                await CreateConnectionAsync().ConfigureAwait(false);

                // Create tables
                await CreateTablesAsync().ConfigureAwait(false);

                _isInitialized = true;
                _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Database initialized successfully");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteConnectionProvider", $"Failed to initialize database: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("Database.Initialize");
                _connectionSemaphore.Release();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_disposed) return false;

            try
            {
                var connection = await GetConnectionAsync().ConfigureAwait(false);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return result != null;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SqliteConnectionProvider", $"Connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task CloseAsync()
        {
            await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_connection != null)
                {
                    if (_connection.State == System.Data.ConnectionState.Open)
                    {
                        _connection.Close();
                    }
                    _connection.Dispose();
                    _connection = null;
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SqliteConnectionProvider", "Database connection closed");
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task CreateConnectionAsync()
        {
            if (_connection != null)
            {
                _connection.Dispose();
            }

            var connectionString = $"Data Source={_databasePath};Version=3;";
            _connection = new SQLiteConnection(connectionString);
            await _connection.OpenAsync().ConfigureAwait(false);
        }

        private async Task CreateTablesAsync()
        {
            if (_connection == null) throw new InvalidOperationException("Connection not established");

            using var command = _connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Documents (
                    DocumentId TEXT PRIMARY KEY,
                    Name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Chunks (
                    ChunkId TEXT PRIMARY KEY,
                    DocumentId TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    ChunkIndex INTEGER NOT NULL,
                    Source TEXT,
                    VectorJson TEXT NOT NULL,
                    FOREIGN KEY (DocumentId) REFERENCES Documents(DocumentId) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON Chunks(DocumentId);
                CREATE INDEX IF NOT EXISTS idx_chunks_chunk_index ON Chunks(ChunkIndex);
            ";

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        // Close connection synchronously during disposal
                        CloseAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        _diagnostics?.Log(DiagnosticLevel.Warning, "SqliteConnectionProvider", $"Error during disposal: {ex.Message}");
                    }

                    _connectionSemaphore?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}