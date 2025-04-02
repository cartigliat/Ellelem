// ollamidesk/RAG/ViewModels/DocumentItemViewModel.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;
using static System.Net.Mime.MediaTypeNames;

namespace ollamidesk.RAG.ViewModels
{
    public class DocumentItemViewModel : ViewModelBase
    {
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IDocumentRepository _documentRepository;
        private bool _isSelected;
        private bool _isProcessing;
        private Document _document;

        public string Id => _document.Id;
        public string Name => _document.Name;
        public bool IsProcessed { get; private set; }

        public string FileSizeDisplay => FormatFileSize(_document.FileSize);
        public string DocumentType => _document.DocumentType;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _document.IsSelected = value;
                    UpdateSelectionAsync(value).FireAndForget(_diagnostics, "DocumentItemViewModel");
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set => SetProperty(ref _isProcessing, value, nameof(Status)); // Notify Status change
        }

        public string Status => IsProcessing ? "Processing..." : (IsProcessed ? "Processed" : "Not Processed");

        public ICommand ProcessCommand { get; }
        public ICommand DeleteCommand { get; }

        public DocumentItemViewModel(
            Document document,
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService,
            RagDiagnosticsService diagnostics,
            IDocumentRepository documentRepository)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));

            IsProcessed = _document.IsProcessed;
            _isSelected = _document.IsSelected;

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
            // Set IsProcessing - PropertyChanged will notify UI, CommandManager will handle requery eventually
            IsProcessing = true;
            // ((RelayCommand)ProcessCommand).RaiseCanExecuteChanged(); // REMOVED
            // ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged(); // REMOVED


            try
            {
                _diagnostics.StartOperation("ProcessDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Processing document: {Id}");

                _document = await _documentProcessingService.ProcessDocumentAsync(_document)
                    .ConfigureAwait(false);

                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessed = _document.IsProcessed;
                    IsSelected = true;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(IsProcessed));
                });

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Document processing finished for: {Id}, Processed: {IsProcessed}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error processing document {Id}: {ex.Message}");
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    MessageBox.Show($"Error processing document '{Name}': {ex.Message}", "Processing Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Reset IsProcessing - PropertyChanged will notify UI, CommandManager will handle requery eventually
                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessing = false;
                    // ((RelayCommand)ProcessCommand).RaiseCanExecuteChanged(); // REMOVED
                    // ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged(); // REMOVED
                });
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        private async Task DeleteDocumentAsync()
        {
            bool confirmDelete = false;
            CollectionHelper.ExecuteOnUIThread(() => {
                confirmDelete = MessageBox.Show(
                    $"Are you sure you want to delete '{Name}'?\nThis will remove the document and its processed data.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes;
            });

            if (!confirmDelete) return;


            // Set IsProcessing - PropertyChanged will notify UI, CommandManager will handle requery eventually
            IsProcessing = true;
            // ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged(); // REMOVED
            // ((RelayCommand)ProcessCommand).RaiseCanExecuteChanged(); // REMOVED


            try
            {
                _diagnostics.StartOperation("DeleteDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Deleting document: {Id}");

                await _documentManagementService.DeleteDocumentAsync(Id)
                    .ConfigureAwait(false);

                var parentViewModel = GetParentViewModel();
                if (parentViewModel != null)
                {
                    var docToRemove = parentViewModel.Documents.FirstOrDefault(dvm => dvm == this);
                    if (docToRemove != null)
                    {
                        CollectionHelper.ExecuteOnUIThread(() => {
                            CollectionHelper.RemoveSafely(parentViewModel.Documents, docToRemove);
                        });
                        _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel", $"Removed document from UI: {Id}");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", $"Could not find DocumentItemViewModel instance in parent collection for ID: {Id}");
                    }

                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", $"Could not find parent DocumentViewModel to remove item for ID: {Id}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                    $"Document deleted successfully from service: {Id}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                    $"Error deleting document {Id}: {ex.Message}");
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    MessageBox.Show($"Error deleting document '{Name}': {ex.Message}", "Deletion Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });

                // Reset IsProcessing if deletion failed - PropertyChanged will notify UI, CommandManager will handle requery eventually
                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessing = false;
                    // ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged(); // REMOVED
                    // ((RelayCommand)ProcessCommand).RaiseCanExecuteChanged(); // REMOVED
                });
            }
            finally
            {
                _diagnostics.EndOperation("DeleteDocument");
                // Do not reset IsProcessing here if successful, as the item is gone.
            }
        }

        private DocumentViewModel? GetParentViewModel()
        {
            // Try to find parent ViewModel in application windows (assuming single MainWindow)
            // Fully qualify Application to resolve ambiguity
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is MainWindow mainWindow && mainWindow.DataContext is MainViewModel mainViewModel)
                {
                    return mainViewModel.DocumentViewModel;
                }
                // Add checks for other potential parent windows if necessary
            }
            _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", "Could not find parent DocumentViewModel via Application Windows.");
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