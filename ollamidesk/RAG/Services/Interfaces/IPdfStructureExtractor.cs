// ollamidesk/RAG/Services/Interfaces/IPdfStructureExtractor.cs
using System.Threading.Tasks;
using ollamidesk.RAG.DocumentProcessors.Interfaces; // For StructuredDocument

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for extracting structured content from a PDF document file.
    /// </summary>
    public interface IPdfStructureExtractor
    {
        /// <summary>
        /// Extracts structured content from a PDF file.
        /// </summary>
        /// <param name="filePath">The path to the PDF file.</param>
        /// <returns>A Task representing the asynchronous operation, containing the StructuredDocument.</returns>
        Task<StructuredDocument> ExtractStructureAsync(string filePath);
    }
}