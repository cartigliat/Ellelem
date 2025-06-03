// ollamidesk/RAG/Services/RagConfigurationService.cs
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
        private bool _useSemanticChunking; // Keep the private field
        private int _embeddingBatchSize;
        private int _embeddingModelDimension;

        // ADD THESE TWO LINES:
        private float _temperature; // ADD: New field for temperature
        private float _topP; // ADD: New field for top_p

        // Modified: Changed to nullable event handler
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Initializes a new instance of the RagConfigurationService class.
        /// </summary>
        /// <param name="initialSettings">Initial RAG settings (loaded from appsettings.json)</param>
        /// <param name="configProvider">Configuration provider</param>
        /// <param name="diagnostics">Diagnostics service</param>
        public RagConfigurationService(
            RagSettings initialSettings,
            ConfigurationProvider configProvider,
            RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));

            // Initialize from settings loaded from the file
            _chunkSize = initialSettings.ChunkSize;
            _chunkOverlap = initialSettings.ChunkOverlap;
            _maxRetrievedChunks = initialSettings.MaxRetrievedChunks;
            _minSimilarityScore = initialSettings.MinSimilarityScore;
            _embeddingBatchSize = initialSettings.EmbeddingBatchSize;
            _useSemanticChunking = initialSettings.UseSemanticChunking; // <-- Read from initialSettings

            // ADD THESE LINES: Initialize model generation parameters from Ollama settings
            var ollamaSettings = _configProvider.Settings.Ollama;
            _temperature = ollamaSettings.Temperature;
            _topP = ollamaSettings.TopP;

            // Default values for any new parameters NOT in RagSettings yet
            // _useSemanticChunking = false; // <-- REMOVED THIS LINE
            _embeddingModelDimension = 384; // Default for many embedding models

            // MODIFY THIS LINE: Add temperature and topP to the log
            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                $"Configuration service initialized with Temperature={_temperature}, TopP={_topP}");
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

        // ADD THESE TWO PROPERTIES:
        public float Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, value);
        }

        public float TopP
        {
            get => _topP;
            set => SetProperty(ref _topP, value);
        }

        /// <summary>
        /// Returns a copy of the current RAG settings
        /// </summary>
        /// <returns>Current RAG settings</returns>
        public RagSettings GetCurrentSettings()
        {
            // Note: This needs the RagSettings class to have the UseSemanticChunking property.
            return new RagSettings
            {
                ChunkSize = ChunkSize,
                ChunkOverlap = ChunkOverlap,
                MaxRetrievedChunks = MaxRetrievedChunks,
                MinSimilarityScore = MinSimilarityScore,
                EmbeddingBatchSize = EmbeddingBatchSize,
                UseSemanticChunking = UseSemanticChunking // <-- Include in returned object
                // Note: EmbeddingModelDimension not in RagSettings class yet
                // Note: Temperature and TopP are in OllamaSettings, not RagSettings
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

                // Update RAG settings in the AppSettings object before saving
                appSettings.Rag.ChunkSize = _chunkSize;
                appSettings.Rag.ChunkOverlap = _chunkOverlap;
                appSettings.Rag.MaxRetrievedChunks = _maxRetrievedChunks;
                appSettings.Rag.MinSimilarityScore = _minSimilarityScore;
                appSettings.Rag.EmbeddingBatchSize = _embeddingBatchSize;
                appSettings.Rag.UseSemanticChunking = _useSemanticChunking; // <-- Add this line to save the value

                // ADD THESE LINES: Update Ollama settings for model generation parameters
                appSettings.Ollama.Temperature = _temperature;
                appSettings.Ollama.TopP = _topP;

                // Save the entire AppSettings object to disk
                _configProvider.SaveConfiguration();

                // MODIFY THIS LINE: Add temperature and topP to the log
                _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                    $"Configuration saved successfully with Temperature={_temperature}, TopP={_topP}");

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
            // Set properties to desired defaults
            ChunkSize = 400;
            ChunkOverlap = 50;
            MaxRetrievedChunks = 4;
            MinSimilarityScore = 0.3f;
            UseSemanticChunking = false; // <-- Set desired default here
            EmbeddingModelDimension = 384;
            EmbeddingBatchSize = 15; // Reset to default

            // ADD THESE LINES: Reset model generation parameters to defaults
            Temperature = 0.7f;
            TopP = 0.9f;

            await SaveConfigurationAsync(); // Save these defaults back

            // MODIFY THIS LINE: Add temperature and topP to the log
            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Reset configuration to defaults including Temperature=0.7, TopP=0.9");
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
            UseSemanticChunking = settings.UseSemanticChunking; // <-- Apply value from settings object

            // Note: Temperature and TopP are not part of RagSettings, they stay at current values
            // If you want to reset them too, you would need to add them to RagSettings or create a separate method

            await SaveConfigurationAsync();

            // MODIFY THIS LINE: Update the log message
            _diagnostics.Log(DiagnosticLevel.Info, "RagConfigurationService",
                "Applied new configuration from settings (Temperature and TopP unchanged)");
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