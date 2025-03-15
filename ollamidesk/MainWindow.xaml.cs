using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.ViewModels;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel = null!; // Use null! to suppress warning, will be initialized in InitializeServices
        private Dictionary<string, ObservableCollection<ChatMessage>> chatHistories =
            new Dictionary<string, ObservableCollection<ChatMessage>>();
        private string? loadedDocument;
        private IOllamaModel? selectedModel;
        private MainWindowRagHelper _ragHelper = null!; // Will be initialized in constructor

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();

            // Enable RAG diagnostics
            _ragHelper = this.EnableRagDiagnostics();
        }

        private void InitializeServices()
        {
            // Create repositories and services
            var documentRepository = new FileSystemDocumentRepository();
            var embeddingService = new OllamaEmbeddingService("nomic-embed-text");
            var vectorStore = new InMemoryVectorStore();

            // Create RAG service
            var ragService = new RagService(
                documentRepository,
                embeddingService,
                vectorStore,
                chunkSize: 500,
                chunkOverlap: 100,
                maxRetrievedChunks: 5
            );

            // Create document view model
            var documentViewModel = new DocumentViewModel(ragService);

            // Create main view model
            var initialModel = OllamaModelLoader.LoadModel("llama2"); // Default model
            _viewModel = new MainViewModel(initialModel, documentViewModel)
            {
                ModelName = "llama2" // Default model name
            };
            selectedModel = initialModel;

            // Initialize chat history for default model
            chatHistories["llama2"] = new ObservableCollection<ChatMessage>();

            // Set data context
            DataContext = _viewModel;

            // Make sure the ChatHistoryItemsControl is bound to the ViewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;

            // Log initialization
            RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                "Application initialized with RAG services");
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SideMenuWindow sideMenuWindow = new SideMenuWindow();

            if (sideMenuWindow.ShowDialog() == true)
            {
                // Update the selected model in the main window
                if (!string.IsNullOrEmpty(sideMenuWindow.SelectedModel))
                {
                    ModelNameTextBlock.Text = sideMenuWindow.SelectedModel;
                    _viewModel.ModelName = sideMenuWindow.SelectedModel;

                    // Load the selected model
                    selectedModel = OllamaModelLoader.LoadModel(sideMenuWindow.SelectedModel);
                    RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                        $"Model changed to: {sideMenuWindow.SelectedModel}");

                    // Update the ViewModel's model service
                    _viewModel = new MainViewModel(selectedModel, _viewModel.DocumentViewModel)
                    {
                        ModelName = sideMenuWindow.SelectedModel
                    };
                    DataContext = _viewModel;

                    // Update chat history binding
                    UpdateChatHistoryBinding(sideMenuWindow.SelectedModel);
                }

                // Update the loaded document
                if (!string.IsNullOrEmpty(sideMenuWindow.LoadedDocument))
                {
                    loadedDocument = sideMenuWindow.LoadedDocument;
                    RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                        $"Document loaded with length: {loadedDocument.Length} chars");
                }
            }
        }

        private async void SendMessage()
        {
            if (selectedModel == null)
            {
                MessageBox.Show("Please select a model first.", "No Model Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string userInput = string.Empty;
            await Dispatcher.InvokeAsync(() =>
            {
                userInput = UserInputTextBox.Text.Trim();
            });

            if (string.IsNullOrEmpty(userInput))
            {
                return;
            }

            string selectedModelName = string.Empty;
            await Dispatcher.InvokeAsync(() =>
            {
                selectedModelName = ModelNameTextBlock.Text;

                // Disable input and show loading indicator
                UserInputTextBox.IsEnabled = false;
                SendButton.IsEnabled = false;
                LoadingIndicator.Visibility = Visibility.Visible;

                // IMPORTANT: Clear the input box right away for better UX
                UserInputTextBox.Clear();
            });

            try
            {
                RagDiagnostics.Instance.StartOperation("ProcessUserMessage");
                RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                    $"Processing user message: \"{userInput.Substring(0, Math.Min(50, userInput.Length))}\"");

                // Get RAG context if enabled
                string prompt = userInput;
                List<DocumentChunk> sources = new List<DocumentChunk>();
                bool usedRag = false;

                if (_viewModel.DocumentViewModel.IsRagEnabled)
                {
                    var selectedDocIds = _viewModel.DocumentViewModel.Documents
                        .Where(d => d.IsSelected && d.IsProcessed)
                        .Select(d => d.Id)
                        .ToList();

                    RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                        $"RAG enabled with {selectedDocIds.Count} selected documents");

                    if (selectedDocIds.Count > 0)
                    {
                        RagDiagnostics.Instance.StartOperation("GenerateAugmentedPrompt");
                        var ragService = (_viewModel.DocumentViewModel as DocumentViewModel).GetRagService();
                        var (augmentedPrompt, retrievedChunks) = await ragService.GenerateAugmentedPromptAsync(
                            userInput, selectedDocIds);
                        RagDiagnostics.Instance.EndOperation("GenerateAugmentedPrompt");

                        prompt = augmentedPrompt;
                        sources = retrievedChunks;
                        usedRag = true;

                        RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                            $"Generated augmented prompt with {retrievedChunks.Count} chunks");
                    }
                }

                // Initialize chat history for this model if not exists
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!chatHistories.ContainsKey(selectedModelName))
                    {
                        chatHistories[selectedModelName] = new ObservableCollection<ChatMessage>();
                    }

                    // CRITICAL FIX: Make sure ChatHistoryItemsControl is using the right collection
                    ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;
                });

                // Get current chat history for context
                var currentChatHistory = await Dispatcher.InvokeAsync(() =>
                {
                    return chatHistories[selectedModelName]
                        .Select(cm => $"User: {cm.UserQuery}\nModel: {cm.ModelResponse}")
                        .ToList();
                });

                // Generate model response
                RagDiagnostics.Instance.StartOperation("GenerateModelResponse");
                string modelResponse;
                if (usedRag && sources != null && sources.Count > 0)
                {
                    RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                        "Generating response with RAG context");
                    modelResponse = await selectedModel.GenerateResponseWithContextAsync(
                        userInput,
                        currentChatHistory,
                        sources
                    );
                }
                else
                {
                    RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "MainWindow",
                        "Generating response without RAG context");
                    modelResponse = await selectedModel.GenerateResponseAsync(
                        userInput,
                        loadedDocument ?? string.Empty,
                        currentChatHistory
                    );
                }
                RagDiagnostics.Instance.EndOperation("GenerateModelResponse");

                // Create and add chat message
                var chatMessage = new ChatMessage
                {
                    UserQuery = userInput,
                    ModelResponse = modelResponse,
                    UsedRag = usedRag,
                    SourceChunkIds = sources?.Select(s => s.Id).ToList() ?? new List<string>()
                };

                // Add to ViewModel
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_viewModel.ChatHistory.Count >= 50)
                    {
                        _viewModel.ChatHistory.RemoveAt(0);
                    }
                    _viewModel.ChatHistory.Add(chatMessage);

                    // Update ChatHistoryItemsControl if needed
                    if (ChatHistoryItemsControl.ItemsSource != _viewModel.ChatHistory)
                    {
                        ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;
                    }
                });

                // Add the chat message to the appropriate model's chat history
                await Dispatcher.InvokeAsync(() =>
                {
                    // Manage chat history size (keep last 50 messages)
                    var history = chatHistories[selectedModelName];
                    if (history.Count >= 50)
                    {
                        history.RemoveAt(0);
                    }
                    history.Add(chatMessage);

                    // Scroll to bottom
                    ChatHistoryScrollViewer.ScrollToBottom();
                });
            }
            catch (Exception ex)
            {
                RagDiagnostics.Instance.Log(DiagnosticLevel.Error, "MainWindow",
                    $"Error processing message: {ex.Message}");

                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                RagDiagnostics.Instance.EndOperation("ProcessUserMessage");

                // Re-enable input and hide loading indicator
                await Dispatcher.InvokeAsync(() =>
                {
                    UserInputTextBox.IsEnabled = true;
                    SendButton.IsEnabled = true;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    UserInputTextBox.Focus();
                });
            }
        }

        private void UpdateChatHistoryBinding(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            // Ensure chat history exists for this model
            if (!chatHistories.ContainsKey(modelName))
            {
                chatHistories[modelName] = new ObservableCollection<ChatMessage>();
            }

            // Update the viewModel's ChatHistory to point to this model's history
            _viewModel.ChatHistory.Clear();
            foreach (var message in chatHistories[modelName])
            {
                _viewModel.ChatHistory.Add(message);
            }

            // Make sure the UI is bound to the viewModel's ChatHistory
            ChatHistoryItemsControl.ItemsSource = _viewModel.ChatHistory;
        }

        private void DiagnoseUiBindings()
        {
            // Log the current state of our collections
            RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "Diagnostics",
                $"ViewModel.ChatHistory count: {_viewModel.ChatHistory.Count}");

            foreach (var entry in chatHistories)
            {
                RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "Diagnostics",
                    $"Model '{entry.Key}' chat history count: {entry.Value.Count}");
            }

            // Check what ChatHistoryItemsControl is bound to
            RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "Diagnostics",
                $"ChatHistoryItemsControl.ItemsSource type: {ChatHistoryItemsControl.ItemsSource?.GetType().Name ?? "null"}");

            // Get the actual count of items in the ItemsControl
            RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "Diagnostics",
                $"ChatHistoryItemsControl items count: {ChatHistoryItemsControl.Items.Count}");

            // Check the DataContext
            RagDiagnostics.Instance.Log(DiagnosticLevel.Info, "Diagnostics",
                $"Window DataContext type: {DataContext?.GetType().Name ?? "null"}");
        }

        private async void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Allow sending message with Shift+Enter (new line) or Ctrl+Enter
            if (e.Key == Key.Enter)
            {
                // Check if Shift or Ctrl is pressed
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ||
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Insert new line
                    int caretIndex = UserInputTextBox.SelectionStart;
                    UserInputTextBox.Text = UserInputTextBox.Text.Insert(caretIndex, Environment.NewLine);
                    UserInputTextBox.SelectionStart = caretIndex + Environment.NewLine.Length;
                }
                else
                {
                    // Send message
                    e.Handled = true;
                    if (_viewModel != null)
                    {
                        await _viewModel.SendMessageAsync();
                    }
                    else
                    {
                        await Task.Run(SendMessage);
                    }
                }
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.SendMessageAsync();
            }
            else
            {
                await Task.Run(SendMessage);
            }
        }

        // Add cleanup for resources when closing
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Clean up RAG diagnostics
            _ragHelper?.Cleanup();
        }
    }

    // Add the extension method to get RagService from DocumentViewModel
    public static class ViewModelExtensions
    {
        public static RagService GetRagService(this DocumentViewModel viewModel)
        {
            // Use reflection to get the private _ragService field
            var fieldInfo = typeof(DocumentViewModel).GetField("_ragService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (fieldInfo == null)
            {
                throw new InvalidOperationException("Could not find _ragService field in DocumentViewModel");
            }

            var service = fieldInfo.GetValue(viewModel) as RagService;
            if (service == null)
            {
                throw new InvalidOperationException("Failed to get RagService from DocumentViewModel");
            }

            return service;
        }
    }
}