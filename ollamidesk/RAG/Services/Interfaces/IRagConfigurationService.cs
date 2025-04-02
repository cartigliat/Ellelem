using System.ComponentModel;
using System.Threading.Tasks;
using ollamidesk.Configuration;

namespace ollamidesk.RAG.Services
{
    /// <summary>
    /// Interface for the RAG configuration service that manages all RAG-related settings.
    /// This is the single source of truth for RAG configuration in the application.
    /// </summary>
    public interface IRagConfigurationService : INotifyPropertyChanged
    {
        // Core chunking parameters
        int ChunkSize { get; set; }
        int ChunkOverlap { get; set; }

        // Retrieval parameters
        int MaxRetrievedChunks { get; set; }
        float MinSimilarityScore { get; set; }

        // Additional parameters
        bool UseSemanticChunking { get; set; }
        int EmbeddingModelDimension { get; set; }

        // Add this property:
        int EmbeddingBatchSize { get; set; }

        // Methods
        /// <summary>
        /// Saves configuration changes to disk
        /// </summary>
        Task SaveConfigurationAsync();

        /// <summary>
        /// Resets all configuration to default values
        /// </summary>
        Task ResetToDefaultsAsync();

        /// <summary>
        /// Returns a copy of the current RAG settings
        /// </summary>
        /// <returns>Current RAG settings</returns>
        RagSettings GetCurrentSettings();

        /// <summary>
        /// Applies a new configuration from RagSettings
        /// </summary>
        /// <param name="settings">New settings to apply</param>
        Task ApplyConfigurationAsync(RagSettings settings);
    }
}