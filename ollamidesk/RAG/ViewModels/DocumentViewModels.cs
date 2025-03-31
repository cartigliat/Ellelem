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
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations;

namespace ollamidesk.RAG.ViewModels
{
    public class DocumentViewModel : ViewModelBase
    {
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;
        private readonly IRetrievalService _retrievalService;
        private readonly IPromptEngineeringService _promptEngineeringService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
        private readonly DocumentProcessorFactory _documentProcessorFactory;
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
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService,
            IRetrievalService retrievalService,
            IPromptEngineeringService promptEngineeringService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository,
            DocumentProcessorFactory documentProcessorFactory)
        {
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _promptEngineeringService = promptEngineeringService ?? throw new ArgumentNullException(nameof(promptEngineeringService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _documentProcessorFactory = documentProcessorFactory ?? throw new ArgumentNullException(nameof(documentProcessorFactory));

            AddDocumentCommand = new RelayCommand(async _ => await AddDocumentAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDocumentsAsync());

            // Load documents on startup with proper error handling
            Task.Run(async () => {
                try
                {
                    await LoadDocumentsAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                        $"Background load failed: {ex.Message}");

                    // Use dispatcher to show error on UI thread if needed
                    CollectionHelper.ExecuteOnUIThread(() => {
                        MessageBox.Show("Error loading documents: " + ex.Message, "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });

            _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel", "Initialized DocumentViewModel");
        }

        public async Task LoadDocumentsAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                _diagnostics.StartOperation("LoadDocuments");

                var documents = await _documentManagementService.GetAllDocumentsAsync().ConfigureAwait(false);

                // Create view models for each document
                var documentViewModels = documents.Select(doc => new DocumentItemViewModel(
                        doc,
                        _documentManagementService,
                        _documentProcessingService,
                        _diagnostics,
                        _documentRepository)).ToList();

                // Update collection in a single atomic operation on UI thread
                CollectionHelper.ExecuteOnUIThread(() => {
                    CollectionHelper.BatchUpdateSafely(Documents, documentViewModels, true);
                });

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Loaded {documents.Count} documents");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error loading documents: {ex.Message}");

                CollectionHelper.ExecuteOnUIThread(() =>
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
            try
            {
                // Get supported file extensions
                string[] supportedExtensions = _documentProcessorFactory.GetSupportedExtensions();

                // Build filter string for dialog
                string filter = "All supported documents|";
                filter += string.Join(";", supportedExtensions.Select(ext => $"*{ext}"));
                filter += "|Word Documents (*.docx)|*.docx";
                filter += "|PDF Files (*.pdf)|*.pdf";
                filter += "|Text Files (*.txt)|*.txt";
                filter += "|Markdown Files (*.md)|*.md";
                filter += "|All Files (*.*)|*.*";

                var openFileDialog = new OpenFileDialog
                {
                    Filter = filter,
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

                        // Add document asynchronously
                        var document = await _documentManagementService.AddDocumentAsync(openFileDialog.FileName)
                            .ConfigureAwait(false);

                        // Create a new document view model
                        var documentViewModel = new DocumentItemViewModel(
                            document,
                            _documentManagementService,
                            _documentProcessingService,
                            _diagnostics,
                            _documentRepository);

                        // Add document to collection as a single atomic operation
                        CollectionHelper.ExecuteOnUIThread(() => {
                            CollectionHelper.AddSafely(Documents, documentViewModel);
                        });

                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                            $"Document added successfully: {document.Id}");
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                            $"Error adding document: {ex.Message}");

                        CollectionHelper.ExecuteOnUIThread(() =>
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
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error preparing document dialog: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                // Retrieve relevant chunks using RetrievalService asynchronously
                var searchResults = await _retrievalService.RetrieveRelevantChunksAsync(query, selectedDocs)
                    .ConfigureAwait(false);

                if (searchResults.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentViewModel",
                        "No relevant chunks found for the query");
                    return (query, new List<DocumentChunk>());
                }

                // Extract chunks from search results
                var relevantChunks = searchResults.Select(result => result.Chunk).ToList();

                // Create augmented prompt using PromptEngineeringService
                string augmentedPrompt = await _promptEngineeringService.CreateAugmentedPromptAsync(query, relevantChunks)
                    .ConfigureAwait(false);

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Generated augmented prompt with {relevantChunks.Count} sources");

                return (augmentedPrompt, relevantChunks);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error generating augmented prompt: {ex.Message}");

                CollectionHelper.ExecuteOnUIThread(() =>
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
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
        private bool _isSelected;
        private bool _isProcessing;
        private Document _document; // Reference to the actual document

        public string Id { get; }
        public string Name { get; }
        public bool IsProcessed { get; private set; }

        // Keep FileSize for informational purposes
        public string FileSizeDisplay => FormatFileSize(_document.FileSize);

        // New property for document type
        public string DocumentType => _document.DocumentType;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _document.IsSelected = value;
                    // Use the FireAndForget extension method for background tasks
                    UpdateSelectionAsync(value).FireAndForget(_diagnostics, "DocumentItemViewModel");
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public string Status => IsProcessed
            ? "Processed"
            : (IsProcessing
                ? "Processing..."
                : "Not Processed");

        public ICommand ProcessCommand { get; }
        public ICommand DeleteCommand { get; }

        public DocumentItemViewModel(
            Document document,
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository)
        {
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
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
                _ => !IsProcessing);
        }

        private async Task UpdateSelectionAsync(bool isSelected)
        {
            try
            {
                await _documentManagementService.UpdateDocumentSelectionAsync(Id, isSelected)
                    .ConfigureAwait(false);

                _diagnostics.Log(DiagnosticLevel.Debug, "DocumentItemViewModel",
                    $"Updated selection state for document {Id} to {isSelected}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error updating document selection: {ex.Message}");
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

                // Process the document using DocumentProcessingService
                _document = await _documentProcessingService.ProcessDocumentAsync(_document)
                    .ConfigureAwait(false);

                // Update UI properties on UI thread
                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessed = true;
                    IsSelected = true;
                    OnPropertyChanged(nameof(Status));
                });

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Document processed successfully: {Id}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error processing document: {ex.Message}");

                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    MessageBox.Show($"Error processing document: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Update IsProcessing on UI thread
                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessing = false;
                    OnPropertyChanged(nameof(Status));
                });

                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        private async Task DeleteDocumentAsync()
        {
            // MessageBox must be shown on UI thread
            bool confirmDelete = false;

            await Task.Run(() => {
                CollectionHelper.ExecuteOnUIThread(() => {
                    confirmDelete = MessageBox.Show(
                        $"Are you sure you want to delete {Name}?",
                        "Confirm Delete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes;
                });
            });

            if (confirmDelete)
            {
                try
                {
                    _diagnostics.StartOperation("DeleteDocument");
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                        $"Deleting document: {Id}");

                    await _documentManagementService.DeleteDocumentAsync(Id)
                        .ConfigureAwait(false);

                    // Remove from collection - find parent ViewModel
                    var parentViewModel = GetParentViewModel();
                    if (parentViewModel != null)
                    {
                        CollectionHelper.ExecuteOnUIThread(() => {
                            var docToRemove = parentViewModel.Documents.FirstOrDefault(d => d.Id == Id);
                            if (docToRemove != null)
                            {
                                CollectionHelper.RemoveSafely(parentViewModel.Documents, docToRemove);
                            }
                        });

                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                            $"Removed document from UI: {Id}");
                    }

                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                        $"Document deleted successfully: {Id}");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                        $"Error deleting document: {ex.Message}");

                    CollectionHelper.ExecuteOnUIThread(() =>
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