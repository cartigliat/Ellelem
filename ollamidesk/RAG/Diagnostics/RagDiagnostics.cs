using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Diagnostics
{
    public enum DiagnosticLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public class RagDiagnostics
    {
        private static readonly Lazy<RagDiagnostics> _instance = new Lazy<RagDiagnostics>(() => new RagDiagnostics());
        public static RagDiagnostics Instance => _instance.Value;

        private readonly string _logFilePath;
        private readonly object _lockObj = new object();
        private bool _isEnabled = false;
        private DiagnosticLevel _minLevel = DiagnosticLevel.Info;
        
        // Performance metrics
        private readonly Dictionary<string, DateTime> _operationStartTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, List<TimeSpan>> _operationDurations = new Dictionary<string, List<TimeSpan>>();

        private RagDiagnostics()
        {
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OllamaDesk",
                "rag_diagnostics.log");
                
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            
            // Clear log on startup
            try
            {
                File.WriteAllText(_logFilePath, $"=== RAG Diagnostics Log Started at {DateTime.Now} ===\n\n");
            }
            catch (IOException)
            {
                // Log file might be locked, we'll append later
            }
        }

        public void Enable(DiagnosticLevel minLevel = DiagnosticLevel.Info)
        {
            _isEnabled = true;
            _minLevel = minLevel;
            Log(DiagnosticLevel.Info, "RagDiagnostics", "Diagnostics enabled");
        }

        public void Disable()
        {
            Log(DiagnosticLevel.Info, "RagDiagnostics", "Diagnostics disabled");
            _isEnabled = false;
        }

        public void Log(DiagnosticLevel level, string component, string message)
        {
            if (!_isEnabled || level < _minLevel)
                return;

            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{component}] {message}";
            
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // If we can't write to file, at least output to debug console
                    System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
            }
        }

        public void LogEmbeddingVector(string component, string text, float[] embedding, int maxElements = 5)
        {
            if (!_isEnabled || DiagnosticLevel.Debug < _minLevel)
                return;

            var truncatedText = text.Length > 50 ? text.Substring(0, 47) + "..." : text;
            var previewElements = embedding.Take(maxElements).Select(e => e.ToString("F4")).ToArray();
            
            Log(DiagnosticLevel.Debug, component, 
                $"Generated embedding for: \"{truncatedText}\" => Vector: [{string.Join(", ", previewElements)}...] (length: {embedding.Length})");
        }

        public void LogSimilarityScore(string component, string chunkId, float score)
        {
            if (!_isEnabled || DiagnosticLevel.Debug < _minLevel)
                return;
                
            Log(DiagnosticLevel.Debug, component, $"Similarity score for chunk {chunkId}: {score:F4}");
        }

        public void LogDocumentChunking(string component, string documentId, List<DocumentChunk> chunks)
        {
            if (!_isEnabled || DiagnosticLevel.Info < _minLevel)
                return;
                
            Log(DiagnosticLevel.Info, component, 
                $"Document {documentId} chunked into {chunks.Count} pieces. First chunk preview: \"{PreviewText(chunks.FirstOrDefault()?.Content)}\"");
        }

        public void LogRetrievedChunks(string component, string query, List<(DocumentChunk Chunk, float Score)> chunks)
        {
            if (!_isEnabled || DiagnosticLevel.Info < _minLevel)
                return;
                
            Log(DiagnosticLevel.Info, component, $"Query: \"{PreviewText(query)}\"");
            Log(DiagnosticLevel.Info, component, $"Retrieved {chunks.Count} chunks:");
            
            foreach (var (chunk, score) in chunks)
            {
                Log(DiagnosticLevel.Info, component, 
                    $" - Chunk {chunk.Id} (score: {score:F4}): \"{PreviewText(chunk.Content)}\"");
            }
        }

        public void LogApiRequest(string component, string endpoint, string requestData)
        {
            if (!_isEnabled || DiagnosticLevel.Debug < _minLevel)
                return;
                
            Log(DiagnosticLevel.Debug, component, 
                $"API Request to {endpoint}: {PreviewText(requestData)}");
        }

        public void LogApiResponse(string component, string endpoint, string responseData, bool isSuccess)
        {
            if (!_isEnabled || DiagnosticLevel.Debug < _minLevel)
                return;
                
            var status = isSuccess ? "SUCCESS" : "FAILED";
            Log(DiagnosticLevel.Debug, component, 
                $"API Response from {endpoint} ({status}): {PreviewText(responseData)}");
        }

        public void StartOperation(string operationName)
        {
            if (!_isEnabled)
                return;
                
            lock (_lockObj)
            {
                _operationStartTimes[operationName] = DateTime.Now;
            }
            Log(DiagnosticLevel.Debug, "Performance", $"Starting operation: {operationName}");
        }

        public void EndOperation(string operationName)
        {
            if (!_isEnabled)
                return;
                
            lock (_lockObj)
            {
                if (_operationStartTimes.TryGetValue(operationName, out var startTime))
                {
                    var duration = DateTime.Now - startTime;
                    
                    if (!_operationDurations.ContainsKey(operationName))
                    {
                        _operationDurations[operationName] = new List<TimeSpan>();
                    }
                    
                    _operationDurations[operationName].Add(duration);
                    
                    Log(DiagnosticLevel.Debug, "Performance", 
                        $"Operation {operationName} completed in {duration.TotalMilliseconds:F2}ms");
                }
            }
        }

        public string GeneratePerformanceReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== RAG Performance Report ===");
            
            lock (_lockObj)
            {
                foreach (var operation in _operationDurations)
                {
                    var durations = operation.Value;
                    var avgTime = durations.Average(d => d.TotalMilliseconds);
                    var maxTime = durations.Max(d => d.TotalMilliseconds);
                    var minTime = durations.Min(d => d.TotalMilliseconds);
                    var count = durations.Count;
                    
                    report.AppendLine($"Operation: {operation.Key}");
                    report.AppendLine($"  Count: {count}");
                    report.AppendLine($"  Avg Time: {avgTime:F2}ms");
                    report.AppendLine($"  Min Time: {minTime:F2}ms");
                    report.AppendLine($"  Max Time: {maxTime:F2}ms");
                    report.AppendLine();
                }
            }
            
            return report.ToString();
        }

        public async Task<string> GetFullLogContentsAsync()
        {
            try
            {
                return await File.ReadAllTextAsync(_logFilePath);
            }
            catch (Exception ex)
            {
                return $"Error reading log file: {ex.Message}";
            }
        }

        private string PreviewText(string? text, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";
                
            return text.Length > maxLength ? text.Substring(0, maxLength - 3) + "..." : text;
        }
    }
}