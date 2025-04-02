// ollamidesk/RAG/Models/DocumentMetadata.cs
// New file
using System;
using System.Collections.Generic;

namespace ollamidesk.RAG.Models
{
    /// <summary>
    /// Represents the metadata of a document, excluding its full content and chunks.
    /// </summary>
    public class DocumentMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; }
        public bool IsProcessed { get; set; }
        public bool IsSelected { get; set; }
        public long FileSize { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        // Keep track if embeddings exist, without loading them
        public bool HasEmbeddings { get; set; }
    }
}