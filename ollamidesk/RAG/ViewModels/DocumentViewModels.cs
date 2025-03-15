using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;

namespace ollamidesk.RAG.ViewModels
{
    public class DocumentViewModel : ViewModelBase
    {
        private readonly RagService _ragService;
        private bool _isRagEnabled;
        private bool _isBusy;

        public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();

        public bool IsRagEnabled
        {
            get => _isRagEnabled;
            set => SetProperty(ref _isRagEnabled, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand AddDocumentCommand { get; }
        public ICommand RefreshCommand { get; }

        public DocumentViewModel(RagService ragService)
        {
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));

            AddDocumentCommand = new RelayCommand(async _ => await AddDocumentAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDocumentsAsync());

            // Load documents on startup
            LoadDocumentsAsync().ConfigureAwait(false);
        }

        public async Task LoadDocumentsAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                var documents = await _ragService.GetAllDocumentsAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Documents.Clear();
                    foreach (var doc in documents)
                    {
                        Documents.Add(new DocumentItemViewModel(doc, _ragService));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading documents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AddDocumentAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                Title = "Select document to add"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;

                    var document = await _ragService.AddDocumentAsync(openFileDialog.FileName);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Documents.Add(new DocumentItemViewModel(document, _ragService));
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error adding document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        public async Task<(string prompt, ObservableCollection<DocumentChunk> sources)> GenerateAugmentedPromptAsync(string query)
        {
            if (!IsRagEnabled)
            {
                return (query, new ObservableCollection<DocumentChunk>());
            }

            try
            {
                var selectedDocs = Documents
                    .Where(d => d.IsSelected && d.IsProcessed)
                    .Select(d => d.Id)
                    .ToList();

                if (selectedDocs.Count == 0)
                {
                    return (query, new ObservableCollection<DocumentChunk>());
                }

                var (augmentedPrompt, sources) = await _ragService.GenerateAugmentedPromptAsync(query, selectedDocs);
                return (augmentedPrompt, new ObservableCollection<DocumentChunk>(sources));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating augmented prompt: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return (query, new ObservableCollection<DocumentChunk>());
            }
        }
    }

    public class DocumentItemViewModel : ViewModelBase
    {
        private readonly RagService _ragService;
        private bool _isSelected;
        private bool _isProcessing;

        public string Id { get; }
        public string Name { get; }
        public bool IsProcessed { get; private set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public string Status => IsProcessed ? "Processed" : (IsProcessing ? "Processing..." : "Not Processed");

        public ICommand ProcessCommand { get; }
        public ICommand DeleteCommand { get; }

        public DocumentItemViewModel(Document document, RagService ragService)
        {
            _ragService = ragService;

            Id = document.Id;
            Name = document.Name;
            IsProcessed = document.IsProcessed;

            ProcessCommand = new RelayCommand(
                async _ => await ProcessDocumentAsync(),
                _ => !IsProcessed && !IsProcessing);

            DeleteCommand = new RelayCommand(
                async _ => await DeleteDocumentAsync(),
                _ => !IsProcessing);
        }

        private async Task ProcessDocumentAsync()
        {
            try
            {
                IsProcessing = true;
                OnPropertyChanged(nameof(Status));

                await _ragService.ProcessDocumentAsync(Id);

                IsProcessed = true;
                IsSelected = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(Status));
            }
        }

        private async Task DeleteDocumentAsync()
        {
            if (MessageBox.Show($"Are you sure you want to delete {Name}?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _ragService.DeleteDocumentAsync(Id);

                    // Remove from collection
                    var parentViewModel = GetParentViewModel();
                    if (parentViewModel != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var docToRemove = parentViewModel.Documents.FirstOrDefault(d => d.Id == Id);
                            if (docToRemove != null)
                            {
                                parentViewModel.Documents.Remove(docToRemove);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private DocumentViewModel? GetParentViewModel()
        {
            // Try to find parent ViewModel in application windows
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext is MainViewModel mainViewModel &&
                    mainViewModel.DocumentViewModel is DocumentViewModel docViewModel)
                {
                    return docViewModel;
                }
            }
            return null;
        }
    }
}