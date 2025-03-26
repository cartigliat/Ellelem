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
    /// Central service for managing all RAG configuration parameters
    /// </summary>
    public interface IRagConfigurationService : INotifyPropertyChanged
    {
        // Core chunking parameters
        int ChunkSize { get; set; }
        int ChunkOverlap { get; set; }

        // Retrieval parameters
        int MaxRetrievedChunks { get; set; }
        float MinSimilarityScore { get; set; }

        // Additional parameters you might want to add
        bool UseSemanticChunking { get; set; }
        int EmbeddingModelDimension { get; set; }

        // Methods
        Task SaveConfigurationAsync();
        Task ResetToDefaultsAsync();
    }

    public class RagConfigurationService : IRagConfigurationService
    {
        private readonly RagDiagnosticsService _diagnostics;
        private readonly ConfigurationProvider _configProvider;

        private int _chunkSize;
        private int _chunkOverlap;
        private int _maxRetrievedChunks;
        private float _minSimilarityScore;
        private bool _useSemanticChunking;
        private int _embeddingModelDimension;

        // Modified: Changed to nullable event handler
        public event PropertyChangedEventHandler? PropertyChanged;

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

        public async Task ResetToDefaultsAsync()
        {
            ChunkSize = 400;
            ChunkOverlap = 50;
            MaxRetrievedChunks = 4;
            MinSimilarityScore = 0.3f;
            UseSemanticChunking = false;
            EmbeddingModelDimension = 384;

            await SaveConfigurationAsync();

            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Reset configuration to defaults");
        }

        // Modified: Updated to handle nullable propertyName
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }

        // Modified: Updated to handle nullable propertyName
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