// ollamidesk/RAG/Services/Interfaces/ISqliteConnectionProvider.cs
using System;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Provides SQLite database connections and manages database lifecycle
    /// </summary>
    public interface ISqliteConnectionProvider : IDisposable
    {
        /// <summary>
        /// Gets a database connection. The connection is managed by the provider.
        /// </summary>
        /// <returns>An open SQLite connection</returns>
        Task<SQLiteConnection> GetConnectionAsync();

        /// <summary>
        /// Initializes the database schema and prepares the connection
        /// </summary>
        Task InitializeDatabaseAsync();

        /// <summary>
        /// Tests if the database connection is working
        /// </summary>
        /// <returns>True if connection is successful</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Closes all connections and cleans up resources
        /// </summary>
        Task CloseAsync();
    }
}