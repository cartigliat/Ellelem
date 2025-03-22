using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Services.Implementations;

namespace ollamidesk.RAG.Diagnostics
{
    public partial class RagDiagnosticWindow : Window
    {
        private DispatcherTimer _logUpdateTimer;
        private DispatcherTimer _perfUpdateTimer;
        private string _lastLogContent = string.Empty;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IEmbeddingService _embeddingService;
        private readonly IOllamaModel _ollamaModel;
        private readonly IVectorStore _vectorStore;
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;

        // Constructor with DI
        public RagDiagnosticWindow(
            RagDiagnosticsService diagnostics,
            IEmbeddingService embeddingService,
            IOllamaModel ollamaModel,
            IVectorStore vectorStore,
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService)
        {
            InitializeComponent();

            // Initialize timer fields to avoid nullable warnings
            _logUpdateTimer = null!;
            _perfUpdateTimer = null!;

            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _ollamaModel = ollamaModel ?? throw new ArgumentNullException(nameof(ollamaModel));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));

            InitializeTimers();

            StatusText.Text = "Connected to application services via DI";
        }

        // Rest of the class remains unchanged
        private void InitializeTimers()
        {
            // Setup timers for log updates
            _logUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;

            _perfUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _perfUpdateTimer.Tick += PerfUpdateTimer_Tick;
        }

        private async void LogUpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                string logContent = await _diagnostics.GetFullLogContentsAsync();
                if (logContent != _lastLogContent)
                {
                    LogTextBox.Text = logContent;
                    LogTextBox.ScrollToEnd();
                    _lastLogContent = logContent;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error updating log: " + ex.Message;
            }
        }

        private void PerfUpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                string perfReport = _diagnostics.GeneratePerformanceReport();
                PerformanceTextBox.Text = perfReport;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error updating performance report: " + ex.Message;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get selected log level
                DiagnosticLevel level = DiagnosticLevel.Info;
                if (LevelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string levelText = selectedItem.Content.ToString() ?? "Info";
                    level = (DiagnosticLevel)Enum.Parse(typeof(DiagnosticLevel), levelText);
                }

                // Enable diagnostics
                _diagnostics.Enable(level);

                // Update UI
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                LevelComboBox.IsEnabled = false;

                // Start log update timer
                _logUpdateTimer.Start();
                _perfUpdateTimer.Start();

                StatusText.Text = $"Diagnostics started with level: {level}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting diagnostics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable diagnostics
                _diagnostics.Disable();

                // Update UI
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                LevelComboBox.IsEnabled = true;

                // Stop log update timer
                _logUpdateTimer.Stop();
                _perfUpdateTimer.Stop();

                StatusText.Text = "Diagnostics stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping diagnostics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogTextBox.Clear();
                _lastLogContent = string.Empty;

                // Also clear the on-disk log by restarting diagnostics
                if (StopButton.IsEnabled)
                {
                    // Diagnostics are currently enabled, restart them
                    _diagnostics.Disable();

                    // Get selected log level
                    DiagnosticLevel level = DiagnosticLevel.Info;
                    if (LevelComboBox.SelectedItem is ComboBoxItem selectedItem)
                    {
                        string levelText = selectedItem.Content.ToString() ?? "Info";
                        level = (DiagnosticLevel)Enum.Parse(typeof(DiagnosticLevel), levelText);
                    }

                    _diagnostics.Enable(level);
                    StatusText.Text = "Log cleared and diagnostics restarted";
                }
                else
                {
                    StatusText.Text = "Log cleared";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".log",
                    FileName = $"ollamidesk_rag_diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.log"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string logContent = await _diagnostics.GetFullLogContentsAsync();
                    await File.WriteAllTextAsync(saveFileDialog.FileName, logContent);

                    // Also save performance report
                    string perfReport = _diagnostics.GeneratePerformanceReport();
                    string perfFileName = Path.Combine(
                        Path.GetDirectoryName(saveFileDialog.FileName) ?? "",
                        Path.GetFileNameWithoutExtension(saveFileDialog.FileName) + "_performance" + Path.GetExtension(saveFileDialog.FileName));
                    await File.WriteAllTextAsync(perfFileName, perfReport);

                    StatusText.Text = $"Log saved to {saveFileDialog.FileName}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestEmbeddingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestEmbeddingButton.IsEnabled = false;
                EmbeddingStatusText.Text = "Testing...";
                TestResultsTextBox.Text = "Testing embedding service...";

                var results = new StringBuilder();
                results.AppendLine("Embedding Service Test Results:");
                results.AppendLine();

                // Test connection
                if (_embeddingService is OllamaEmbeddingService ollamaEmbedding)
                {
                    bool connectionResult = await ollamaEmbedding.TestConnectionAsync();
                    results.AppendLine($"Connection Test: {(connectionResult ? "SUCCESS" : "FAILED")}");

                    if (!connectionResult)
                    {
                        EmbeddingStatusText.Text = "FAILED: Connection error";
                        TestResultsTextBox.Text = results.ToString();
                        return;
                    }
                }

                // Test generating an embedding
                try
                {
                    string testText = "This is a test of the embedding service";
                    results.AppendLine($"Generating embedding for: \"{testText}\"");

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(testText);

                    results.AppendLine($"  Result: Vector of length {embedding.Length}");
                    results.AppendLine($"  First 5 values: {string.Join(", ", embedding.Take(5).Select(v => v.ToString("F4")))}");

                    EmbeddingStatusText.Text = "SUCCESS";
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  ERROR: {ex.Message}");
                    EmbeddingStatusText.Text = "FAILED: " + ex.Message;
                }

                TestResultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                EmbeddingStatusText.Text = "ERROR: " + ex.Message;
                TestResultsTextBox.Text += "\nUnexpected error: " + ex.Message;
            }
            finally
            {
                TestEmbeddingButton.IsEnabled = true;
            }
        }

        private async void TestModelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestModelButton.IsEnabled = false;
                ModelStatusText.Text = "Testing...";
                TestResultsTextBox.Text = "Testing Ollama model...";

                var results = new StringBuilder();
                results.AppendLine("Ollama Model Test Results:");
                results.AppendLine();

                // Test connection
                if (_ollamaModel is OllamaModel ollamaModel)
                {
                    bool connectionResult = await ollamaModel.TestConnectionAsync();
                    results.AppendLine($"Connection Test: {(connectionResult ? "SUCCESS" : "FAILED")}");

                    if (!connectionResult)
                    {
                        ModelStatusText.Text = "FAILED: Connection error";
                        TestResultsTextBox.Text = results.ToString();
                        return;
                    }
                }

                // Test generating a simple response
                try
                {
                    string testPrompt = "What is 2+2? Answer with just the number.";
                    results.AppendLine($"Generating response for: \"{testPrompt}\"");

                    var response = await _ollamaModel.GenerateResponseAsync(testPrompt, "", new List<string>());

                    results.AppendLine($"  Result: {response}");

                    ModelStatusText.Text = "SUCCESS";
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  ERROR: {ex.Message}");
                    ModelStatusText.Text = "FAILED: " + ex.Message;
                }

                TestResultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                ModelStatusText.Text = "ERROR: " + ex.Message;
                TestResultsTextBox.Text += "\nUnexpected error: " + ex.Message;
            }
            finally
            {
                TestModelButton.IsEnabled = true;
            }
        }

        private async void TestVectorStoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestVectorStoreButton.IsEnabled = false;
                VectorStoreStatusText.Text = "Testing...";
                TestResultsTextBox.Text = "Testing vector store...";

                var results = new StringBuilder();
                results.AppendLine("Vector Store Test Results:");
                results.AppendLine();

                // Create a test document and embedding
                try
                {
                    const string testDocId = "test-vector-store-doc";

                    // First, make sure any previous test document is removed
                    await _vectorStore.RemoveVectorsAsync(testDocId);

                    // Create test chunks
                    var testChunks = new List<DocumentChunk>();
                    string[] testTexts = {
                        "This is a test chunk about dogs. Dogs are pets that people have at home.",
                        "Cats are also common pets that people have at home. They meow and purr.",
                        "Python is a popular programming language used in data science and AI.",
                        "C# is a programming language commonly used for Windows applications."
                    };

                    results.AppendLine("Creating test document chunks:");

                    // Generate embeddings and add chunks
                    for (int i = 0; i < testTexts.Length; i++)
                    {
                        results.AppendLine($"  - Processing chunk {i + 1}: \"{testTexts[i].Substring(0, Math.Min(30, testTexts[i].Length))}...\"");

                        var embedding = await _embeddingService.GenerateEmbeddingAsync(testTexts[i]);

                        var chunk = new DocumentChunk
                        {
                            Id = $"test-chunk-{i}",
                            DocumentId = testDocId,
                            Content = testTexts[i],
                            ChunkIndex = i,
                            Embedding = embedding,
                            Source = "Test"
                        };

                        testChunks.Add(chunk);
                    }

                    // Add to vector store
                    results.AppendLine("Adding chunks to vector store...");
                    await _vectorStore.AddVectorsAsync(testChunks);

                    // Test search
                    results.AppendLine("\nTesting search with different queries:");

                    // Test queries
                    string[] testQueries = {
                        "Tell me about pets",
                        "Programming languages",
                        "This is a totally unrelated query about space travel"
                    };

                    foreach (var query in testQueries)
                    {
                        results.AppendLine($"\nQuery: \"{query}\"");

                        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                        var searchResults = await _vectorStore.SearchAsync(queryEmbedding, 2);

                        results.AppendLine($"  Found {searchResults.Count} results:");

                        foreach (var (chunk, score) in searchResults)
                        {
                            results.AppendLine($"  - Score: {score:F4}, Content: \"{chunk.Content.Substring(0, Math.Min(30, chunk.Content.Length))}...\"");
                        }
                    }

                    // Clean up
                    results.AppendLine("\nCleaning up test data...");
                    await _vectorStore.RemoveVectorsAsync(testDocId);

                    VectorStoreStatusText.Text = "SUCCESS";
                }
                catch (Exception ex)
                {
                    results.AppendLine($"\nERROR: {ex.Message}");
                    VectorStoreStatusText.Text = "FAILED: " + ex.Message;
                }

                TestResultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                VectorStoreStatusText.Text = "ERROR: " + ex.Message;
                TestResultsTextBox.Text += "\nUnexpected error: " + ex.Message;
            }
            finally
            {
                TestVectorStoreButton.IsEnabled = true;
            }
        }

        private async void TestChunkingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestChunkingButton.IsEnabled = false;
                ChunkingStatusText.Text = "Testing...";
                TestResultsTextBox.Text = "Testing document chunking...";

                var results = new StringBuilder();
                results.AppendLine("Document Chunking Test Results:");
                results.AppendLine();

                // Create a test document
                string fileName = Path.GetTempFileName();
                try
                {
                    // Write test content to file
                    string testContent = @"# Test Document

This is a sample document used to test the chunking functionality.

## Section 1

This is the first section of our document. It contains some example text
that will be used to test how the chunking works with different types of content.

## Section 2

This section includes:
- A bullet list
- With multiple items
- And sub-items
  - Like this one
  - And this one

## Section 3

```csharp
// This is a code block
public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""Hello, World!"");
    }
}
```

## Final Section

This is the final section of our test document.
It should be processed correctly by the chunking algorithm.";

                    await File.WriteAllTextAsync(fileName, testContent);

                    // Test adding and processing the document
                    results.AppendLine($"Adding test document: {Path.GetFileName(fileName)}");
                    var document = await _documentManagementService.AddDocumentAsync(fileName);

                    results.AppendLine($"Document added with ID: {document.Id}");
                    results.AppendLine($"Processing document...");

                    document = await _documentProcessingService.ProcessDocumentAsync(document);

                    results.AppendLine($"Document processed. Generated {document.Chunks.Count} chunks:");

                    foreach (var chunk in document.Chunks)
                    {
                        string preview = chunk.Content.Length > 50
                            ? chunk.Content.Substring(0, 47) + "..."
                            : chunk.Content;

                        results.AppendLine($"  - Chunk {chunk.ChunkIndex}: \"{preview}\"");
                    }

                    // Clean up
                    results.AppendLine("\nCleaning up test document...");
                    await _documentManagementService.DeleteDocumentAsync(document.Id);

                    ChunkingStatusText.Text = "SUCCESS";
                }
                catch (Exception ex)
                {
                    results.AppendLine($"\nERROR: {ex.Message}");
                    ChunkingStatusText.Text = "FAILED: " + ex.Message;
                }
                finally
                {
                    // Delete the temp file
                    try
                    {
                        if (File.Exists(fileName))
                        {
                            File.Delete(fileName);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }

                TestResultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                ChunkingStatusText.Text = "ERROR: " + ex.Message;
                TestResultsTextBox.Text += "\nUnexpected error: " + ex.Message;
            }
            finally
            {
                TestChunkingButton.IsEnabled = true;
            }
        }
    }
}