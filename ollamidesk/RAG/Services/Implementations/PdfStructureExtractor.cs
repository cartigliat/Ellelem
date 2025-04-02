// ollamidesk/RAG/Services/Implementations/PdfStructureExtractor.cs
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
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Filter;
using iText.Kernel.Geom;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Services.Interfaces; // Added interface namespace
using ollamidesk.RAG.Exceptions; // Added for potential exceptions

namespace ollamidesk.RAG.Services.Implementations // Changed namespace to match interface/implementations pattern
{
    /// <summary>
    /// Extracts structured content from PDF documents using iText7.
    /// </summary>
    public class PdfStructureExtractor : IPdfStructureExtractor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public PdfStructureExtractor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public async Task<StructuredDocument> ExtractStructureAsync(string filePath)
        {
            _diagnostics.StartOperation("PdfStructureExtractor.ExtractStructureAsync");

            try
            {
                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor", $"File not found: {filePath}");
                    throw new FileNotFoundException($"PDF document not found: {filePath}");
                }

                _diagnostics.Log(DiagnosticLevel.Info, "PdfStructureExtractor",
                    $"Extracting structured content from PDF: {IOPath.GetFileName(filePath)}");

                // Use Task.Run to execute potentially blocking file I/O and iText processing
                return await Task.Run(() => // Removed async from lambda as inner operations are sync
                {
                    var structuredDoc = new StructuredDocument
                    {
                        Title = IOPath.GetFileNameWithoutExtension(filePath)
                    };

                    try
                    {
                        using (PdfReader pdfReader = new PdfReader(filePath))
                        using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
                        {
                            // Try to extract document metadata first
                            ExtractDocumentMetadata(pdfDocument, structuredDoc);

                            int numPages = pdfDocument.GetNumberOfPages();
                            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Processing {numPages} pages.");
                            for (int i = 1; i <= numPages; i++)
                            {
                                ProcessPage(pdfDocument.GetPage(i), i, numPages, structuredDoc);
                            }
                        }

                        // After extracting all content, try to identify structure
                        EnhanceDocumentStructure(structuredDoc);
                        _diagnostics.Log(DiagnosticLevel.Info, "PdfStructureExtractor", $"Extraction complete. Found {structuredDoc.Elements.Count} elements.");
                    }
                    catch (iText.IO.Exceptions.IOException ioEx) when (ioEx.Message.Contains("PDF header signature not found"))
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor", $"Invalid PDF header for file: {filePath}. Error: {ioEx.Message}");
                        structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Error: Invalid or corrupt PDF file (header signature not found): {IOPath.GetFileName(filePath)}]" });
                        // No rethrow, return the document with the error message
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                           $"Internal error during PDF structure extraction task: {ex.Message}");
                        // Wrap in a processing exception to give context
                        throw new DocumentProcessingException($"Failed to extract structure from PDF: {IOPath.GetFileName(filePath)}", ex);
                    }

                    return structuredDoc; // Removed Task.FromResult and await
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                    $"Outer error extracting PDF structure: {ex.Message}");
                // Wrap in a processing exception to give context
                throw new DocumentProcessingException($"Error processing PDF file: {IOPath.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("PdfStructureExtractor.ExtractStructureAsync");
            }
        }


        #region Helper Methods (Copied from PdfDocumentProcessor)

        private void ExtractDocumentMetadata(PdfDocument pdfDocument, StructuredDocument structuredDoc)
        {
            try
            {
                PdfDocumentInfo info = pdfDocument.GetDocumentInfo();
                if (info != null && !string.IsNullOrWhiteSpace(info.GetTitle()))
                {
                    structuredDoc.Title = info.GetTitle();
                    _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Found title in PDF metadata: '{structuredDoc.Title}'");
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "PdfStructureExtractor",
                    $"Error extracting PDF metadata: {ex.Message}");
            }
        }

        private void ProcessPage(PdfPage page, int pageNumber, int totalPages, StructuredDocument structuredDoc)
        {
            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Processing page {pageNumber}.");
            try
            {
                var strategy = new CustomLocationTextExtractionStrategy();
                PdfCanvasProcessor processor = new PdfCanvasProcessor(strategy);
                processor.ProcessPageContent(page);
                var textChunks = strategy.GetTextChunks();

                if (textChunks.Count == 0 && page.GetContentStreamCount() > 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "PdfStructureExtractor",
                        $"Page {pageNumber} might be image-based or empty.");
                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = ElementType.Paragraph,
                        Text = $"[Page {pageNumber} appears to be image-based or has no extractable text]",
                        Metadata = new Dictionary<string, string> { { "PageNumber", pageNumber.ToString() }, { "ContentType", "ImageBasedOrEmpty" } }
                    });
                    return;
                }

                var paragraphs = GroupTextChunksIntoParagraphs(textChunks);
                _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Page {pageNumber}: Grouped into {paragraphs.Count} paragraphs.");


                foreach (var paragraph in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(paragraph.Text)) continue;

                    bool isHeading = IsLikelyHeading(paragraph, paragraphs);
                    int? headingLevel = isHeading ? DetermineHeadingLevel(paragraph, paragraphs) : null;
                    bool isTable = IsLikelyTable(paragraph); // Table check can be refined

                    ElementType elementType = ElementType.Paragraph; // Default
                    if (isHeading)
                    {
                        elementType = headingLevel switch { 1 => ElementType.Heading1, 2 => ElementType.Heading2, _ => ElementType.Heading3 };
                    }
                    else if (isTable)
                    {
                        elementType = ElementType.Table;
                    }

                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = elementType,
                        Text = paragraph.Text,
                        HeadingLevel = headingLevel,
                        Metadata = new Dictionary<string, string> { { "PageNumber", pageNumber.ToString() }, { "YPosition", paragraph.Y.ToString("F0") }, { "FontSize", paragraph.FontSize.ToString("F1") } }
                    });
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                    $"Error processing page {pageNumber}: {ex.Message}");
                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Paragraph,
                    Text = $"[Error processing content on page {pageNumber}]",
                    Metadata = new Dictionary<string, string> { { "PageNumber", pageNumber.ToString() }, { "Error", ex.Message } }
                });
            }
        }

        private void EnhanceDocumentStructure(StructuredDocument structuredDoc)
        {
            var sectionStack = new Stack<(int Level, string Heading)>(); // Removed index as it wasn't used

            // Create section paths based on heading hierarchy
            for (int i = 0; i < structuredDoc.Elements.Count; i++)
            {
                var element = structuredDoc.Elements[i];
                int? currentLevel = null;

                if (element.Type == ElementType.Heading1 || element.Type == ElementType.Heading2 || element.Type == ElementType.Heading3)
                {
                    currentLevel = element.HeadingLevel ?? (element.Type == ElementType.Heading1 ? 1 : (element.Type == ElementType.Heading2 ? 2 : 3));

                    // Pop headings of equal or greater level
                    while (sectionStack.Count > 0 && sectionStack.Peek().Level >= currentLevel.Value)
                    {
                        sectionStack.Pop();
                    }
                    // Push the current heading
                    sectionStack.Push((currentLevel.Value, element.Text));
                }

                // Assign the current section path to the element
                if (sectionStack.Count > 0)
                {
                    element.SectionPath = string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading));
                }
                else
                {
                    element.SectionPath = string.Empty; // Root level
                }
            }
            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", "Enhanced document structure with section paths.");
        }

        private List<TextParagraph> GroupTextChunksIntoParagraphs(List<TextChunk> textChunks)
        {
            if (textChunks.Count == 0) return new List<TextParagraph>();

            var sortedChunks = textChunks
                .OrderByDescending(c => c.Bounds.GetY())
                .ThenBy(c => c.Bounds.GetX())
                .ToList();

            var paragraphs = new List<TextParagraph>();
            if (sortedChunks.Count == 0) return paragraphs; // Handle empty list after sorting

            TextParagraph currentParagraph = new TextParagraph { Y = sortedChunks[0].Bounds.GetY(), FontSize = sortedChunks[0].FontSize };
            float lastXEnd = float.MinValue; // Track end of last chunk on the line

            for (int i = 0; i < sortedChunks.Count; i++)
            {
                var chunk = sortedChunks[i];
                var bounds = chunk.Bounds;

                // Check for new paragraph based on Y-position or large X-gap (start of line)
                // Tolerance based on font size helps group slightly misaligned lines
                bool yDifference = Math.Abs(bounds.GetY() - currentParagraph.Y) > chunk.FontSize * 0.7f;
                bool largeXGap = bounds.GetX() < lastXEnd - chunk.FontSize; // If X is significantly less than previous end, likely new line

                if (yDifference || largeXGap)
                {
                    // Finish previous paragraph if it has text
                    if (!string.IsNullOrWhiteSpace(currentParagraph.Text))
                    {
                        paragraphs.Add(currentParagraph);
                    }
                    // Start new paragraph
                    currentParagraph = new TextParagraph { Y = bounds.GetY(), FontSize = chunk.FontSize, Text = chunk.Text.TrimStart() }; // Trim leading space
                    lastXEnd = bounds.GetX() + bounds.GetWidth();
                }
                else
                {
                    // Add to current paragraph
                    // Check if space is needed based on X distance
                    float spaceThreshold = chunk.FontSize * 0.2f; // Adjust threshold as needed
                    if (bounds.GetX() > lastXEnd + spaceThreshold)
                    {
                        currentParagraph.Text += " "; // Add space
                    }
                    currentParagraph.Text += chunk.Text;
                    lastXEnd = bounds.GetX() + bounds.GetWidth();

                    // Use font size of the latest chunk for the paragraph? Or average? Max? For now, latest.
                    currentParagraph.FontSize = chunk.FontSize;
                }
            }

            // Add the very last paragraph if it has text
            if (!string.IsNullOrWhiteSpace(currentParagraph.Text))
            {
                paragraphs.Add(currentParagraph);
            }

            // Post-process: Merge paragraphs that are likely continuations (small Y diff, similar font)
            // This requires a more complex loop, potentially omitted for simplicity initially.

            return paragraphs;
        }

        private bool IsLikelyHeading(TextParagraph paragraph, List<TextParagraph> allParagraphs)
        {
            if (allParagraphs.Count < 3) return false; // Need context

            // Calculate average font size, excluding potential outliers
            var fontSizes = allParagraphs.Select(p => p.FontSize).Where(fs => fs > 0).ToList();
            if (fontSizes.Count < 3) return false;
            float averageFontSize = fontSizes.Average();
            float stdDev = (float)Math.Sqrt(fontSizes.Average(v => Math.Pow(v - averageFontSize, 2)));

            // Criteria: Significantly larger font, shorter text, not ending with common sentence terminators
            bool isLargerThanAverage = paragraph.FontSize > averageFontSize + stdDev * 0.5f; // More robust threshold
            bool isRelativelyShort = paragraph.Text.Length < 120;
            bool doesNotEndWithPunctuation = !Regex.IsMatch(paragraph.Text.TrimEnd(), @"[.?!:]$");
            // bool isCentered = false; // <-- REMOVED This Line

            // You could potentially use isCentered here in the future if you implement logic to check X position
            return isLargerThanAverage && isRelativelyShort && doesNotEndWithPunctuation;
        }

        private int DetermineHeadingLevel(TextParagraph paragraph, List<TextParagraph> allParagraphs)
        {
            var headingParagraphs = allParagraphs
                .Where(p => IsLikelyHeading(p, allParagraphs))
                .Select(p => p.FontSize)
                .Distinct()
                .OrderByDescending(fs => fs)
                .ToList();

            if (headingParagraphs.Count == 0) return 3; // Default if no distinct heading sizes found

            float currentFontSize = paragraph.FontSize;

            // Find index of the current font size in the distinct sorted list
            int index = headingParagraphs.FindIndex(fs => Math.Abs(fs - currentFontSize) < 0.1f);

            if (index == 0) return 1; // Largest font size
            if (index == 1) return 2; // Second largest
            // All others map to level 3 (or higher if more levels were needed)
            return 3;
        }

        private bool IsLikelyTable(TextParagraph paragraph)
        {
            // Refined table detection: Look for multiple columns separated by significant whitespace
            // and potentially rows with similar structure. This is complex and error-prone with text extraction alone.
            // Simple heuristic: Multiple instances of 3+ spaces, or multiple pipe characters
            int spaceSequences = Regex.Matches(paragraph.Text, @"\s{3,}").Count;
            int pipeCount = paragraph.Text.Count(c => c == '|');

            // Also check for typical table content like numbers aligned
            bool hasAlignedNumbers = Regex.IsMatch(paragraph.Text, @"(\s+\d+(\.\d+)?){2,}"); // At least two numbers separated by space

            return (spaceSequences > 1 || pipeCount > 1) && paragraph.Text.Length > 10; // Basic check, could be improved
        }

        #endregion

        #region Custom Text Extraction Classes (Copied from PdfDocumentProcessor)

        private class TextParagraph
        {
            public string Text { get; set; } = string.Empty;
            public float Y { get; set; }
            public float FontSize { get; set; }
        }

        private class TextChunk
        {
            public string Text { get; set; } = string.Empty;
            public float FontSize { get; set; }
            public required Rectangle Bounds { get; set; }
        }

        private class CustomLocationTextExtractionStrategy : LocationTextExtractionStrategy
        {
            private readonly List<TextChunk> _textChunks = new List<TextChunk>();

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT)
                {
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    string text = renderInfo.GetText();
                    if (string.IsNullOrWhiteSpace(text)) return; // Ignore whitespace chunks

                    float fontSize = renderInfo.GetFontSize();
                    // Correct way to get bounding box in iText 7/8
                    LineSegment baseline = renderInfo.GetBaseline();
                    Vector startPoint = baseline.GetStartPoint();
                    Vector endPoint = baseline.GetEndPoint();
                    float ascent = renderInfo.GetAscentLine().GetStartPoint().Get(Vector.I2); // Approx height based on ascent
                    float descent = renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2); // Approx height based on descent


                    // Create a rectangle approximating the text bounds
                    Rectangle textRectangle = new Rectangle(
                         startPoint.Get(Vector.I1), // x
                         descent, // y (bottom)
                         Math.Max(1, endPoint.Get(Vector.I1) - startPoint.Get(Vector.I1)), // width (ensure positive)
                         Math.Max(1, ascent - descent) // height (ensure positive)
                     );


                    _textChunks.Add(new TextChunk
                    {
                        Text = text,
                        FontSize = fontSize,
                        Bounds = textRectangle
                    });
                }
                base.EventOccurred(data, type); // Call base method
            }

            public List<TextChunk> GetTextChunks() => _textChunks;
        }
        #endregion
    }
}