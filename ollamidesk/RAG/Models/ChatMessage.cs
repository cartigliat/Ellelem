using System.Collections.Generic;

namespace ollamidesk.RAG.Models
{
    public class ChatMessage
    {
        public string UserQuery { get; set; } = string.Empty;
        public string ModelResponse { get; set; } = string.Empty;
        public List<string> SourceChunkIds { get; set; } = new List<string>();
        public bool UsedRag { get; set; }
    }
}