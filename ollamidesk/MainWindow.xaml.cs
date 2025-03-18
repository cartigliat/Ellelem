using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Animation;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.ViewModels;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Common.MVVM;
using ollamidesk.Transition;
using ollamidesk.DependencyInjection;
using ollamidesk.Services;

namespace ollamidesk
{
    public partial class MainWindow : Window
    {
        // Remove readonly modifiers from fields that need assignment after construction
        private MainViewModel _viewModel;
        private string? _loadedDocument;
        private IOllamaModel _ollamaModel;
        private MainWindowRagHelper _ragHelper;
        private RagDiagnosticsService _diagnostics;

        // Legacy constructor for backward compatibility
        public MainWindow()
        {
            // Initialize fields with default values to avoid warnings
            _viewModel = null!;
            _ollamaModel = null!;
            _diagnostics = null!;
            _ragHelper = null!;

            try
            {
                // If ServiceProviderFactory is initialized, use it
                if (ServiceProviderFactory.IsInitialized)
                {
                    // Get services from DI container
                    _viewModel = ServiceProviderFactory.GetService<MainViewModel>();
                    _ollamaModel = ServiceProviderFactory.GetService<IOllamaModel>();
                    _diagnostics = ServiceProviderFactory.GetService<RagDiagnosticsService>();

                    InitializeComponent();
                    InitializeWithDI();
                }
                else
                {
                    // Use legacy initialization
                    InitializeComponent();
                    InitializeServices();
                }
            }
            catch (Exception)
            {
                // Fallback to legacy initialization
                InitializeComponent();
                InitializeServices();
            }
        }

        // New constructor with DI
        public MainWindow(
            MainViewModel viewModel,
            IOllamaModel ollamaModel,
            RagDiagnosticsService diagnostics)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _ollamaModel = ollamaModel ?? throw new ArgumentNullException(nameof(ollamaModel));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _ragHelper = new MainWindowRagHelper(this, _diagnostics);
            InitializeWithDI();
        }

        private void InitializeWithDI()
        {
            // Set data context
            DataContext = _viewModel;

            // Setup initial RAG panel state
            UpdateRagPanelVisibility(_viewModel.DocumentViewModel.IsRagEnabled);

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Log initialization
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");
        }

        private void InitializeServices()
        {
            // Create diagnostics service
            _diagnostics = LegacySupport.CreateDiagnosticsService();

            // Create repositories and services with settings
            var storageSettings = LegacySupport.CreateStorageSettings();
            var ollamaSettings = LegacySupport.CreateOllamaSettings();
            var ragSettings = LegacySupport.CreateRagSettings(
                chunkSize: 500,
                chunkOverlap: 100,
                maxRetrievedChunks: 5,
                minSimilarityScore: 0.1f);

            var documentRepository = new FileSystemDocumentRepository(storageSettings, _diagnostics);
            var embeddingService = new OllamaEmbeddingService(ollamaSettings, _diagnostics);
            var vectorStore = new FileSystemVectorStore(storageSettings, _diagnostics);

            // Create RAG service
            var ragService = new RagService(
                documentRepository,
                embeddingService,
                vectorStore,
                ragSettings,
                _diagnostics
            );

            // Create document view model
            var documentViewModel = new DocumentViewModel(ragService, _diagnostics);

            // Create main view model
            var initialModel = OllamaModelLoader.LoadModel("llama2"); // Default model
            _viewModel = new MainViewModel(initialModel, documentViewModel,
                ollamaSettings, _diagnostics)
            {
                ModelName = "llama2" // Default model name
            };
            _ollamaModel = initialModel;

            // Set data context
            DataContext = _viewModel;

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Enable RAG diagnostics
            _ragHelper = this.EnableRagDiagnostics();

            // Setup initial RAG panel state
            UpdateRagPanelVisibility(_viewModel.DocumentViewModel.IsRagEnabled);

            // Log initialization
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a side menu window without requiring the model factory
            SideMenuWindow sideMenuWindow;

            try
            {
                // Try to create with DI
                if (ServiceProviderFactory.IsInitialized)
                {
                    sideMenuWindow = ServiceProviderFactory.GetService<SideMenuWindow>();
                }
                else
                {
                    sideMenuWindow = new SideMenuWindow();
                }
            }
            catch
            {
                // Fallback to direct instantiation
                sideMenuWindow = new SideMenuWindow();
            }

            if (sideMenuWindow.ShowDialog() == true)
            {
                // Update the selected model in the main window
                if (!string.IsNullOrEmpty(sideMenuWindow.SelectedModel))
                {
                    string previousModelName = _viewModel.ModelName;
                    string newModelName = sideMenuWindow.SelectedModel;

                    // Only reload the model if it's different from the current one
                    if (previousModelName != newModelName)
                    {
                        ModelNameTextBlock.Text = newModelName;

                        // Load the selected model
                        IOllamaModel selectedModel;

                        try
                        {
                            // Try to use DI factory
                            if (ServiceProviderFactory.IsInitialized)
                            {
                                var factory = ServiceProviderFactory.GetService<OllamaModelFactory>();
                                selectedModel = factory.CreateModel(newModelName);
                            }
                            else
                            {
                                selectedModel = OllamaModelLoader.LoadModel(newModelName);
                            }
                        }
                        catch
                        {
                            // Fallback to legacy loader
                            selectedModel = OllamaModelLoader.LoadModel(newModelName);
                        }

                        _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                            $"Model changed to: {newModelName}");

                        // Update the ViewModel with the new model
                        _viewModel.UpdateModelService(selectedModel, newModelName);
                    }
                }

                // Update the loaded document
                if (!string.IsNullOrEmpty(sideMenuWindow.LoadedDocument))
                {
                    _loadedDocument = sideMenuWindow.LoadedDocument;
                    _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                        $"Document loaded with length: {sideMenuWindow.LoadedDocument.Length} chars");
                }
            }
        }

        private async void UserInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;

                // Send the message
                if (!string.IsNullOrWhiteSpace(UserInputTextBox.Text))
                {
                    if (_viewModel != null)
                    {
                        // Use the ViewModel's implementation for message handling
                        await _viewModel.SendMessageAsync();

                        // Scroll to bottom after message is sent
                        ChatHistoryScrollViewer.ScrollToBottom();
                    }
                }
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Allow Shift+Enter for new line
                // Don't mark as handled so the TextBox processes it normally
            }
        }

        // Add cleanup for resources when closing
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Clean up RAG diagnostics
            _ragHelper?.Cleanup();
        }

        // RAG panel visibility handlers
        private void RagEnableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Show the RAG panel
            RagPanel.Visibility = Visibility.Visible;

            // Log that RAG was enabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG enabled by user");
        }

        private void RagEnableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Hide the RAG panel
            RagPanel.Visibility = Visibility.Collapsed;

            // Log that RAG was disabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG disabled by user");
        }

        private void UpdateRagPanelVisibility(bool isVisible)
        {
            if (RagPanel == null) return;

            // Simple visibility toggle without animation
            RagPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}