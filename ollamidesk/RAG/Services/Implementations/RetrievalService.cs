using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the retrieval service
    /// </summary>
    public class RetrievalService : IRetrievalService
    {
        private readonly IVectorStore _vectorStore;
        private readonly IEmbeddingService _embeddingService;
        private readonly RagDiagnosticsService _diagnostics;
        private readonly float _minSimilarityScore;
        private readonly int _maxRetrievedChunks;

        public RetrievalService(
            IVectorStore vectorStore,
            IEmbeddingService embeddingService,
            RagSettings ragSettings,
            RagDiagnosticsService diagnostics)
        {
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (ragSettings == null)
                throw new ArgumentNullException(nameof(ragSettings));

            _minSimilarityScore = ragSettings.MinSimilarityScore;
            _maxRetrievedChunks = ragSettings.MaxRetrievedChunks;

            _diagnostics.Log(DiagnosticLevel.Info, "RetrievalService",
                $"Initialized RetrievalService with min similarity score: {_minSimilarityScore}, max chunks: {_maxRetrievedChunks}");
        }

        public async Task<List<(DocumentChunk Chunk, float Score)>> RetrieveRelevantChunksAsync(
            string query,
            List<string> documentIds,
            int maxResults = 4)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query cannot be empty", nameof(query));
            }

            if (documentIds == null || documentIds.Count == 0)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "RetrievalService", "No documents selected for retrieval");
                return new List<(DocumentChunk, float)>();
            }

            _diagnostics.StartOperation("RetrieveRelevantChunks");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "RetrievalService",
                    $"Retrieving chunks for query: \"{(query.Length > 50 ? query.Substring(0, 47) + "..." : query)}\"");

                _diagnostics.Log(DiagnosticLevel.Info, "RetrievalService",
                    $"Selected document IDs: {string.Join(", ", documentIds)}");

                // Use the provided maxResults if specified, otherwise use configured value
                int effectiveMaxResults = maxResults > 0 ? maxResults : _maxRetrievedChunks;

                // Generate embedding for query
                _diagnostics.StartOperation("QueryEmbeddingGeneration");
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                _diagnostics.EndOperation("QueryEmbeddingGeneration");

                // Use document-first search approach
                _diagnostics.StartOperation("VectorSearch");
                var searchResults = await _vectorStore.SearchInDocumentsAsync(
                    queryEmbedding,
                    documentIds,
                    effectiveMaxResults * 2); // Get more than needed
                _diagnostics.EndOperation("VectorSearch");

                // Filter by minimum similarity score
                searchResults = searchResults
                    .Where(r => r.Score >= _minSimilarityScore) // Filter out low similarity scores
                    .Take(effectiveMaxResults)
                    .ToList();

                // Log the retrieved chunks and their scores
                _diagnostics.LogRetrievedChunks("RetrievalService", query, searchResults);

                return searchResults;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RetrievalService",
                    $"Error retrieving relevant chunks: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("RetrieveRelevantChunks");
            }
        }

        public async Task<float> CalculateRelevanceScoreAsync(string query, DocumentChunk chunk)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query cannot be empty", nameof(query));
            }

            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            try
            {
                _diagnostics.StartOperation("CalculateRelevanceScore");

                // Generate query embedding
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

                // If chunk doesn't have embedding, generate it
                if (chunk.Embedding == null || chunk.Embedding.Length == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "RetrievalService",
                        $"Chunk {chunk.Id} doesn't have embedding, generating it now");
                    chunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
                }

                // Calculate cosine similarity
                float similarity = CalculateCosineSimilarity(queryEmbedding, chunk.Embedding);

                _diagnostics.LogSimilarityScore("RetrievalService", chunk.Id, similarity);

                return similarity;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "RetrievalService",
                    $"Error calculating relevance score: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("CalculateRelevanceScore");
            }
        }

        private float CalculateCosineSimilarity(float[] v1, float[] v2)
        {
            if (v1 == null || v2 == null || v1.Length != v2.Length || v1.Length == 0)
                return 0;

            float dotProduct = 0;
            float magnitude1 = 0;
            float magnitude2 = 0;

            for (int i = 0; i < v1.Length; i++)
            {
                dotProduct += v1[i] * v2[i];
                magnitude1 += v1[i] * v1[i];
                magnitude2 += v2[i] * v2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (magnitude1 * magnitude2);
        }
    }
}