using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Facade for the RAG service that maintains backward compatibility with the original RagService
    /// while delegating to the new specialized services.
    /// 
    /// This class is provided for backward compatibility and will be deprecated in the future.
    /// New code should use the specialized services directly.
    /// </summary>
    public class RagServiceFacade
    {
        private readonly IDocumentManagementService _documentManagementService;
        private readonly IDocumentProcessingService _documentProcessingService;
        private readonly IRetrievalService _retrievalService;
        private readonly IPromptEngineeringService _promptEngineeringService;
        private readonly RagDiagnosticsService _diagnostics;

        public RagServiceFacade(
            IDocumentManagementService documentManagementService,
            IDocumentProcessingService documentProcessingService,
            IRetrievalService retrievalService,
            IPromptEngineeringService promptEngineeringService,
            RagDiagnosticsService diagnostics)
        {
            _documentManagementService = documentManagementService ?? throw new ArgumentNullException(nameof(documentManagementService));
            _documentProcessingService = documentProcessingService ?? throw new ArgumentNullException(nameof(documentProcessingService));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
            _promptEngineeringService = promptEngineeringService ?? throw new ArgumentNullException(nameof(promptEngineeringService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _diagnostics.Log(DiagnosticLevel.Info, "RagServiceFacade", "Initialized RagService facade");
        }

        /// <summary>
        /// Gets all documents in the repository.
        /// </summary>
        /// <returns>List of documents</returns>
        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            return await _documentManagementService.GetAllDocumentsAsync();
        }

        /// <summary>
        /// Adds a document from a file path.
        /// </summary>
        /// <param name="filePath">Path to the document file</param>
        /// <param name="loadFullContent">Whether to load the full content for large files</param>
        /// <returns>The added document</returns>
        public async Task<Document> AddDocumentAsync(string filePath, bool loadFullContent = false)
        {
            return await _documentManagementService.AddDocumentAsync(filePath, loadFullContent);
        }

        /// <summary>
        /// Processes a document (chunking and embedding).
        /// </summary>
        /// <param name="documentId">ID of the document to process</param>
        /// <returns>The processed document</returns>
        public async Task<Document> ProcessDocumentAsync(string documentId)
        {
            // First, get the document by ID
            var document = await _documentManagementService.GetDocumentAsync(documentId);
            if (document == null)
            {
                throw new ArgumentException($"Document not found: {documentId}");
            }

            // Then, process the document
            return await _documentProcessingService.ProcessDocumentAsync(document);
        }

        /// <summary>
        /// Generates an augmented prompt from a query and relevant document chunks.
        /// </summary>
        /// <param name="query">The query to augment</param>
        /// <param name="selectedDocumentIds">List of selected document IDs</param>
        /// <returns>The augmented prompt and list of source chunks</returns>
        public async Task<(string augmentedPrompt, List<DocumentChunk> sources)> GenerateAugmentedPromptAsync(
            string query, List<string> selectedDocumentIds)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query cannot be empty", nameof(query));
            }

            if (selectedDocumentIds == null || selectedDocumentIds.Count == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "RagServiceFacade", "No documents selected for retrieval");
                return (query, new List<DocumentChunk>());
            }

            _diagnostics.StartOperation("GenerateAugmentedPrompt");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "RagServiceFacade",
                    $"Generating augmented prompt for query: \"{(query.Length > 50 ? query.Substring(0, 47) + "..." : query)}\"");

                // Retrieve relevant chunks
                var searchResults = await _retrievalService.RetrieveRelevantChunksAsync(query, selectedDocumentIds);

                if (searchResults.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RagServiceFacade",
                        "No relevant chunks found for the query");
                    return (query, new List<DocumentChunk>());
                }

                // Extract chunks from search results
                var relevantChunks = new List<DocumentChunk>();
                foreach (var (chunk, _) in searchResults)
                {
                    relevantChunks.Add(chunk);
                }

                // Create augmented prompt
                string augmentedPrompt = await _promptEngineeringService.CreateAugmentedPromptAsync(query, relevantChunks);

                _diagnostics.Log(DiagnosticLevel.Info, "RagServiceFacade",
                    $"Generated augmented prompt with {relevantChunks.Count} chunks (total length: {augmentedPrompt.Length} chars)");

                return (augmentedPrompt, relevantChunks);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RagServiceFacade",
                    $"Failed to generate augmented prompt: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("GenerateAugmentedPrompt");
            }
        }

        /// <summary>
        /// Deletes a document from the repository.
        /// </summary>
        /// <param name="documentId">ID of the document to delete</param>
        public async Task DeleteDocumentAsync(string documentId)
        {
            await _documentManagementService.DeleteDocumentAsync(documentId);
        }

        /// <summary>
        /// Updates the selection state of a document.
        /// </summary>
        /// <param name="documentId">ID of the document to update</param>
        /// <param name="isSelected">Whether the document is selected</param>
        public async Task UpdateDocumentSelectionAsync(string documentId, bool isSelected)
        {
            await _documentManagementService.UpdateDocumentSelectionAsync(documentId, isSelected);
        }
    }
}