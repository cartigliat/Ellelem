// ollamidesk/RAG/Services/Implementations/PdfDocumentProcessor.cs
using IOPath = System.IO.Path; // Define alias for System.IO.Path
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
// Removed unused iText using statements
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Services.Interfaces; // Added interface namespace
using ollamidesk.RAG.Exceptions; // Added for potential exceptions

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Processor for PDF documents using iText7. Delegates structure extraction.
    /// </summary>
    public class PdfDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;
        private readonly IPdfStructureExtractor _pdfStructureExtractor; // <-- ADDED Dependency

        // Constructor updated to accept the extractor
        public PdfDocumentProcessor(RagDiagnosticsService diagnostics, IPdfStructureExtractor pdfStructureExtractor)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _pdfStructureExtractor = pdfStructureExtractor ?? throw new ArgumentNullException(nameof(pdfStructureExtractor)); // <-- Assign dependency
        }

        public string[] SupportedExtensions => new[] { ".pdf" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        // ExtractTextAsync remains largely the same, could potentially be moved
        // to the extractor as well, but keeping it here for now as per original structure.
        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("PdfDocumentProcessor.ExtractTextAsync");
            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor", $"File not found: {filePath}");
                    throw new FileNotFoundException($"PDF document not found: {filePath}");
                }
                _diagnostics.Log(DiagnosticLevel.Info, "PdfDocumentProcessor",
                    $"Extracting plain text from PDF: {IOPath.GetFileName(filePath)}");

                return await Task.Run(() => // Removed async from lambda
                {
                    var fullText = new StringBuilder();
                    try
                    {
                        using (PdfReader pdfReader = new PdfReader(filePath))
                        using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
                        {
                            int numPages = pdfDocument.GetNumberOfPages();
                            for (int i = 1; i <= numPages; i++)
                            {
                                // Using SimpleTextExtractionStrategy for potentially faster plain text
                                var strategy = new SimpleTextExtractionStrategy();
                                string pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(i), strategy);
                                if (!string.IsNullOrWhiteSpace(pageText)) // Add page break only if page has text
                                {
                                    fullText.AppendLine($"--- Page {i} ---");
                                    fullText.AppendLine(pageText.Trim()); // Trim whitespace from page text
                                    fullText.AppendLine();
                                }
                                else
                                {
                                    _diagnostics.Log(DiagnosticLevel.Debug, "PdfDocumentProcessor", $"Page {i} has no extractable text (SimpleTextExtractionStrategy).");
                                }
                            }
                        }
                    }
                    catch (iText.IO.Exceptions.IOException ioEx) when (ioEx.Message.Contains("PDF header signature not found"))
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor", $"Invalid PDF header for file: {filePath}. Error: {ioEx.Message}");
                        return $"[Error: Invalid or corrupt PDF file (header signature not found): {IOPath.GetFileName(filePath)}]";
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor",
                           $"Internal error during PDF text extraction task: {ex.Message}");
                        // Wrap in a processing exception
                        throw new DocumentProcessingException($"Failed to extract text from PDF: {IOPath.GetFileName(filePath)}", ex);
                    }
                    return fullText.ToString().Trim(); // Removed await/ConfigureAwait
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor",
                    $"Outer error extracting PDF text: {ex.Message}");
                // Re-throw as specific exception type
                throw new DocumentProcessingException($"Error processing PDF for text extraction: {IOPath.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("PdfDocumentProcessor.ExtractTextAsync");
            }
        }

        public bool SupportsStructuredExtraction => true;

        // Updated to delegate to the injected service
        public async Task<StructuredDocument> ExtractStructuredContentAsync(string filePath)
        {
            // Delegate directly to the injected extractor
            // The extractor now handles logging start/end operations and its own errors.
            // Keep a top-level try-catch here if needed for processor-level error handling.
            try
            {
                return await _pdfStructureExtractor.ExtractStructureAsync(filePath).ConfigureAwait(false);
            }
            catch (DocumentProcessingException)
            {
                // Logged within extractor, rethrow
                throw;
            }
            catch (FileNotFoundException)
            {
                // Logged within extractor, rethrow
                throw;
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors not handled by the extractor
                _diagnostics.Log(DiagnosticLevel.Critical, "PdfDocumentProcessor",
                   $"Unexpected critical error calling IPdfStructureExtractor: {ex.Message}");
                // Return a document indicating failure
                var errorDoc = new StructuredDocument { Title = IOPath.GetFileNameWithoutExtension(filePath) };
                errorDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Critical Error: Failed to extract structure from PDF: {ex.Message}]" });
                return errorDoc;
            }
        }

        // #region Helper Methods - REMOVED (Moved to PdfStructureExtractor)
        // - ExtractDocumentMetadata
        // - ProcessPage
        // - EnhanceDocumentStructure
        // - GroupTextChunksIntoParagraphs
        // - IsLikelyHeading
        // - DetermineHeadingLevel
        // - IsLikelyTable
        // #endregion

        // #region Custom Text Extraction - REMOVED (Moved to PdfStructureExtractor)
        // - TextParagraph class
        // - TextChunk class
        // - CustomLocationTextExtractionStrategy class
        // #endregion
    }
}