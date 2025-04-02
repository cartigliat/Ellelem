// ollamidesk/RAG/Services/Implementations/WordDocumentProcessor.cs
// Modified version - Uses Injected IWordStructureExtractor
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Exceptions;
using ollamidesk.RAG.Services.Interfaces; // <--- Ensure this using is present

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    public class WordDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;
        //          vvvvvvvvvvvvvvvvvvvvvvvvvvvv ----> ADDED: Field for injected extractor
        private readonly IWordStructureExtractor _wordStructureExtractor;

        //                                          vvvvvvvvvvvvvvvvvvvvvvvvvvvv ----> ADDED: Constructor parameter
        public WordDocumentProcessor(RagDiagnosticsService diagnostics, IWordStructureExtractor wordStructureExtractor)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            //          vvvvvvvvvvvvvvvvvvvvvvvvv ----> ADDED: Assign injected instance
            _wordStructureExtractor = wordStructureExtractor ?? throw new ArgumentNullException(nameof(wordStructureExtractor));
        }

        public string[] SupportedExtensions => new[] { ".docx" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        // ExtractTextAsync remains the same...
        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractTextFromWord");
            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"File not found: {filePath}");
                    throw new FileNotFoundException($"Word document not found: {filePath}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor", $"Extracting text from: {Path.GetFileName(filePath)}");

                return await Task.Run(() =>
                {
                    var textBuilder = new StringBuilder();
                    try
                    {
                        using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, false))
                        {
                            if (doc.MainDocumentPart?.Document?.Body == null)
                            {
                                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor", "Document body is null.");
                                return $"[Empty or invalid Word document: {Path.GetFileName(filePath)}]";
                            }

                            var body = doc.MainDocumentPart.Document.Body;

                            // Extract text from paragraphs and tables
                            foreach (var element in body.ChildElements)
                            {
                                if (element is Paragraph paragraph)
                                {
                                    textBuilder.AppendLine(ExtractTextFromParagraph(paragraph));
                                }
                                else if (element is Table table)
                                {
                                    ExtractTextFromTable(table, textBuilder); // Use the local helper for plain text
                                }
                            }
                        }
                        string result = textBuilder.ToString().Trim();
                        _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor", $"Extracted {result.Length} characters (plain text)");
                        return result;
                    }
                    catch (OpenXmlPackageException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"OpenXML package error (possibly corrupt): {ex.Message}");
                        throw new DocumentProcessingException($"Corrupt/invalid Word document: {Path.GetFileName(filePath)}", ex);
                    }
                    catch (FileFormatException ex)
                    { // Catch if it's likely an older .doc format
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"File format error (may be .doc): {ex.Message}");
                        return $"[Unsupported Word format (.doc?). Convert to .docx: {Path.GetFileName(filePath)}]";
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"Error during plain text extraction: {ex.Message}");
                        throw new DocumentProcessingException($"Failed to extract text from Word doc: {Path.GetFileName(filePath)}", ex);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"Outer error extracting text: {ex.Message}");
                throw new DocumentProcessingException($"Error reading Word document: {Path.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("ExtractTextFromWord");
            }
        }


        public bool SupportsStructuredExtraction => true;

        public async Task<StructuredDocument> ExtractStructuredContentAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractStructuredContentFromWord");
            var structuredDoc = new StructuredDocument { Title = Path.GetFileNameWithoutExtension(filePath) }; // Default title

            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"File not found for structured extraction: {filePath}");
                    throw new FileNotFoundException($"Word document not found: {filePath}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor", $"Extracting structured content from: {Path.GetFileName(filePath)}");

                // Use Task.Run for the OpenXML operations
                return await Task.Run(() =>
                {
                    try
                    {
                        using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, false))
                        {
                            // Extract title from properties or first heading
                            ExtractDocumentTitle(doc, structuredDoc);

                            // Use the injected helper class instance
                            // REMOVED: var extractor = new WordStructureExtractor(_diagnostics);
                            //          vvvvvvvvvvvvvvvvvvvvvvv ----> CHANGED: Use injected field
                            _wordStructureExtractor.ExtractStructure(doc.MainDocumentPart?.Document?.Body, structuredDoc);
                        }
                        _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor", $"Structured extraction complete. Found {structuredDoc.Elements.Count} elements.");
                        return structuredDoc;
                    }
                    catch (OpenXmlPackageException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"OpenXML package error during structured extraction: {ex.Message}");
                        structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Error: Corrupt/invalid Word document: {Path.GetFileName(filePath)}]" });
                        return structuredDoc; // Return partial/error doc
                    }
                    catch (FileFormatException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"File format error (may be .doc) during structured extraction: {ex.Message}");
                        structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Unsupported Word format (.doc?). Convert to .docx: {Path.GetFileName(filePath)}]" });
                        return structuredDoc; // Return partial/error doc
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"Error during structured content extraction: {ex.Message}");
                        throw new DocumentProcessingException($"Failed to extract structure from Word doc: {Path.GetFileName(filePath)}", ex);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor", $"Outer error extracting structure: {ex.Message}");
                throw new DocumentProcessingException($"Error reading Word document for structure: {Path.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("ExtractStructuredContentFromWord");
            }
        }

        // --- Helper methods remaining in WordDocumentProcessor ---
        // ExtractDocumentTitle, ExtractTextFromParagraph, ExtractTextFromTable remain the same...
        private void ExtractDocumentTitle(WordprocessingDocument doc, StructuredDocument structuredDoc)
        {
            // Try core properties first
            var coreProps = doc.PackageProperties;
            if (!string.IsNullOrWhiteSpace(coreProps?.Title))
            {
                structuredDoc.Title = coreProps.Title;
                _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor", $"Found title in properties: '{structuredDoc.Title}'");
                return;
            }

            // Try first H1 or Title styled paragraph
            var firstHeading = doc.MainDocumentPart?.Document?.Body
                ?.Descendants<Paragraph>()
                .FirstOrDefault(p => {
                    string? styleId = p?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                    return styleId != null && (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase));
                });

            if (firstHeading != null)
            {
                string headingText = firstHeading.InnerText.Trim();
                if (!string.IsNullOrWhiteSpace(headingText))
                {
                    structuredDoc.Title = headingText;
                    _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor", $"Using first H1/Title as title: '{structuredDoc.Title}'");
                    return;
                }
            }
            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor", $"No title found in properties or H1/Title style. Using filename '{structuredDoc.Title}'.");
        }

        // Helper specifically for plain text extraction from paragraphs (simpler than structure version)
        private string ExtractTextFromParagraph(Paragraph paragraph)
        {
            return paragraph.InnerText; // InnerText concatenates text from runs
        }

        // Helper specifically for plain text extraction from tables
        private void ExtractTextFromTable(Table table, StringBuilder textBuilder)
        {
            textBuilder.AppendLine(); // Add space before table
            foreach (var row in table.Elements<TableRow>())
            {
                foreach (var cell in row.Elements<TableCell>())
                {
                    // Concatenate text from all paragraphs in the cell for plain text extraction
                    string cellText = string.Join(" ", cell.Elements<Paragraph>().Select(p => p.InnerText.Trim()));
                    textBuilder.Append(cellText.Trim()).Append("\t"); // Use tab as separator for plain text
                }
                textBuilder.AppendLine(); // Newline after each row
            }
            textBuilder.AppendLine(); // Add space after table
        }
    }
}