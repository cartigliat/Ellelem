using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Transition;
using ollamidesk.Services;

namespace ollamidesk
{
    public partial class SideMenuWindow : Window
    {
        private readonly OllamaModelFactory? _modelFactory;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly CommandLineService? _commandLineService;

        public string? SelectedModel { get; private set; }
        public string? LoadedDocument { get; private set; }

        // Legacy constructor for backward compatibility
        public SideMenuWindow()
        {
            InitializeComponent();

            // Try to get services from DI container if available
            try
            {
                if (ServiceProviderFactory.IsInitialized)
                {
                    _modelFactory = ServiceProviderFactory.GetService<OllamaModelFactory>();
                    _diagnostics = ServiceProviderFactory.GetService<RagDiagnosticsService>();
                    _commandLineService = ServiceProviderFactory.GetService<CommandLineService>();
                }
                else
                {
                    _diagnostics = LegacySupport.CreateDiagnosticsService();
                }
            }
            catch
            {
                _diagnostics = LegacySupport.CreateDiagnosticsService();
            }

            PopulateModelListAsync();
        }

        // New constructor with DI
        public SideMenuWindow(
            OllamaModelFactory modelFactory,
            RagDiagnosticsService diagnostics,
            CommandLineService commandLineService)
        {
            InitializeComponent();

            _modelFactory = modelFactory ?? throw new ArgumentNullException(nameof(modelFactory));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _commandLineService = commandLineService ?? throw new ArgumentNullException(nameof(commandLineService));

            PopulateModelListAsync();
        }

        private async void PopulateModelListAsync()
        {
            try
            {
                string modelList;

                if (_commandLineService != null)
                {
                    // Use the CommandLineService
                    var result = await _commandLineService.ExecuteCommandAsync("cmd.exe", "/C ollama list");
                    modelList = result.output;
                }
                else
                {
                    // Fall back to the utility class
                    modelList = await ExecuteCommandAsync("ollama list");
                }

                // Split the output and skip the header line
                string[] models = modelList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Skip(1) // Skip header
                                           .Select(line => line.Split(new[] { ' ' }, 2)[0]) // Extract model name
                                           .ToArray();

                ModelListBox.Items.Clear();
                foreach (string model in models)
                {
                    ModelListBox.Items.Add(model);
                }

                if (ModelListBox.Items.Count > 0)
                {
                    ModelListBox.SelectedIndex = 0;
                }

                _diagnostics.Log(DiagnosticLevel.Info, "SideMenuWindow",
                    $"Populated model list with {models.Length} models");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "SideMenuWindow",
                    $"Error loading models: {ex.Message}");
                MessageBox.Show($"Error loading models: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Legacy method for use when CommandLineService is not available
        private async Task<string> ExecuteCommandAsync(string command)
        {
            try
            {
                var (output, error) = await CommandLineUtil.ExecuteCommandAsync("cmd.exe", $"/C {command}");

                if (!string.IsNullOrEmpty(error))
                {
                    throw new Exception($"Command execution error: {error}");
                }

                return output;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to execute command: {ex.Message}");
            }
        }

        private void ModelListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ModelListBox.SelectedItem != null)
            {
                SelectedModel = ModelListBox.SelectedItem.ToString();
                _diagnostics.Log(DiagnosticLevel.Debug, "SideMenuWindow", $"Selected model: {SelectedModel}");
            }
        }

        private void LoadDocumentButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Select a document to load"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string fileName = openFileDialog.FileName;
                    LoadedDocument = File.ReadAllText(fileName);
                    _diagnostics.Log(DiagnosticLevel.Info, "SideMenuWindow",
                        $"Document loaded: {Path.GetFileName(fileName)}");
                    MessageBox.Show($"Document loaded: {Path.GetFileName(fileName)}", "Document Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "SideMenuWindow",
                        $"Error loading document: {ex.Message}");
                    MessageBox.Show($"Error loading document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RagButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future RAG implementation
            _diagnostics.Log(DiagnosticLevel.Info, "SideMenuWindow", "RAG button clicked");
            MessageBox.Show("RAG functionality not implemented yet.", "Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}