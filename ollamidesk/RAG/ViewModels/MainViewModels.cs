using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.Configuration;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.ViewModels;

namespace ollamidesk.RAG.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private IOllamaModel _modelService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly OllamaSettings _ollamaSettings;
        private readonly IDiagnosticsUIService _diagnosticsUIService;
        private readonly IRetrievalService _retrievalService;
        private readonly IPromptEngineeringService _promptEngineeringService;
        private string _userInput = string.Empty;
        private string _modelName = string.Empty;
        private bool _isBusy;

        // Dictionary to store chat histories for different models
        private readonly Dictionary<string, ObservableCollection<ChatMessage>> _modelChatHistories =
            new Dictionary<string, ObservableCollection<ChatMessage>>();

        // Lock object for dictionary access synchronization
        private readonly object _chatHistoriesLock = new object();

        // Maximum number of messages to keep in history
        private const int MaxChatHistoryMessages = 50;

        public string UserInput
        {
            get => _userInput;
            set => SetProperty(ref _userInput, value);
        }

        public string ModelName
        {
            get => _modelName;
            set
            {
                if (SetProperty(ref _modelName, value))
                {
                    // When model name changes, update chat history
                    LoadChatHistoryForModel(value);
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new ObservableCollection<ChatMessage>();
        public DocumentViewModel DocumentViewModel { get; }
        public IRagConfigurationService ConfigurationService { get; }

        // Commands
        public ICommand SendMessageCommand { get; }
        public ICommand DiagnosticsCommand { get; }

        public MainViewModel(
            IOllamaModel modelService,
            DocumentViewModel documentViewModel,
            OllamaSettings ollamaSettings,
            RagDiagnosticsService diagnostics,
            IDiagnosticsUIService diagnosticsUIService,
            IRetrievalService retrievalService,
            IPromptEngineeringService promptEngineeringService,
            IRagConfigurationService configurationService)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            DocumentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
            _ollamaSettings = ollamaSettings ?? throw new ArgumentNullException(nameof(ollamaSettings));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnosticsUIService = diagnosticsUIService ?? throw new ArgumentNullException(nameof(diagnosticsUIService));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _promptEngineeringService = promptEngineeringService ?? throw new ArgumentNullException(nameof(promptEngineeringService));
            ConfigurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));

            // Use default model name from settings
            ModelName = _ollamaSettings.DefaultModel;

            // Initialize commands
            SendMessageCommand = new RelayCommand(
                async _ => await SendMessageAsync(),
                _ => !string.IsNullOrWhiteSpace(UserInput) && !IsBusy);

            // Get diagnostics command from service
            DiagnosticsCommand = _diagnosticsUIService.GetShowDiagnosticsCommand();

            // Initialize the chat history for the default model in a thread-safe way
            if (!string.IsNullOrEmpty(ModelName))
            {
                lock (_chatHistoriesLock)
                {
                    if (!_modelChatHistories.ContainsKey(ModelName))
                    {
                        _modelChatHistories[ModelName] = new ObservableCollection<ChatMessage>();
                    }
                }
            }

            _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel", "ViewModel initialized");
        }

        /// <summary>
        /// Updates the model service and refreshes the UI accordingly
        /// </summary>
        public void UpdateModelService(IOllamaModel newModelService, string modelName)
        {
            if (newModelService == null)
                throw new ArgumentNullException(nameof(newModelService));

            // Save current chat history before switching
            if (!string.IsNullOrEmpty(ModelName))
            {
                SaveChatHistoryForModel(ModelName);
            }

            _modelService = newModelService;
            ModelName = modelName; // This will trigger LoadChatHistoryForModel via property setter

            _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel",
                $"Model service updated to {modelName}");
        }

        /// <summary>
        /// Saves the current chat history for a specific model
        /// </summary>
        public void SaveChatHistoryForModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            // Thread-safe dictionary update
            lock (_chatHistoriesLock)
            {
                // Create a new collection with the current chat history
                _modelChatHistories[modelName] = new ObservableCollection<ChatMessage>(ChatHistory);
            }

            _diagnostics.Log(DiagnosticLevel.Debug, "MainViewModel",
                $"Saved chat history for model {modelName} ({ChatHistory.Count} messages)");
        }

        /// <summary>
        /// Loads chat history for a specific model
        /// </summary>
        public void LoadChatHistoryForModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            ObservableCollection<ChatMessage> historyToLoad;

            // Thread-safe dictionary access
            lock (_chatHistoriesLock)
            {
                // Initialize history for this model if it doesn't exist
                if (!_modelChatHistories.ContainsKey(modelName))
                {
                    _modelChatHistories[modelName] = new ObservableCollection<ChatMessage>();
                    _diagnostics.Log(DiagnosticLevel.Debug, "MainViewModel",
                        $"Created new chat history for model {modelName}");
                }

                // Get a local copy to avoid holding the lock while updating the UI
                historyToLoad = new ObservableCollection<ChatMessage>(_modelChatHistories[modelName]);
            }

            // Apply updates on the UI thread
            CollectionHelper.ReplaceSafely(ChatHistory, historyToLoad);

            _diagnostics.Log(DiagnosticLevel.Debug, "MainViewModel",
                $"Loaded chat history for model {modelName} ({historyToLoad.Count} messages)");
        }

        /// <summary>
        /// Adds a new chat message to the current chat history
        /// </summary>
        public void AddChatMessage(ChatMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            // Manage maximum history size on the UI thread
            if (ChatHistory.Count >= MaxChatHistoryMessages)
            {
                CollectionHelper.RemoveAtSafely(ChatHistory, 0);
            }

            CollectionHelper.AddSafely(ChatHistory, message);

            // Update the model-specific history in our dictionary
            if (!string.IsNullOrEmpty(ModelName))
            {
                // Thread-safe dictionary update
                lock (_chatHistoriesLock)
                {
                    if (!_modelChatHistories.ContainsKey(ModelName))
                    {
                        _modelChatHistories[ModelName] = new ObservableCollection<ChatMessage>();
                    }

                    // Make a copy of the current UI-bound collection
                    _modelChatHistories[ModelName] = new ObservableCollection<ChatMessage>(ChatHistory);
                }
            }

            _diagnostics.Log(DiagnosticLevel.Debug, "MainViewModel",
                "Added new chat message to history");
        }

        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput) || IsBusy)
                return;

            string userQuery = UserInput;
            UserInput = string.Empty;

            try
            {
                IsBusy = true;
                _diagnostics.StartOperation("SendMessage");
                _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel",
                    $"Processing message: \"{userQuery.Substring(0, Math.Min(50, userQuery.Length))}\"");

                // Get RAG context if enabled
                List<DocumentChunk> sources = new List<DocumentChunk>();
                string prompt = userQuery;
                bool usedRag = false;

                if (DocumentViewModel.IsRagEnabled)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel", "Using RAG for this message");

                    // Get selected document IDs
                    var selectedDocs = DocumentViewModel.Documents
                        .Where(d => d.IsSelected && d.IsProcessed)
                        .Select(d => d.Id)
                        .ToList();

                    if (selectedDocs.Count > 0)
                    {
                        // Retrieve relevant chunks using the RetrievalService
                        var searchResults = await _retrievalService.RetrieveRelevantChunksAsync(userQuery, selectedDocs)
                            .ConfigureAwait(false);

                        // Extract chunks from search results
                        var relevantChunks = searchResults.Select(result => result.Chunk).ToList();

                        if (relevantChunks.Count > 0)
                        {
                            // Create augmented prompt using the PromptEngineeringService
                            prompt = await _promptEngineeringService.CreateAugmentedPromptAsync(userQuery, relevantChunks)
                                .ConfigureAwait(false);
                            sources = relevantChunks;
                            usedRag = true;

                            _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel",
                                $"Generated augmented prompt with {relevantChunks.Count} sources");
                        }
                        else
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "MainViewModel",
                                "No relevant chunks found for the query");
                        }
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "MainViewModel",
                            "No processed documents selected for RAG");
                    }
                }

                // MODIFICATION START: Removed chat history context retrieval
                // // Get chat history for context
                // List<string> historyContext = ChatHistory
                //     .Take(10) // Limit to last 10 messages for context
                //     .Select(m => $"User: {m.UserQuery}\nAssistant: {m.ModelResponse}")
                //     .ToList();
                // MODIFICATION END

                // Generate model response
                string modelResponse;

                if (usedRag && sources.Count > 0)
                {
                    // MODIFICATION START: Pass empty list for historyContext
                    modelResponse = await _modelService.GenerateResponseWithContextAsync(
                        userQuery,
                        new List<string>(), // Pass empty list instead of historyContext
                        sources
                    ).ConfigureAwait(false);
                    // MODIFICATION END
                }
                else
                {
                    // MODIFICATION START: Pass empty list for historyContext
                    modelResponse = await _modelService.GenerateResponseAsync(
                        prompt,
                        string.Empty, // No loaded document, using RAG instead
                        new List<string>() // Pass empty list instead of historyContext
                    ).ConfigureAwait(false);
                    // MODIFICATION END
                }

                // Create chat message
                var message = new ChatMessage
                {
                    UserQuery = userQuery,
                    ModelResponse = modelResponse,
                    UsedRag = usedRag,
                    SourceChunkIds = sources.Select(s => s.Id).ToList()
                };

                // Add message to chat history (keeps UI updated)
                AddChatMessage(message);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "MainViewModel",
                    $"Error sending message: {ex.Message}");

                // Use properly awaited async call by capturing the task or using discard
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsBusy = false;
                _diagnostics.EndOperation("SendMessage");
            }
        }

        /// <summary>
        /// Clears the chat history for all models to free up memory
        /// </summary>
        public void ClearAllChatHistory()
        {
            CollectionHelper.ClearSafely(ChatHistory);

            // Thread-safe dictionary clearing
            lock (_chatHistoriesLock)
            {
                _modelChatHistories.Clear();

                if (!string.IsNullOrEmpty(ModelName))
                {
                    _modelChatHistories[ModelName] = new ObservableCollection<ChatMessage>();
                }
            }

            _diagnostics.Log(DiagnosticLevel.Info, "MainViewModel", "Cleared all chat history");
        }
    }
}