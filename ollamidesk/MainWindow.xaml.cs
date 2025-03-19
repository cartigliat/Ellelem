using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.ViewModels;
using ollamidesk.DependencyInjection;
using ollamidesk.Services;

namespace ollamidesk
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IOllamaModel _ollamaModel;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly MainWindowRagHelper _ragHelper;
        private string? _loadedDocument;

        // Constructor with DI
        public MainWindow(
            MainViewModel viewModel,
            IOllamaModel ollamaModel,
            RagDiagnosticsService diagnostics)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _ollamaModel = ollamaModel ?? throw new ArgumentNullException(nameof(ollamaModel));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Set data context
            DataContext = _viewModel;

            // Setup RAG helper
            _ragHelper = new MainWindowRagHelper(this, _diagnostics);

            // Setup initial RAG panel state
            UpdateRagPanelVisibility(_viewModel.DocumentViewModel.IsRagEnabled);

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Log initialization
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a side menu window through DI
            var sideMenuWindow = ServiceProviderFactory.GetService<SideMenuWindow>();

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

                        // Load the selected model using the factory
                        var factory = ServiceProviderFactory.GetService<OllamaModelFactory>();
                        var selectedModel = factory.CreateModel(newModelName);

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