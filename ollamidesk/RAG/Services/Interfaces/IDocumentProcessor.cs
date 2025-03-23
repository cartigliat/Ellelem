using System.Collections.Generic;
using System.Threading.Tasks;

namespace ollamidesk.RAG.DocumentProcessors.Interfaces
{
    /// <summary>
    /// Interface for document processors that extract text from different file types
    /// </summary>
    public interface IDocumentProcessor
    {
        /// <summary>
        /// Checks if this processor can handle the specified file extension
        /// </summary>
        bool CanProcess(string fileExtension);

        /// <summary>
        /// Gets the list of file extensions this processor supports
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// Extracts text content from a document file
        /// </summary>
        /// <param name="filePath">Path to the document file</param>
        /// <returns>The extracted text content</returns>
        Task<string> ExtractTextAsync(string filePath);

        /// <summary>
        /// Gets whether this processor can extract structured information
        /// </summary>
        bool SupportsStructuredExtraction { get; }

        /// <summary>
        /// Extracts structured text with metadata about document elements
        /// </summary>
        /// <param name="filePath">Path to the document file</param>
        /// <returns>Structured document representation</returns>
        Task<StructuredDocument> ExtractStructuredContentAsync(string filePath);
    }

    /// <summary>
    /// Represents structured content from a document
    /// </summary>
    public class StructuredDocument
    {
        public string Title { get; set; } = string.Empty;
        public List<DocumentElement> Elements { get; set; } = new List<DocumentElement>();

        public string ToPlainText()
        {
            // Convert structured document to plain text
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(Title))
            {
                sb.AppendLine(Title);
                sb.AppendLine(new string('=', Title.Length));
                sb.AppendLine();
            }

            foreach (var element in Elements)
            {
                sb.AppendLine(element.Text);
                if (element.Type != ElementType.Paragraph)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Type of document element
    /// </summary>
    public enum ElementType
    {
        Heading1,
        Heading2,
        Heading3,
        Paragraph,
        ListItem,
        CodeBlock,
        Table,
        Image,
        Quote
    }

    /// <summary>
    /// Represents a single element in a document
    /// </summary>
    public class DocumentElement
    {
        public ElementType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public int? HeadingLevel { get; set; }
        public string SectionPath { get; set; } = string.Empty;
    }
}