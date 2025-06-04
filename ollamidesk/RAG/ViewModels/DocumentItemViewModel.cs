// ollamidesk/RAG/ViewModels/DocumentItemViewModel.cs
// CORRECTED VERSION - Further refined UI threading for collection updates

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.Dialogs; // For CustomConfirmDialog
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;

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
        private bool _isProcessed;

        public string Id => _document.Id;
        public string Name => _document.Name;
        public bool IsProcessed
        {
            get => _isProcessed;
            private set => SetProperty(ref _isProcessed, value, nameof(Status));
        }

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
            private set => SetProperty(ref _isProcessing, value, nameof(Status));
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

            _isProcessed = _document.IsProcessed;
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
            CollectionHelper.ExecuteOnUIThread(() => IsProcessing = true);

            try
            {
                _diagnostics.StartOperation("ProcessDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Processing document: {Id}");

                _document = await _documentProcessingService.ProcessDocumentAsync(_document)
                    .ConfigureAwait(false);

                CollectionHelper.ExecuteOnUIThread(() => {
                    IsProcessed = _document.IsProcessed;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(IsProcessed));
                    _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                       $"UI Updated for document processing finished for: {Id}, Processed: {IsProcessed}");
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                   $"Error processing document {Id}: {ex.Message}");

                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    IsProcessed = false;
                    OnPropertyChanged(nameof(Status));
                    MessageBox.Show($"Error processing document '{Name}': {ex.Message}", "Processing Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                CollectionHelper.ExecuteOnUIThread(() => IsProcessing = false);
                _diagnostics.EndOperation("ProcessDocument");
            }
        }

        private async Task DeleteDocumentAsync()
        {
            // RelayCommand ensures this part runs on the UI thread.
            var dialog = new CustomConfirmDialog("Confirm Delete", $"Are you sure you want to delete '{Name}'?\nThis will remove the document and its processed data.");
            // To ensure the dialog is owned by the main window and centers correctly:
            if (System.Windows.Application.Current.MainWindow != null && System.Windows.Application.Current.MainWindow.IsLoaded)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }
            bool? dialogResult = dialog.ShowDialog();
            bool confirmDelete = dialogResult == true;

            if (!confirmDelete)
            {
                return; // User cancelled deletion
            }

            IsProcessing = true; // Set IsProcessing on UI thread *after* confirmation and *before* starting background work

            try
            {
                _diagnostics.StartOperation("DeleteDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Deleting document: {Id}");

                // Step 2: Perform the actual deletion on a background thread.
                // Use Task.Run and ConfigureAwait(false) to move the blocking work off the UI thread.
                await Task.Run(async () => {
                    await _documentManagementService.DeleteDocumentAsync(Id);
                }).ConfigureAwait(false); // Continue on a thread pool thread after deletion

                // Step 3: All UI updates related to the collection must be explicitly marshaled to the UI thread.
                // The entire block accessing parentViewModel and its Documents collection needs to be on UI thread.
                CollectionHelper.ExecuteOnUIThread(() => {
                    var parentViewModel = GetParentViewModel(); // Get parent ViewModel on UI thread
                    if (parentViewModel != null)
                    {
                        // Access and modify the Documents collection on the UI thread
                        var docToRemove = parentViewModel.Documents.FirstOrDefault(dvm => dvm.Id == this.Id);
                        if (docToRemove != null)
                        {
                            CollectionHelper.RemoveSafely(parentViewModel.Documents, docToRemove);
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
                });

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Document deleted successfully from service: {Id}");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                   $"Error deleting document {Id}: {ex.Message}");

                // Step 4: Handle errors and update UI on the UI thread.
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    IsProcessing = false; // Reset IsProcessing on UI thread
                    MessageBox.Show($"Error deleting document '{Name}': {ex.Message}", "Deletion Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Ensure IsProcessing is false if deletion failed and item potentially remains.
                // This 'finally' block ensures it's reset even if the try/catch re-throws.
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    var parentViewModel = GetParentViewModel();
                    bool itemStillExists = parentViewModel?.Documents.Any(dvm => dvm.Id == this.Id) ?? false;
                    if (itemStillExists)
                    {
                        IsProcessing = false;
                    }
                });
                _diagnostics.EndOperation("DeleteDocument");
            }
        }

        // Method remains unchanged
        private DocumentViewModel? GetParentViewModel()
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is MainWindow mainWindow && mainWindow.DataContext is MainViewModel mainViewModel)
                {
                    return mainViewModel.DocumentViewModel;
                }
            }
            _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", "Could not find parent DocumentViewModel via Application Windows.");
            return null;
        }

        // Method remains unchanged
        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1) { size /= 1024; suffixIndex++; }
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }
}