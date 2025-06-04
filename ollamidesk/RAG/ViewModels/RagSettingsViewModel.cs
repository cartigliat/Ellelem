using System;
using System.Windows;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.ViewModels
{
    public class RagSettingsViewModel : ViewModelBase
    {
        private readonly IRagConfigurationService _configService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly Window _window;

        // Properties
        private int _chunkSize;
        private int _chunkOverlap;
        private int _maxRetrievedChunks;
        private float _minSimilarityScore;
        private bool _useSemanticChunking;

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

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ResetCommand { get; }

        public RagSettingsViewModel(
            IRagConfigurationService configService,
            RagDiagnosticsService diagnostics,
            Window window)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _window = window ?? throw new ArgumentNullException(nameof(window));

            // Initialize properties from service
            ChunkSize = _configService.ChunkSize;
            ChunkOverlap = _configService.ChunkOverlap;
            MaxRetrievedChunks = _configService.MaxRetrievedChunks;
            MinSimilarityScore = _configService.MinSimilarityScore;
            UseSemanticChunking = _configService.UseSemanticChunking;

            // Commands
            SaveCommand = new RelayCommand(_ => SaveAndClose());
            CancelCommand = new RelayCommand(_ => _window.Close());
            ResetCommand = new RelayCommand(_ => ResetToDefaults());

            _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                "Settings view model initialized");
        }

        private async void SaveAndClose()
        {
            try
            {
                // Apply changes to the service
                _configService.ChunkSize = ChunkSize;
                _configService.ChunkOverlap = ChunkOverlap;
                _configService.MaxRetrievedChunks = MaxRetrievedChunks;
                _configService.MinSimilarityScore = MinSimilarityScore;
                _configService.UseSemanticChunking = UseSemanticChunking;

                // Save configuration
                await _configService.SaveConfigurationAsync();

                _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                    "Settings saved successfully");

                // Close window
                _window.DialogResult = true;
                _window.Close();
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagSettingsViewModel",
                    $"Error saving settings: {ex.Message}");
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResetToDefaults()
        {
            if (MessageBox.Show("Are you sure you want to reset all settings to defaults?",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _configService.ResetToDefaultsAsync();

                // Update view properties
                ChunkSize = _configService.ChunkSize;
                ChunkOverlap = _configService.ChunkOverlap;
                MaxRetrievedChunks = _configService.MaxRetrievedChunks;
                MinSimilarityScore = _configService.MinSimilarityScore;
                UseSemanticChunking = _configService.UseSemanticChunking;

                _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                    "Settings reset to defaults");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagSettingsViewModel",
                    $"Error resetting settings: {ex.Message}");
                MessageBox.Show($"Error resetting settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}