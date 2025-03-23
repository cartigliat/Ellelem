using System;
using System.Collections.Generic;
using System.Linq;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Factory for creating document processors based on file extension
    /// </summary>
    public class DocumentProcessorFactory
    {
        private readonly IEnumerable<IDocumentProcessor> _processors;
        private readonly RagDiagnosticsService _diagnostics;

        public DocumentProcessorFactory(
            IEnumerable<IDocumentProcessor> processors,
            RagDiagnosticsService diagnostics)
        {
            _processors = processors ?? throw new ArgumentNullException(nameof(processors));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Gets a document processor for the specified file extension
        /// </summary>
        public IDocumentProcessor GetProcessor(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension))
                throw new ArgumentException("File extension cannot be empty", nameof(fileExtension));

            string normalizedExtension = fileExtension.ToLowerInvariant();
            if (!normalizedExtension.StartsWith("."))
                normalizedExtension = "." + normalizedExtension;

            var processor = _processors.FirstOrDefault(p => p.CanProcess(normalizedExtension));

            if (processor == null)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "DocumentProcessorFactory",
                    $"No processor found for file extension: {normalizedExtension}");
                throw new NotSupportedException($"File type {normalizedExtension} is not supported");
            }

            return processor;
        }

        /// <summary>
        /// Gets all supported file extensions
        /// </summary>
        public string[] GetSupportedExtensions()
        {
            return _processors
                .SelectMany(p => p.SupportedExtensions)
                .Distinct()
                .OrderBy(ext => ext)
                .ToArray();
        }
    }
}