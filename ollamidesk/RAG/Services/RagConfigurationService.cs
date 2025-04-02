using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.Services
{
    /// <summary>
    /// Central service for managing all RAG configuration parameters.
    /// This is the single source of truth for RAG configuration in the application.
    /// </summary>
    public class RagConfigurationService : IRagConfigurationService
    {
        private readonly RagDiagnosticsService _diagnostics;
        private readonly ConfigurationProvider _configProvider;

        private int _chunkSize;
        private int _chunkOverlap;
        private int _maxRetrievedChunks;
        private float _minSimilarityScore;
        private bool _useSemanticChunking;
        private int _embeddingBatchSize;
        private int _embeddingModelDimension;

        // Modified: Changed to nullable event handler
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Initializes a new instance of the RagConfigurationService class.
        /// </summary>
        /// <param name="initialSettings">Initial RAG settings</param>
        /// <param name="configProvider">Configuration provider</param>
        /// <param name="diagnostics">Diagnostics service</param>
        public RagConfigurationService(
            RagSettings initialSettings,
            ConfigurationProvider configProvider,
            RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));

            // Initialize from settings
            _chunkSize = initialSettings.ChunkSize;
            _chunkOverlap = initialSettings.ChunkOverlap;
            _maxRetrievedChunks = initialSettings.MaxRetrievedChunks;
            _minSimilarityScore = initialSettings.MinSimilarityScore;
            _embeddingBatchSize = initialSettings.EmbeddingBatchSize;

            // Default values for any new parameters
            _useSemanticChunking = false;
            _embeddingModelDimension = 384; // Default for many embedding models

            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Configuration service initialized");
        }

        public int ChunkSize
        {
            get => _chunkSize;
            set => SetProperty(ref _chunkSize, value);
        }

        public int ChunkOverlap
        {
            get => _chunkOverlap;
            set => SetProperty(ref _chunkOverlap, value);
        }

        public int MaxRetrievedChunks
        {
            get => _maxRetrievedChunks;
            set => SetProperty(ref _maxRetrievedChunks, value);
        }

        public float MinSimilarityScore
        {
            get => _minSimilarityScore;
            set => SetProperty(ref _minSimilarityScore, value);
        }

        public bool UseSemanticChunking
        {
            get => _useSemanticChunking;
            set => SetProperty(ref _useSemanticChunking, value);
        }

        public int EmbeddingModelDimension
        {
            get => _embeddingModelDimension;
            set => SetProperty(ref _embeddingModelDimension, value);
        }

        public int EmbeddingBatchSize
        {
            get => _embeddingBatchSize;
            set => SetProperty(ref _embeddingBatchSize, value);
        }

        /// <summary>
        /// Returns a copy of the current RAG settings
        /// </summary>
        /// <returns>Current RAG settings</returns>
        public RagSettings GetCurrentSettings()
        {
            return new RagSettings
            {
                ChunkSize = ChunkSize,
                ChunkOverlap = ChunkOverlap,
                MaxRetrievedChunks = MaxRetrievedChunks,
                MinSimilarityScore = MinSimilarityScore,
                EmbeddingBatchSize = EmbeddingBatchSize
                // Note: Extended properties not in RagSettings yet
            };
        }

        /// <summary>
        /// Saves configuration changes to disk
        /// </summary>
        public async Task SaveConfigurationAsync()
        {
            try
            {
                _diagnostics.StartOperation("SaveRagConfiguration");

                // Update the app settings
                var appSettings = _configProvider.Settings;

                // Update RAG settings
                appSettings.Rag.ChunkSize = _chunkSize;
                appSettings.Rag.ChunkOverlap = _chunkOverlap;
                appSettings.Rag.MaxRetrievedChunks = _maxRetrievedChunks;
                appSettings.Rag.MinSimilarityScore = _minSimilarityScore;
                appSettings.Rag.EmbeddingBatchSize = _embeddingBatchSize;

                // Add any new properties to the RagSettings class if needed

                // Save to disk
                _configProvider.SaveConfiguration();

                _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                    "Configuration saved successfully");

                await Task.CompletedTask; // Just to make it async
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagConfigurationService",
                    $"Error saving configuration: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("SaveRagConfiguration");
            }
        }

        /// <summary>
        /// Resets all configuration to default values
        /// </summary>
        public async Task ResetToDefaultsAsync()
        {
            ChunkSize = 400;
            ChunkOverlap = 50;
            MaxRetrievedChunks = 4;
            MinSimilarityScore = 0.3f;
            UseSemanticChunking = false;
            EmbeddingModelDimension = 384;
            EmbeddingBatchSize = 15; // Reset to default

            await SaveConfigurationAsync();

            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Reset configuration to defaults");
        }

        /// <summary>
        /// Applies a new configuration from RagSettings
        /// </summary>
        /// <param name="settings">New settings to apply</param>
        public async Task ApplyConfigurationAsync(RagSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            ChunkSize = settings.ChunkSize;
            ChunkOverlap = settings.ChunkOverlap;
            MaxRetrievedChunks = settings.MaxRetrievedChunks;
            MinSimilarityScore = settings.MinSimilarityScore;
            EmbeddingBatchSize = settings.EmbeddingBatchSize;

            await SaveConfigurationAsync();

            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Applied new configuration from settings");
        }

        // Property change notification methods remain unchanged...
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;

            storage = value;
            OnPropertyChanged(propertyName);

            _diagnostics.Log(DiagnosticLevel.Debug, "RagConfigurationService",
                $"Configuration parameter {propertyName} changed to {value}");

            return true;
        }
    }
}