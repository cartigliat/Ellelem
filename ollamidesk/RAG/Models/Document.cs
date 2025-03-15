using System;
using System.Collections.Generic;

namespace ollamidesk.RAG.Models
{
    public class Document
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public bool IsProcessed { get; set; }
        public List<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    }

    public class DocumentChunk
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DocumentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public string Source { get; set; } = string.Empty;
    }
}