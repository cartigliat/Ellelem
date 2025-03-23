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
        public bool IsSelected { get; set; }
        public long FileSize { get; set; } // New property for file size
        public bool IsLargeFile => FileSize > 10 * 1024 * 1024; // 10MB threshold
        public bool IsContentTruncated { get; set; } // Flag indicating if content is just a preview
        public List<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();

        // Added property for document type
        public string DocumentType { get; set; } = string.Empty;

        // Added property for document metadata
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class DocumentChunk
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DocumentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public string Source { get; set; } = string.Empty;

        // New property for metadata about the chunk
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        // New property for the chunk type/category
        public string ChunkType { get; set; } = "text";

        // New property for heading level (if the chunk is a heading)
        public int? HeadingLevel { get; set; }

        // New property for section path in the document structure
        public string SectionPath { get; set; } = string.Empty;
    }
}