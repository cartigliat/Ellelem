// ollamidesk/RAG/ViewModels/DocumentItemViewModel.cs
// MODIFIED VERSION - Removed automatic setting of IsSelected after processing
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
using static System.Net.Mime.MediaTypeNames; // Note: This using statement might not be necessary

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
        private bool _isProcessed; // Changed to private field with public getter

        public string Id => _document.Id;
        public string Name => _document.Name;
        public bool IsProcessed // Public getter remains
        {
            get => _isProcessed;
            private set => SetProperty(ref _isProcessed, value, nameof(Status)); // Notify Status change too
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
                    // Update selection in the backend asynchronously
                    UpdateSelectionAsync(value).FireAndForget(_diagnostics, "DocumentItemViewModel");
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set => SetProperty(ref _isProcessing, value, nameof(Status)); // Notify Status change
        }

        // Status property now depends on IsProcessing and IsProcessed
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

            // Initialize properties from the document model
            _isProcessed = _document.IsProcessed;
            _isSelected = _document.IsSelected;

            // RelayCommand uses CommandManager.RequerySuggested by default
            ProcessCommand = new RelayCommand(
                async _ => await ProcessDocumentAsync(),
                _ => !IsProcessed && !IsProcessing); // Can execute if not already processed or processing

            DeleteCommand = new RelayCommand(
                async _ => await DeleteDocumentAsync(),
                _ => !IsProcessing); // Can execute if not currently processing
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
                // Potentially revert UI state or show error
            }
        }

        private async Task ProcessDocumentAsync()
        {
            // Set IsProcessing immediately on the UI thread
            CollectionHelper.ExecuteOnUIThread(() => IsProcessing = true);
            // CommandManager.InvalidateRequerySuggested(); // Optional: Force CanExecute update

            try
            {
                _diagnostics.StartOperation("ProcessDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Processing document: {Id}");

                // Perform the actual processing (potentially long-running)
                _document = await _documentProcessingService.ProcessDocumentAsync(_document)
                    .ConfigureAwait(false); // Stay on background thread after await

                // Update UI properties on the UI thread after processing completes
                CollectionHelper.ExecuteOnUIThread(() => {
                    // Update IsProcessed based on the result from the service
                    IsProcessed = _document.IsProcessed;

                    // <<< MODIFIED: Removed setting IsSelected = true here >>>
                    // IsSelected = true; // This was already commented out in the original

                    // Explicitly notify Status and IsProcessed changes
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

                // Update UI on error (ensure IsProcessed is false)
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    IsProcessed = false; // Ensure IsProcessed is false on error
                    OnPropertyChanged(nameof(Status)); // Update status display
                    MessageBox.Show($"Error processing document '{Name}': {ex.Message}", "Processing Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Reset IsProcessing on the UI thread in the finally block
                CollectionHelper.ExecuteOnUIThread(() => IsProcessing = false);
                // CommandManager.InvalidateRequerySuggested(); // Optional: Force CanExecute update
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

            // Set IsProcessing to disable buttons during delete
            CollectionHelper.ExecuteOnUIThread(() => IsProcessing = true);
            // CommandManager.InvalidateRequerySuggested(); // Optional

            try
            {
                _diagnostics.StartOperation("DeleteDocument");
                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Deleting document: {Id}");

                await _documentManagementService.DeleteDocumentAsync(Id)
                    .ConfigureAwait(false);

                // Remove this item from the parent collection
                var parentViewModel = GetParentViewModel();
                if (parentViewModel != null)
                {
                    // Execute removal on UI thread
                    CollectionHelper.ExecuteOnUIThread(() => {
                        var docToRemove = parentViewModel.Documents.FirstOrDefault(dvm => dvm.Id == this.Id); // Safer comparison by Id
                        if (docToRemove != null)
                        {
                            CollectionHelper.RemoveSafely(parentViewModel.Documents, docToRemove);
                            _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel", $"Removed document from UI: {Id}");
                        }
                        else
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", $"Could not find DocumentItemViewModel instance in parent collection for ID: {Id}");
                        }
                    });
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "DocumentItemViewModel", $"Could not find parent DocumentViewModel to remove item for ID: {Id}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "DocumentItemViewModel",
                   $"Document deleted successfully from service: {Id}");
                // No need to reset IsProcessing if successful, as the item is gone.
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DocumentItemViewModel",
                   $"Error deleting document {Id}: {ex.Message}");

                // Reset IsProcessing if deletion failed
                CollectionHelper.ExecuteOnUIThread(() =>
                {
                    IsProcessing = false;
                    MessageBox.Show($"Error deleting document '{Name}': {ex.Message}", "Deletion Error",
                       MessageBoxButton.OK, MessageBoxImage.Error);
                });
                // CommandManager.InvalidateRequerySuggested(); // Optional
            }
            finally
            {
                // Ensure IsProcessing is false if deletion failed and item potentially remains
                var parentViewModel = GetParentViewModel(); // Re-check parent
                bool itemStillExists = parentViewModel?.Documents.Any(dvm => dvm.Id == this.Id) ?? false;
                if (itemStillExists) // Only reset if the item wasn't successfully removed
                {
                    CollectionHelper.ExecuteOnUIThread(() => IsProcessing = false);
                }
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