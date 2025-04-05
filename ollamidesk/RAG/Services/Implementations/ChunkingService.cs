// ollamidesk/RAG/Services/Implementations/ChunkingService.cs
// MODIFIED VERSION - Accepts StructuredDocument and passes it to strategies
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // <-- ADDED for StructuredDocument

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the document chunking service using registered strategies.
    /// Prioritizes strategies that can handle structured documents when available.
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
            // It's generally better to let the DI container manage the order
            // or implement prioritization logic within ChunkDocumentAsync.
            // The order here is less critical now that specific strategies check for structuredDoc.
            _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
            _defaultStrategy = defaultStrategy ?? throw new ArgumentNullException(nameof(defaultStrategy));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Initialized with {_strategies.Count()} strategies.");
        }

        // <<< MODIFIED: Method signature updated >>>
        public Task<List<DocumentChunk>> ChunkDocumentAsync(Document document, StructuredDocument? structuredDoc = null)
        {
            _diagnostics.StartOperation("ChunkDocumentAsync");
            _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Starting chunking for document: {document.Id} ({document.DocumentType}). StructuredDoc provided: {structuredDoc != null}");

            if (document == null || string.IsNullOrWhiteSpace(document.Content))
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "ChunkingService", $"Empty content for document {document?.Id}");
                _diagnostics.EndOperation("ChunkDocumentAsync");
                return Task.FromResult(new List<DocumentChunk>());
            }

            List<DocumentChunk> chunks = new List<DocumentChunk>();
            bool strategyFound = false;

            try
            {
                // Iterate through strategies provided by DI.
                // Prioritize strategies that can use structuredDoc if it exists.
                // (DI registration order or more complex logic here can enforce strict order if needed)
                foreach (var strategy in _strategies)
                {
                    // <<< MODIFIED: Pass structuredDoc to CanChunk >>>
                    if (strategy.CanChunk(document, structuredDoc))
                    {
                        _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Strategy {strategy.GetType().Name} CanChunk=true. Attempting chunking for document {document.Id}.");

                        // <<< MODIFIED: Pass structuredDoc to Chunk >>>
                        chunks = strategy.Chunk(document, structuredDoc);
                        strategyFound = true; // Mark that a capable strategy was found

                        if (chunks.Count > 0)
                        {
                            _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"Strategy {strategy.GetType().Name} successfully created {chunks.Count} chunks for document {document.Id}.");
                            _diagnostics.LogDocumentChunking("ChunkingService", document.Id, chunks);
                            // Strategy succeeded, break the loop and return chunks
                            break;
                        }
                        else
                        {
                            _diagnostics.Log(DiagnosticLevel.Warning, "ChunkingService", $"Strategy {strategy.GetType().Name} was applicable but returned 0 chunks for document {document.Id}. Trying next strategy.");
                            strategyFound = false; // Reset flag as this strategy didn't produce output
                        }
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Debug, "ChunkingService", $"Strategy {strategy.GetType().Name} CanChunk=false for document {document.Id}.");
                    }
                }

                // If no specific strategy created chunks, potentially use the default (Text) strategy
                // Note: The default strategy (TextChunkingStrategy) might be included in _strategies itself.
                // This explicit fallback might be redundant if TextChunkingStrategy is always in the list
                // and its CanChunk always returns true. Check DI registration.
                // If _defaultStrategy is guaranteed to be in _strategies, this block might not be needed.
                if (!strategyFound && chunks.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Info, "ChunkingService", $"No specific strategy yielded chunks. Using default strategy: {_defaultStrategy.GetType().Name} for document {document.Id}");
                    // <<< MODIFIED: Pass structuredDoc to default strategy Chunk >>>
                    // Ensure TextChunkingStrategy.Chunk signature is also updated to accept structuredDoc (even if unused)
                    chunks = _defaultStrategy.Chunk(document, structuredDoc);
                    _diagnostics.LogDocumentChunking("ChunkingService", document.Id, chunks);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "ChunkingService", $"Error during chunking strategy execution for document {document.Id}: {ex.Message}");
                chunks = new List<DocumentChunk>(); // Ensure empty list on error
            }
            finally
            {
                _diagnostics.EndOperation("ChunkDocumentAsync");
            }

            return Task.FromResult(chunks);
        }
    }
}