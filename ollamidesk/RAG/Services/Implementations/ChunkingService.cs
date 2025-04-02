// ollamidesk/RAG/Services/Implementations/ChunkingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the document chunking service using registered strategies.
    /// </summary>
    public class ChunkingService : IChunkingService
    {
        private readonly IEnumerable<IChunkingStrategy> _strategies;
        private readonly TextChunkingStrategy _defaultStrategy; // Keep explicit default
        private readonly RagDiagnosticsService _diagnostics;

        // Inject all strategies and specifically the default Text strategy
        public ChunkingService(
            IEnumerable<IChunkingStrategy> strategies,
            TextChunkingStrategy defaultStrategy, // Ensure TextChunkingStrategy is registered itself
            RagDiagnosticsService diagnostics)
        {
            // Order strategies: Specific (Code, Structured) before Default (Text)
            _strategies = strategies?.OrderByDescending(s => s is TextChunkingStrategy ? 0 : 1) // Basic ordering
                          ?? throw new ArgumentNullException(nameof(strategies));
            _defaultStrategy = defaultStrategy ?? throw new ArgumentNullException(nameof(defaultStrategy));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Initialized with {_strategies.Count()} strategies.");
        }

        public Task<List<DocumentChunk>> ChunkDocumentAsync(Document document)
        {
            _diagnostics.StartOperation("ChunkDocumentAsync");
            _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Starting chunking for document: {document.Id} ({document.DocumentType})");

            if (document == null || string.IsNullOrWhiteSpace(document.Content))
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "ChunkingService", $"Empty content for document {document?.Id}");
                _diagnostics.EndOperation("ChunkDocumentAsync");
                return Task.FromResult(new List<DocumentChunk>());
            }

            // Initialize chunks here to prevent CS0165
            List<DocumentChunk> chunks = new List<DocumentChunk>();
            bool strategyFound = false;

            try
            {
                // Find the first applicable strategy (excluding the default one initially)
                foreach (var strategy in _strategies.Where(s => s != _defaultStrategy))
                {
                    if (strategy.CanChunk(document))
                    {
                        _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Using strategy: {strategy.GetType().Name} for document {document.Id}");
                        chunks = strategy.Chunk(document); // Assign the result here
                        strategyFound = true;

                        if (chunks.Count > 0)
                        {
                            _diagnostics.LogDocumentChunking("ChunkingService", document.Id, chunks);
                            // We have successful chunks, return them immediately
                            _diagnostics.EndOperation("ChunkDocumentAsync");
                            return Task.FromResult(chunks);
                        }
                        else
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "ChunkingService", $"Strategy {strategy.GetType().Name} returned 0 chunks for document {document.Id}. Falling back.");
                            strategyFound = false; // Reset flag to allow fallback
                            break; // Stop trying other specific strategies if one applied but yielded no chunks
                        }
                    }
                }

                // If no specific strategy applied or the applied one failed/yielded no chunks, use the default
                if (!strategyFound)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Using default strategy: {_defaultStrategy.GetType().Name} for document {document.Id}");
                    chunks = _defaultStrategy.Chunk(document); // Assign the result here
                    _diagnostics.LogDocumentChunking("ChunkingService", document.Id, chunks);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "ChunkingService", $"Error during chunking for document {document.Id}: {ex.Message}");
                // Ensure chunks is an empty list on error before returning
                chunks = new List<DocumentChunk>();
            }
            finally
            {
                _diagnostics.EndOperation("ChunkDocumentAsync");
            }

            // This return statement is now safe because 'chunks' is guaranteed to be assigned
            return Task.FromResult(chunks);
        }

        // --- REMOVED Private Chunking Methods ---
        // ChunkTextDocument, ChunkStructuredDocument, ChunkCodeDocument, SplitSectionIfTooLarge, SplitCodeOrTextChunkByLines
        // Logic is now within the strategy classes.
    }
}