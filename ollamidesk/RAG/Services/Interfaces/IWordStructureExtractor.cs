// ollamidesk/RAG/Services/Interfaces/IWordStructureExtractor.cs
using DocumentFormat.OpenXml.Wordprocessing;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // For StructuredDocument (assuming this is needed by StructuredDocument)

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for extracting structured content from the body of a Word document.
    /// </summary>
    public interface IWordStructureExtractor
    {
        /// <summary>
        /// Processes the body of a Word document to extract structured elements.
        /// </summary>
        /// <param name="body">The Body element of the Word document.</param>
        /// <param name="structuredDoc">The StructuredDocument object to populate.</param>
        void ExtractStructure(Body? body, StructuredDocument structuredDoc);
    }
}