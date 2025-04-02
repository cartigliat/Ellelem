// ollamidesk/RAG/Services/Interfaces/ISqliteConnectionProvider.cs
// New file
using System.Data.SQLite;
using System.Threading.Tasks;
using System;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Interface for managing and providing SQLite database connections for the vector store.
    /// </summary>
    public interface ISqliteConnectionProvider : IDisposable
    {
        /// <summary>
        /// Gets an open SQLite connection, ensuring the database is initialized.
        /// </summary>
        /// <returns>An open SQLiteConnection.</returns>
        Task<SQLiteConnection> GetConnectionAsync();

        /// <summary>
        /// Ensures the database schema is created and any necessary migrations are run.
        /// To be called once during application startup or service initialization.
        /// </summary>
        Task InitializeDatabaseAsync();
    }
}