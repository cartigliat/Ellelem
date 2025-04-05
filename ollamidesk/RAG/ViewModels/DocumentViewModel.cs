// ollamidesk/RAG/ViewModels/DocumentViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input; // <-- Added this
using Microsoft.Win32;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services; // Note: Namespace might need adjustment if Interfaces are separate
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations; // Needed for DocumentProcessorFactory
using ollamidesk.Configuration; // Added for IRagConfigurationService
using ollamidesk.DependencyInjection; // <-- Added this for ServiceProviderFactory
using ollamidesk.RAG.Windows; // <-- Added this for RagSettingsWindow namespace (adjust if different)

namespace ollamidesk.RAG.ViewModels
{
    public class DocumentViewModel : ViewModelBase
    {
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;
        private readonly IRetrievalService _retrievalService; // Keep if used elsewhere, though GenerateAugmentedPromptAsync is removed
        private readonly IPromptEngineeringService _promptEngineeringService; // Keep if used elsewhere
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
        private readonly DocumentProcessorFactory _documentProcessorFactory;
        private readonly IRagConfigurationService _configService; // Added dependency
        private bool _isRagEnabled;
        private bool _isBusy;

        public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();

        public bool IsRagEnabled
        {
            get => _isRagEnabled;
            // Consider adding logic if changing this requires action (e.g., update config)
            set => SetProperty(ref _isRagEnabled, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            // Use private set if only controlled internally
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand AddDocumentCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenRagSettingsCommand { get; } // <-- Added this Command Property

        // Constructor updated with IRagConfigurationService
        public DocumentViewModel(
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService,
            IRetrievalService retrievalService,
            IPromptEngineeringService promptEngineeringService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository,
            DocumentProcessorFactory documentProcessorFactory,
            IRagConfigurationService configService) // Added parameter
        {
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _promptEngineeringService = promptEngineeringService ?? throw new ArgumentNullException(nameof(promptEngineeringService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _documentProcessorFactory = documentProcessorFactory ?? throw new ArgumentNullException(nameof(documentProcessorFactory));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService)); // Assign added parameter

            AddDocumentCommand = new RelayCommand(async _ => await AddDocumentAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDocumentsAsync());
            OpenRagSettingsCommand = new RelayCommand(_ => ExecuteOpenRagSettings()); // <-- Initialize Command

            // Load documents asynchronously on initialization
            // Consider handling potential exceptions during background load
            Task.Run(async () => {
                try
                {
                    await LoadDocumentsAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                        $"Background load failed: {ex.Message}");
                    // Optionally inform the user on the UI thread
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
                // Create DocumentItemViewModel instances for each document
                var documentViewModels = documents.Select(doc => new DocumentItemViewModel(
                        doc,
                        _documentManagementService,
                        _documentProcessingService,
                        _diagnostics,
                        _documentRepository)).ToList(); // Pass necessary dependencies

                // Update the collection on the UI thread safely
                CollectionHelper.ExecuteOnUIThread(() => {
                    // Replace entire collection efficiently
                    CollectionHelper.BatchUpdateSafely(Documents, documentViewModels, true);
                });

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                    $"Loaded {documents.Count} documents");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error loading documents: {ex.Message}");
                // Show error message on UI thread
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
                // Get supported extensions dynamically from the factory
                string[] supportedExtensions = _documentProcessorFactory.GetSupportedExtensions();
                string filter = "All supported documents|" + string.Join(";", supportedExtensions.Select(ext => $"*{ext}"));
                // Add specific known types for user convenience
                filter += "|Word Documents (*.docx)|*.docx";
                filter += "|PDF Files (*.pdf)|*.pdf";
                filter += "|Text Files (*.txt)|*.txt";
                filter += "|Markdown Files (*.md)|*.md";
                filter += "|All Files (*.*)|*.*";


                var openFileDialog = new OpenFileDialog
                {
                    Filter = filter,
                    Title = "Select document to add"
                    // Consider setting InitialDirectory if applicable
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        IsBusy = true;
                        _diagnostics.StartOperation("AddDocument");
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                            $"Adding document: {openFileDialog.FileName}");

                        // Call service to add the document
                        var document = await _documentManagementService.AddDocumentAsync(openFileDialog.FileName)
                            .ConfigureAwait(false); // Stay on background thread

                        // Create the ViewModel for the new document
                        var documentViewModel = new DocumentItemViewModel(
                            document,
                            _documentManagementService,
                            _documentProcessingService,
                            _diagnostics,
                            _documentRepository); // Pass dependencies

                        // Add the new ViewModel to the collection on the UI thread
                        CollectionHelper.ExecuteOnUIThread(() => {
                            CollectionHelper.AddSafely(Documents, documentViewModel);
                        });

                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel",
                            $"Document added successfully: {document.Id}");
                    }
                    catch (Exception ex) // Catch specific exceptions if possible
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                            $"Error adding document: {ex.Message}");
                        // Show error message on UI thread
                        CollectionHelper.ExecuteOnUIThread(() =>
                        {
                            MessageBox.Show($"Error adding document: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                    finally
                    {
                        IsBusy = false; // Ensure IsBusy is reset
                        _diagnostics.EndOperation("AddDocument");
                    }
                }
            }
            catch (Exception ex) // Catch errors related to showing the dialog itself
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel",
                    $"Error preparing document dialog: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Method removed in original file provided - keeping it removed here
        // public async Task<(string prompt, List<DocumentChunk> sources)> GenerateAugmentedPromptAsync(string query)
        // {
        //    // ... implementation removed ...
        // }

        // --- Add this method to execute the command ---
        private void ExecuteOpenRagSettings()
        {
            _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel", "Open RAG Settings command executed.");
            try
            {
                // Get the settings window instance via the ServiceProviderFactory
                // Ensure the namespace ollamidesk.RAG.Windows is correct for RagSettingsWindow
                var settingsWindow = ServiceProviderFactory.GetService<ollamidesk.RAG.Windows.RagSettingsWindow>();

                if (settingsWindow != null)
                {
                    // Try to find the MainWindow to set the owner for modality
                    Window? owner = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                    if (owner != null)
                    {
                        settingsWindow.Owner = owner;
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentViewModel", "Could not find MainWindow to set owner for RagSettingsWindow.");
                    }

                    settingsWindow.ShowDialog(); // Show as modal dialog
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentViewModel", "RAG Settings window opened via command.");
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel", "Failed to resolve RAG Settings window from Service Provider for command.");
                    // Show error message on UI thread if possible/needed
                    CollectionHelper.ExecuteOnUIThread(() => {
                        MessageBox.Show("Could not open RAG Settings window. Check application setup.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentViewModel", $"Error opening RAG Settings window via command: {ex.Message}");
                CollectionHelper.ExecuteOnUIThread(() => {
                    MessageBox.Show($"Error opening RAG Settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        // --- End of new method ---
    }
}