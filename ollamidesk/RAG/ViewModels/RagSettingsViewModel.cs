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
        private float _temperature;
        private float _topP;

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

        /// <summary>
        /// Controls randomness in model responses. Range: 0.0 (deterministic) to 2.0 (very creative)
        /// </summary>
        public float Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, Math.Max(0.0f, Math.Min(2.0f, value))); // Clamp to valid range
        }

        /// <summary>
        /// Nucleus sampling parameter. Range: 0.0 to 1.0
        /// </summary>
        public float TopP
        {
            get => _topP;
            set => SetProperty(ref _topP, Math.Max(0.0f, Math.Min(1.0f, value))); // Clamp to valid range
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
            Temperature = _configService.Temperature;
            TopP = _configService.TopP;

            // Commands
            SaveCommand = new RelayCommand(_ => SaveAndClose());
            CancelCommand = new RelayCommand(_ => _window.Close());
            ResetCommand = new RelayCommand(_ => ResetToDefaults());

            _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                $"Settings view model initialized with Temperature={Temperature}, TopP={TopP}");
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
                _configService.Temperature = Temperature;
                _configService.TopP = TopP;

                // Save configuration
                await _configService.SaveConfigurationAsync();

                _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                    $"Settings saved successfully with Temperature={Temperature}, TopP={TopP}");

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
                Temperature = _configService.Temperature;
                TopP = _configService.TopP;

                _diagnostics.Log(DiagnosticLevel.Info, "RagSettingsViewModel",
                    $"Settings reset to defaults including Temperature={Temperature}, TopP={TopP}");
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