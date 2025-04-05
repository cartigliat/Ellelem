// ---- ollamidesk/RAG/Windows/RagSettingsWindow.xaml.cs ----
// (This file defines the VIEW's code-behind)

using System.Windows;
using ollamidesk.RAG.Services; // For IRagConfigurationService
using ollamidesk.RAG.Diagnostics; // For RagDiagnosticsService
using ollamidesk.RAG.ViewModels; // For RagSettingsViewModel

// Ensure this namespace matches the x:Class attribute in RagSettingsWindow.xaml
namespace ollamidesk.RAG.Windows
{
    /// <summary>
    /// Interaction logic for RagSettingsWindow.xaml
    /// </summary>
    public partial class RagSettingsWindow : Window
    {
        // Constructor receives dependencies from the DI container
        // These dependencies are needed to manually create the ViewModel
        public RagSettingsWindow(
            IRagConfigurationService configService,
            RagDiagnosticsService diagnostics)
        {
            // Standard WPF initialization - loads the XAML definition
            InitializeComponent();

            // Create the ViewModel instance manually.
            // Pass the dependencies injected into this constructor (configService, diagnostics)
            // and also pass 'this' (the Window instance itself) as required by the ViewModel.
            var viewModel = new RagSettingsViewModel(configService, diagnostics, this);

            // Set the DataContext of this Window (the View) to the ViewModel instance.
            // This enables the XAML bindings defined in RagSettingsWindow.xaml to connect
            // to the properties and commands in RagSettingsViewModel.
            this.DataContext = viewModel;
        }
    }
}