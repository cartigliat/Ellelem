// ollamidesk/MainWindow.xaml.cs
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.Services.Interfaces;
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
        private readonly IDiagnosticsUIService _diagnosticsUIService;

        // Constructor with DI
        public MainWindow(
            MainViewModel viewModel,
            IOllamaModel ollamaModel,
            RagDiagnosticsService diagnostics,
            IDiagnosticsUIService diagnosticsUIService)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _ollamaModel = ollamaModel ?? throw new ArgumentNullException(nameof(ollamaModel));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnosticsUIService = diagnosticsUIService ?? throw new ArgumentNullException(nameof(diagnosticsUIService));

            // Set data context
            DataContext = _viewModel;

            // Setup diagnostics UI service with this window
            _diagnosticsUIService.SetupDiagnosticsUI(this);

            // Setup initial RAG panel state
            // Assumes RagPanel is the name of the panel in XAML
            UpdateRagPanelVisibility(_viewModel.DocumentViewModel.IsRagEnabled);

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            // Assumes ChatHistoryItemsControl is the name in XAML
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Log initialization
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");
        }

        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            // Close the application
            this.Close();
        }

        private async void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable button while processing to prevent multiple clicks
            // Assumes MenuToggleButton is the name in XAML
            MenuToggleButton.IsEnabled = false;
            try
            {
                // Create a side menu window through DI
                var sideMenuWindow = ServiceProviderFactory.GetService<SideMenuWindow>();

                // ShowDialog is inherently blocking - this is expected UI behavior for modal dialogs
                if (sideMenuWindow.ShowDialog() == true)
                {
                    // After the dialog closes, handle model updates asynchronously
                    if (!string.IsNullOrEmpty(sideMenuWindow.SelectedModel))
                    {
                        await UpdateModelAsync(sideMenuWindow.SelectedModel);
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "MainWindow",
                    $"Error processing dialog result: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MenuToggleButton.IsEnabled = true;
            }
        }

        private async Task UpdateModelAsync(string newModelName)
        {
            string previousModelName = _viewModel.ModelName;

            // Only reload the model if it's different from the current one
            if (previousModelName != newModelName)
            {
                // Update UI immediately
                // Assumes ModelNameTextBlock is the name in XAML
                ModelNameTextBlock.Text = newModelName;

                // Load the selected model using the factory
                var factory = ServiceProviderFactory.GetService<OllamaModelFactory>();
                var selectedModel = factory.CreateModel(newModelName);

                // Test connection in background if possible
                if (selectedModel is OllamaModel ollamaModel)
                {
                    await ollamaModel.TestConnectionAsync().ConfigureAwait(false);
                }

                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                    $"Model changed to: {newModelName}");

                // Update the ViewModel with the new model
                _viewModel.UpdateModelService(selectedModel, newModelName);
            }
        }

        private async void UserInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Assumes UserInputTextBox is the name in XAML
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
                        // Assumes ChatHistoryScrollViewer is the name in XAML
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

            // Clean up diagnostic handlers
            _diagnosticsUIService.UnregisterHandlers();
        }

        // RAG panel visibility handlers
        // Assumes RagEnableCheckBox is the name in XAML
        private void RagEnableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Show the RAG panel
            // Assumes RagPanel is the name in XAML
            UpdateRagPanelVisibility(true); // Use helper method

            // Log that RAG was enabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG enabled by user");
        }

        // Assumes RagEnableCheckBox is the name in XAML
        private void RagEnableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Hide the RAG panel
            // Assumes RagPanel is the name in XAML
            UpdateRagPanelVisibility(false); // Use helper method

            // Log that RAG was disabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG disabled by user");
        }

        // Helper method to update visibility
        // Assumes RagPanel is the name in XAML
        private void UpdateRagPanelVisibility(bool isVisible)
        {
            if (RagPanel == null) return;

            // Simple visibility toggle without animation
            RagPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        // No RagSettingsButton_Click handler is needed here for the MVVM approach,
        // as the button's Command property should be bound in XAML to the
        // OpenRagSettingsCommand in DocumentViewModel.
    }
}