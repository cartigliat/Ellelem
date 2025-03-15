using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ollamidesk.RAG.Models;

namespace ollamidesk.RAG.Services
{
    public class InMemoryVectorStore : IVectorStore
    {
        private readonly List<DocumentChunk> _chunks = new();
        private readonly object _lock = new();

        public Task AddVectorsAsync(List<DocumentChunk> chunks)
        {
            lock (_lock)
            {
                foreach (var chunk in chunks)
                {
                    // Remove any existing chunks with the same ID
                    _chunks.RemoveAll(c => c.Id == chunk.Id);
                    // Add the new chunk
                    _chunks.Add(chunk);
                }
            }
            return Task.CompletedTask;
        }

        public Task RemoveVectorsAsync(string documentId)
        {
            lock (_lock)
            {
                _chunks.RemoveAll(c => c.DocumentId == documentId);
            }
            return Task.CompletedTask;
        }

        public Task<List<(DocumentChunk Chunk, float Score)>> SearchAsync(float[] queryVector, int limit = 5)
        {
            if (queryVector == null || queryVector.Length == 0)
                return Task.FromResult(new List<(DocumentChunk, float)>());

            List<(DocumentChunk Chunk, float Score)> results;
            lock (_lock)
            {
                results = _chunks
                    .Select(chunk => (Chunk: chunk, Score: CosineSimilarity(queryVector, chunk.Embedding)))
                    .OrderByDescending(x => x.Score)
                    .Take(limit)
                    .ToList();
            }

            return Task.FromResult(results);
        }

        private float CosineSimilarity(float[] v1, float[] v2)
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