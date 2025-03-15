using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;

namespace ollamidesk.RAG.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IOllamaModel _modelService;
        private string _userInput = string.Empty;
        private string _modelName = string.Empty;
        private bool _isBusy;

        public string UserInput
        {
            get => _userInput;
            set => SetProperty(ref _userInput, value);
        }

        public string ModelName
        {
            get => _modelName;
            set => SetProperty(ref _modelName, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();
        public DocumentViewModel DocumentViewModel { get; }

        public ICommand SendMessageCommand { get; }

        public MainViewModel(IOllamaModel modelService, DocumentViewModel documentViewModel)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            DocumentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));

            SendMessageCommand = new RelayCommand(
                async _ => await SendMessageAsync(),
                _ => !string.IsNullOrWhiteSpace(UserInput) && !IsBusy);
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

                // Get any documents to augment with
                var (augmentedPrompt, sources) = await DocumentViewModel.GenerateAugmentedPromptAsync(userQuery);

                // Get chat history for context
                var historyContext = ChatHistory
                    .Take(10) // Limit to last 10 messages for context
                    .Select(m => $"User: {m.UserQuery}\nAssistant: {m.ModelResponse}")
                    .ToList();

                // Generate response
                string response = await _modelService.GenerateResponseAsync(
                    augmentedPrompt,
                    string.Empty, // No loaded document, using RAG instead
                    historyContext
                );

                // Create chat message
                var message = new ChatMessage
                {
                    UserQuery = userQuery,
                    ModelResponse = response,
                    UsedRag = DocumentViewModel.IsRagEnabled && sources.Count > 0,
                    SourceChunkIds = sources.Select(s => s.Id).ToList()
                };

                // Add to history
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ChatHistory.Count >= 50)
                    {
                        ChatHistory.RemoveAt(0);
                    }
                    ChatHistory.Add(message);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}