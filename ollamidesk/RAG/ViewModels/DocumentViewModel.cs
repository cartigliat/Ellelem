// ollamidesk/RAG/ViewModels/DocumentViewModel.cs
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
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations; // Needed for DocumentProcessorFactory

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
        private readonly IRagConfigurationService _configService;
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
            DocumentProcessorFactory documentProcessorFactory,
            IRagConfigurationService configService)
        {
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _promptEngineeringService = promptEngineeringService ?? throw new ArgumentNullException(nameof(promptEngineeringService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _documentProcessorFactory = documentProcessorFactory ?? throw new ArgumentNullException(nameof(documentProcessorFactory));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            AddDocumentCommand = new RelayCommand(async _ => await AddDocumentAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDocumentsAsync());

            Task.Run(async () => {
                try
                {
                    await LoadDocumentsAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                        $"Background load failed: {ex.Message}");
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
                var documentViewModels = documents.Select(doc => new DocumentItemViewModel(
                        doc,
                        _documentManagementService,
                        _documentProcessingService,
                        _diagnostics,
                        _documentRepository)).ToList();

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
                string[] supportedExtensions = _documentProcessorFactory.GetSupportedExtensions();
                string filter = "All supported documents|" + string.Join(";", supportedExtensions.Select(ext => $"*{ext}"));
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

                        var document = await _documentManagementService.AddDocumentAsync(openFileDialog.FileName)
                            .ConfigureAwait(false);
                        var documentViewModel = new DocumentItemViewModel(
                            document,
                            _documentManagementService,
                            _documentProcessingService,
                            _diagnostics,
                            _documentRepository);

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

        // Method removed in original file provided - keeping it removed here
        // public async Task<(string prompt, List<DocumentChunk> sources)> GenerateAugmentedPromptAsync(string query)
        // {
        //    // ... implementation ...
        // }
    }
}