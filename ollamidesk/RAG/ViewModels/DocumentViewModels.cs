using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services;
using ollamidesk.RAG;

namespace ollamidesk.RAG.ViewModels
{
    public class DocumentViewModel : ViewModelBase, IRagProvider
    {
        private readonly RagService _ragService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
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

        public DocumentViewModel(
            RagService ragService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository)
        {
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));

            AddDocumentCommand = new RelayCommand(async _ => await AddDocumentAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDocumentsAsync());

            // Load documents on startup
            LoadDocumentsAsync().ConfigureAwait(false);

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel", "Initialized DocumentViewModel");
        }

        /// <summary>
        /// Gets the RAG service instance
        /// </summary>
        /// <returns>The RAG service</returns>
        public RagService GetRagService()
        {
            return _ragService;
        }

        public async Task LoadDocumentsAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                _diagnostics.StartOperation("LoadDocuments");

                var documents = await _ragService.GetAllDocumentsAsync();

                // Update collection on UI thread using CollectionHelper
                CollectionHelper.ClearSafely(Documents);

                foreach (var doc in documents)
                {
                    // Add documents one by one on UI thread
                    CollectionHelper.AddSafely(Documents,
                        new DocumentItemViewModel(doc, _ragService, _diagnostics, _documentRepository));
                }

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Loaded {documents.Count} documents");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error loading documents: {ex.Message}");

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading documents: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsBusy = false;
                _diagnostics.EndOperation("LoadDocuments");
            }
        }

        private async Task AddDocumentAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|PDF files (*.pdf)|*.pdf|Code files (*.cs;*.js;*.py;*.java)|*.cs;*.js;*.py;*.java|All files (*.*)|*.*",
                Title = "Select document to add"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    _diagnostics.StartOperation("AddDocument");
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                        $"Adding document: {openFileDialog.FileName}");

                    var document = await _ragService.AddDocumentAsync(openFileDialog.FileName);

                    // Add new document to collection on UI thread
                    CollectionHelper.AddSafely(Documents,
                        new DocumentItemViewModel(document, _ragService, _diagnostics, _documentRepository));

                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                        $"Document added successfully: {document.Id}");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                        $"Error adding document: {ex.Message}");

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error adding document: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    IsBusy = false;
                    _diagnostics.EndOperation("AddDocument");
                }
            }
        }

        public async Task<(string prompt, List<DocumentChunk> sources)> GenerateAugmentedPromptAsync(string query)
        {
            if (!IsRagEnabled)
            {
                return (query, new List<DocumentChunk>());
            }

            try
            {
                _diagnostics.StartOperation("GenerateAugmentedPrompt");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Generating augmented prompt for query: {query.Substring(0, Math.Min(50, query.Length))}");

                var selectedDocs = Documents
                    .Where(d => d.IsSelected && d.IsProcessed)
                    .Select(d => d.Id)
                    .ToList();

                if (selectedDocs.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentViewModel",
                        "No processed documents selected for RAG");
                    return (query, new List<DocumentChunk>());
                }

                var (augmentedPrompt, sources) = await _ragService.GenerateAugmentedPromptAsync(query, selectedDocs);

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Generated augmented prompt with {sources.Count} sources");

                return (augmentedPrompt, sources.ToList());
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error generating augmented prompt: {ex.Message}");

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error generating augmented prompt: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });

                return (query, new List<DocumentChunk>());
            }
            finally
            {
                _diagnostics.EndOperation("GenerateAugmentedPrompt");
            }
        }
    }

    public class DocumentItemViewModel : ViewModelBase
    {
        private readonly RagService _ragService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
        private bool _isSelected;
        private bool _isProcessing;
        private bool _isLoadingFullContent;
        private Document _document; // Reference to the actual document

        public string Id { get; }
        public string Name { get; }
        public bool IsProcessed { get; private set; }
        public bool IsLargeFile => _document.IsLargeFile;
        public bool IsContentTruncated => _document.IsContentTruncated;
        public string FileSizeDisplay => FormatFileSize(_document.FileSize);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _document.IsSelected = value;
                    // Save the document when selection changes
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _ragService.UpdateDocumentSelectionAsync(Id, value);

                            _diagnostics.Log(DiagnosticLevel.Debug, "DocumentItemViewModel",
                                $"Updated selection state for document {Id} to {value}");
                        }
                        catch (Exception ex)
                        {
                            // Log but don't block the UI
                            _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                                $"Error updating document selection: {ex.Message}");
                        }
                    });
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public bool IsLoadingFullContent
        {
            get => _isLoadingFullContent;
            set => SetProperty(ref _isLoadingFullContent, value);
        }

        public string Status => IsProcessed
            ? "Processed"
            : (IsProcessing
                ? "Processing..."
                : (IsLargeFile
                    ? "Large File"
                    : "Not Processed"));

        public ICommand ProcessCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand LoadFullContentCommand { get; }

        public DocumentItemViewModel(
            Document document,
            RagService ragService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository)
        {
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _document = document ?? throw new ArgumentNullException(nameof(document));

            Id = document.Id;
            Name = document.Name;
            IsProcessed = document.IsProcessed;
            _isSelected = document.IsSelected;

            ProcessCommand = new RelayCommand(
                async _ => await ProcessDocumentAsync(),
                _ => !IsProcessed && !IsProcessing);

            DeleteCommand = new RelayCommand(
                async _ => await DeleteDocumentAsync(),
                _ => !IsProcessing && !IsLoadingFullContent);

            LoadFullContentCommand = new RelayCommand(
                async _ => await LoadFullContentAsync(),
                _ => IsContentTruncated && !IsLoadingFullContent);
        }

        private async Task LoadFullContentAsync()
        {
            try
            {
                IsLoadingFullContent = true;

                _diagnostics.StartOperation("LoadFullDocumentContent");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Loading full content for document: {Id}");

                // Load full content
                _document = await _documentRepository.LoadFullContentAsync(Id);

                // Notify UI of changes
                OnPropertyChanged(nameof(IsContentTruncated));

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Full content loaded for document: {Id}, content length: {_document.Content.Length} chars");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error loading full content: {ex.Message}");

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading full document content: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoadingFullContent = false;
                _diagnostics.EndOperation("LoadFullDocumentContent");
            }
        }

        private async Task ProcessDocumentAsync()
        {
            try
            {
                IsProcessing = true;
                OnPropertyChanged(nameof(Status));

                _diagnostics.StartOperation("ProcessDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Processing document: {Id}");

                // For large files that still have truncated content, load full content first
                if (IsLargeFile && IsContentTruncated)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                        $"Large file with truncated content detected, loading full content before processing");

                    _document = await _documentRepository.LoadFullContentAsync(Id);
                    OnPropertyChanged(nameof(IsContentTruncated));
                }

                // Process the document
                _document = await _ragService.ProcessDocumentAsync(Id);

                IsProcessed = true;
                IsSelected = true;

                // Update UI properties
                OnPropertyChanged(nameof(Status));

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Document processed successfully: {Id}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error processing document: {ex.Message}");

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error processing document: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(Status));
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        private async Task DeleteDocumentAsync()
        {
            if (MessageBox.Show($"Are you sure you want to delete {Name}?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    _diagnostics.StartOperation("DeleteDocument");
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                        $"Deleting document: {Id}");

                    await _ragService.DeleteDocumentAsync(Id);

                    // Remove from collection - find parent ViewModel
                    var parentViewModel = GetParentViewModel();
                    if (parentViewModel != null)
                    {
                        var docToRemove = parentViewModel.Documents.FirstOrDefault(d => d.Id == Id);
                        if (docToRemove != null)
                        {
                            CollectionHelper.RemoveSafely(parentViewModel.Documents, docToRemove);

                            _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                                $"Removed document from UI: {Id}");
                        }
                    }

                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                        $"Document deleted successfully: {Id}");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                        $"Error deleting document: {ex.Message}");

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error deleting document: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    _diagnostics.EndOperation("DeleteDocument");
                }
            }
        }

        private DocumentViewModel? GetParentViewModel()
        {
            // Try to find parent ViewModel in application windows
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window.DataContext is MainViewModel mainViewModel &&
                    mainViewModel.DocumentViewModel is DocumentViewModel docViewModel)
                {
                    return docViewModel;
                }
            }
            return null;
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }
}