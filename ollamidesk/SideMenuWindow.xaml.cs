using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ollamidesk
{
    public partial class SideMenuWindow : Window
    {
        public string? SelectedModel { get; private set; }
        public string? LoadedDocument { get; private set; }

        public SideMenuWindow()
        {
            InitializeComponent();
            PopulateModelListAsync();
        }

        private async void PopulateModelListAsync()
        {
            try
            {
                string modelList = await ExecuteCommandAsync("ollama list");

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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading models: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                    MessageBox.Show($"Document loaded: {Path.GetFileName(fileName)}", "Document Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading document: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RagButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future RAG implementation
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