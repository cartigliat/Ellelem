// ollamidesk/MainWindow.xaml.cs - Enhanced with proper cleanup
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation; // Added for DoubleAnimation
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
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _isDisposing = false;

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

            // Create cancellation token source for operations
            _cancellationTokenSource = new CancellationTokenSource();

            // Set data context
            DataContext = _viewModel;

            // Setup diagnostics UI service with this window
            _diagnosticsUIService.SetupDiagnosticsUI(this);

            // Setup initial RAG panel state
            UpdateRagPanelVisibility(_viewModel.DocumentViewModel.IsRagEnabled);

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Log initialization
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");

            // Subscribe to window closing event for cleanup
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isDisposing)
            {
                _isDisposing = true;

                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Window closing - starting cleanup");

                // Cancel any ongoing operations
                _cancellationTokenSource.Cancel();

                // Allow a brief moment for operations to cancel gracefully
                try
                {
                    // Wait up to 2 seconds for ongoing operations to complete
                    Task.Delay(2000, CancellationToken.None).Wait();
                }
                catch
                {
                    // Ignore timeout exceptions during shutdown
                }

                // Clean up resources
                CleanupResources();

                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Window cleanup completed");
            }
        }

        private void CleanupResources()
        {
            try
            {
                // Clean up diagnostic handlers
                _diagnosticsUIService?.UnregisterHandlers();

                // Dispose of cancellation token source
                _cancellationTokenSource?.Dispose();

                _diagnostics.Log(DiagnosticLevel.Debug, "MainWindow", "MainWindow resources cleaned up");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "MainWindow", $"Error during MainWindow cleanup: {ex.Message}");
            }
        }

        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            // Close the application
            this.Close();
        }

        private async void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if we're disposing to avoid new operations during shutdown
            if (_isDisposing || _cancellationTokenSource.Token.IsCancellationRequested)
                return;

            // Disable button while processing to prevent multiple clicks
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
                if (!_isDisposing)
                {
                    MenuToggleButton.IsEnabled = true;
                }
            }
        }

        private async Task UpdateModelAsync(string newModelName)
        {
            // Check cancellation token before proceeding
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            string previousModelName = _viewModel.ModelName;

            // Only reload the model if it's different from the current one
            if (previousModelName != newModelName)
            {
                try
                {
                    // Update UI immediately
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
                catch (OperationCanceledException)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Model update cancelled during shutdown");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "MainWindow", $"Error updating model: {ex.Message}");
                }
            }
        }

        private async void UserInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Check if we're disposing to avoid new operations during shutdown
            if (_isDisposing || _cancellationTokenSource.Token.IsCancellationRequested)
                return;

            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;

                // Send the message
                if (!string.IsNullOrWhiteSpace(UserInputTextBox.Text))
                {
                    if (_viewModel != null)
                    {
                        try
                        {
                            // Use the ViewModel's implementation for message handling
                            await _viewModel.SendMessageAsync();

                            // Scroll to bottom after message is sent
                            ChatHistoryScrollViewer.ScrollToBottom();
                        }
                        catch (OperationCanceledException)
                        {
                            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Message sending cancelled during shutdown");
                        }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Error, "MainWindow", $"Error sending message: {ex.Message}");
                        }
                    }
                }
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Allow Shift+Enter for new line
                // Don't mark as handled so the TextBox processes it normally
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if we're disposing to avoid new operations during shutdown
            if (_isDisposing || _cancellationTokenSource.Token.IsCancellationRequested)
                return;

            // Send the message
            if (!string.IsNullOrWhiteSpace(UserInputTextBox.Text))
            {
                if (_viewModel != null)
                {
                    try
                    {
                        // Use the ViewModel's implementation for message handling
                        await _viewModel.SendMessageAsync();

                        // Scroll to bottom after message is sent
                        ChatHistoryScrollViewer.ScrollToBottom();
                    }
                    catch (OperationCanceledException)
                    {
                        _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Message sending cancelled during shutdown");
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "MainWindow", $"Error sending message from SendButton: {ex.Message}");
                        // Optionally, show a message to the user, though the ViewModel might handle this
                        // MessageBox.Show($"Error sending message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // Add cleanup for resources when closing
        protected override void OnClosed(EventArgs e)
        {
            if (!_isDisposing)
            {
                CleanupResources();
            }

            base.OnClosed(e);
        }

        // RAG panel visibility handlers
        private void RagEnableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Show the RAG panel
            UpdateRagPanelVisibility(true);

            // Log that RAG was enabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG enabled by user");
        }

        private void RagEnableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Hide the RAG panel
            UpdateRagPanelVisibility(false);

            // Log that RAG was disabled
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "RAG disabled by user");
        }

        // Helper method to update visibility
        private void UpdateRagPanelVisibility(bool isVisible)
        {
            if (RagPanel == null) return;

            double durationSeconds = 0.3; // Animation duration

            if (isVisible)
            {
                RagPanel.Visibility = Visibility.Visible;
                DoubleAnimation fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(durationSeconds));
                RagPanel.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);
            }
            else
            {
                DoubleAnimation fadeOutAnimation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(durationSeconds));
                fadeOutAnimation.Completed += (s, _) => RagPanel.Visibility = Visibility.Collapsed;
                RagPanel.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
            }
        }

        // Add these methods to MainWindow.xaml.cs
        private void SetConservativePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.ConfigurationService != null)
            {
                _viewModel.ConfigurationService.Temperature = 0.3f;
                _viewModel.ConfigurationService.TopP = 0.7f;
                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Applied Conservative preset (Temp: 0.3, TopP: 0.7)");
            }
        }

        private void SetBalancedPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.ConfigurationService != null)
            {
                _viewModel.ConfigurationService.Temperature = 0.7f;
                _viewModel.ConfigurationService.TopP = 0.9f;
                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Applied Balanced preset (Temp: 0.7, TopP: 0.9)");
            }
        }

        private void SetCreativePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.ConfigurationService != null)
            {
                _viewModel.ConfigurationService.Temperature = 1.2f;
                _viewModel.ConfigurationService.TopP = 0.95f;
                _diagnostics.Log(DiagnosticLevel.Info, "MainWindow", "Applied Creative preset (Temp: 1.2, TopP: 0.95)");
            }
        }
    }
}