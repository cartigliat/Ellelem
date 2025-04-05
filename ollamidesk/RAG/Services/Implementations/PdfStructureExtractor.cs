// ollamidesk/RAG/Services/Implementations/PdfStructureExtractor.cs
// MODIFIED VERSION with optimizations
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

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Extracts structured content from PDF documents using iText7.
    /// </summary>
    public class PdfStructureExtractor : IPdfStructureExtractor
    {
        private readonly RagDiagnosticsService _diagnostics;

        // Confidence threshold for font name checks (adjust if needed)
        private static readonly string[] BoldSubstrings = { "bold", "black", "heavy", "semibold" };
        private static readonly string[] ItalicSubstrings = { "italic", "oblique" };

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

                return await Task.Run(() =>
                {
                    var structuredDoc = new StructuredDocument
                    {
                        Title = IOPath.GetFileNameWithoutExtension(filePath)
                    };
                    var allParagraphsAcrossPages = new List<TextParagraph>(); // Store all paragraphs for global analysis

                    try
                    {
                        using (PdfReader pdfReader = new PdfReader(filePath))
                        using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
                        {
                            ExtractDocumentMetadata(pdfDocument, structuredDoc);

                            int numPages = pdfDocument.GetNumberOfPages();
                            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Processing {numPages} pages.");
                            for (int i = 1; i <= numPages; i++)
                            {
                                // Process page and add paragraphs to the global list
                                allParagraphsAcrossPages.AddRange(ProcessPage(pdfDocument.GetPage(i), i, structuredDoc));
                            }
                        }

                        // After extracting all content, try to identify structure globally
                        EnhanceDocumentStructure(structuredDoc, allParagraphsAcrossPages);
                        _diagnostics.Log(DiagnosticLevel.Info, "PdfStructureExtractor", $"Extraction complete. Found {structuredDoc.Elements.Count} elements.");
                    }
                    catch (iText.IO.Exceptions.IOException ioEx) when (ioEx.Message.Contains("PDF header signature not found"))
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor", $"Invalid PDF header for file: {filePath}. Error: {ioEx.Message}");
                        structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Error: Invalid or corrupt PDF file (header signature not found): {IOPath.GetFileName(filePath)}]" });
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                           $"Internal error during PDF structure extraction task: {ex.Message}");
                        throw new DocumentProcessingException($"Failed to extract structure from PDF: {IOPath.GetFileName(filePath)}", ex);
                    }

                    return structuredDoc;
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DocumentProcessingException && ex is not FileNotFoundException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                    $"Outer error extracting PDF structure: {ex.Message}");
                throw new DocumentProcessingException($"Error processing PDF file: {IOPath.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("PdfStructureExtractor.ExtractStructureAsync");
            }
        }


        #region Helper Methods (Modified)

        private void ExtractDocumentMetadata(PdfDocument pdfDocument, StructuredDocument structuredDoc)
        {
            // (Same as before)
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

        // Modified to return paragraphs for global analysis later
        private List<TextParagraph> ProcessPage(PdfPage page, int pageNumber, StructuredDocument structuredDoc)
        {
            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Processing page {pageNumber}.");
            var pageParagraphs = new List<TextParagraph>();
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
                    // Add placeholder element directly to structuredDoc here if needed
                    // structuredDoc.Elements.Add(...)
                    return pageParagraphs; // Return empty list for this page
                }

                pageParagraphs = GroupTextChunksIntoParagraphs(textChunks, pageNumber); // Pass page number
                _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", $"Page {pageNumber}: Grouped into {pageParagraphs.Count} paragraphs.");

                // Initial classification can happen here, but final decision is in EnhanceDocumentStructure
                // foreach (var para in pageParagraphs) { /* Basic classification? */ }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfStructureExtractor",
                    $"Error processing page {pageNumber}: {ex.Message}");
                // Add placeholder element directly to structuredDoc here if needed
                // structuredDoc.Elements.Add(...)
            }
            return pageParagraphs;
        }

        // Modified EnhanceDocumentStructure to take all paragraphs
        private void EnhanceDocumentStructure(StructuredDocument structuredDoc, List<TextParagraph> allParagraphs)
        {
            if (allParagraphs.Count == 0) return;

            // Calculate global font statistics (more robust)
            var validFontSizes = allParagraphs.Where(p => p.FontSize > 0).Select(p => p.FontSize).ToList();
            float globalAvgFontSize = validFontSizes.Count > 0 ? validFontSizes.Average() : 10f; // Default if no valid sizes
            float globalStdDevFontSize = validFontSizes.Count > 1 ? (float)Math.Sqrt(validFontSizes.Average(v => Math.Pow(v - globalAvgFontSize, 2))) : 1f;

            var sectionStack = new Stack<(int Level, string Heading)>();
            var elements = new List<DocumentElement>(); // Build new list

            foreach (var paragraph in allParagraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph.Text)) continue;

                // Detect Lists (basic example)
                var listMatch = Regex.Match(paragraph.Text.TrimStart(), @"^([\*\-\•]|\d+\.|\w[\.\)])\s+");
                bool isListItem = listMatch.Success;

                // Heading Check (using global stats and boldness)
                bool isLikelyHeading = IsLikelyHeading(paragraph, globalAvgFontSize, globalStdDevFontSize);
                int? headingLevel = isLikelyHeading ? DetermineHeadingLevel(paragraph, allParagraphs) : null;

                // Determine ElementType
                ElementType elementType = ElementType.Paragraph; // Default
                if (isLikelyHeading && headingLevel.HasValue)
                {
                    elementType = headingLevel switch { 1 => ElementType.Heading1, 2 => ElementType.Heading2, _ => ElementType.Heading3 };

                    // --- Update Section Stack ---
                    int currentLevel = headingLevel.Value;
                    while (sectionStack.Count > 0 && sectionStack.Peek().Level >= currentLevel)
                    {
                        sectionStack.Pop();
                    }
                    sectionStack.Push((currentLevel, paragraph.Text));
                    // -------------------------
                }
                else if (isListItem)
                {
                    elementType = ElementType.ListItem;
                }
                // Basic Table check (can be improved)
                else if (IsLikelyTable(paragraph))
                {
                    elementType = ElementType.Table;
                }

                // --- Assign Section Path ---
                string sectionPath = sectionStack.Count > 0
                    ? string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading))
                    : string.Empty;
                // -------------------------

                elements.Add(new DocumentElement
                {
                    Type = elementType,
                    Text = paragraph.Text,
                    HeadingLevel = headingLevel,
                    SectionPath = sectionPath, // Assign calculated path
                    Metadata = new Dictionary<string, string> {
                        { "PageNumber", paragraph.PageNumber.ToString() },
                        { "YPosition", paragraph.Y.ToString("F0") },
                        { "FontSize", paragraph.FontSize.ToString("F1") },
                        { "IsBold", paragraph.IsBold.ToString() } // Add boldness metadata
                    }
                });
            }

            structuredDoc.Elements = elements; // Replace elements with the processed list
            _diagnostics.Log(DiagnosticLevel.Debug, "PdfStructureExtractor", "Enhanced document structure with section paths and improved element types.");
        }

        // Modified GroupTextChunksIntoParagraphs
        private List<TextParagraph> GroupTextChunksIntoParagraphs(List<TextChunk> textChunks, int pageNumber)
        {
            if (textChunks.Count == 0) return new List<TextParagraph>();

            var sortedChunks = textChunks
                .OrderByDescending(c => c.Bounds.GetY())
                .ThenBy(c => c.Bounds.GetX())
                .ToList();

            var paragraphs = new List<TextParagraph>();
            if (sortedChunks.Count == 0) return paragraphs;

            TextParagraph currentParagraph = CreateNewParagraph(sortedChunks[0], pageNumber);
            float lastY = currentParagraph.Y;
            float lastXEnd = sortedChunks[0].Bounds.GetX() + sortedChunks[0].Bounds.GetWidth();
            float typicalLineHeight = sortedChunks[0].FontSize * 1.2f; // Estimate typical line height

            for (int i = 1; i < sortedChunks.Count; i++)
            {
                var chunk = sortedChunks[i];
                var bounds = chunk.Bounds;
                float currentY = bounds.GetY();
                float currentXStart = bounds.GetX();

                // Estimate line spacing threshold (adjust multiplier as needed)
                float paragraphBreakThreshold = typicalLineHeight * 1.5f;

                // Check for new paragraph:
                // 1. Significant vertical gap (more than ~1.5 lines)
                // 2. Or, current chunk starts significantly *before* the previous line ended (indentation, new column)
                bool yDifference = (lastY - currentY) > paragraphBreakThreshold;
                bool significantIndent = currentXStart < (lastXEnd - chunk.FontSize * 2) && Math.Abs(lastY - currentY) < paragraphBreakThreshold; // Check Y proximity for indent

                if (yDifference || significantIndent)
                {
                    if (!string.IsNullOrWhiteSpace(currentParagraph.Text))
                    {
                        currentParagraph.Text = currentParagraph.Text.Trim(); // Trim final paragraph
                        paragraphs.Add(currentParagraph);
                    }
                    currentParagraph = CreateNewParagraph(chunk, pageNumber);
                    typicalLineHeight = chunk.FontSize * 1.2f; // Update typical height
                }
                else // Add to current paragraph
                {
                    float spaceThreshold = chunk.FontSize * 0.2f; // Space width threshold
                    if (currentXStart > lastXEnd + spaceThreshold)
                    {
                        currentParagraph.Text += " "; // Add space if horizontal gap
                    }
                    currentParagraph.Text += chunk.Text;
                    // Update paragraph style based on the majority or last chunk? Let's use last for now.
                    currentParagraph.FontSize = chunk.FontSize;
                    currentParagraph.IsBold = currentParagraph.IsBold || chunk.IsBold; // If any part is bold, mark paragraph as potentially bold
                }
                lastY = currentY;
                lastXEnd = bounds.GetX() + bounds.GetWidth();
            }

            if (!string.IsNullOrWhiteSpace(currentParagraph.Text))
            {
                currentParagraph.Text = currentParagraph.Text.Trim();
                paragraphs.Add(currentParagraph);
            }

            return paragraphs;
        }

        private TextParagraph CreateNewParagraph(TextChunk firstChunk, int pageNumber)
        {
            return new TextParagraph
            {
                Y = firstChunk.Bounds.GetY(),
                FontSize = firstChunk.FontSize,
                IsBold = firstChunk.IsBold,
                Text = firstChunk.Text.TrimStart(),
                PageNumber = pageNumber
            };
        }


        // Modified IsLikelyHeading
        private bool IsLikelyHeading(TextParagraph paragraph, float globalAvgFontSize, float globalStdDevFontSize)
        {
            if (string.IsNullOrWhiteSpace(paragraph.Text) || paragraph.Text.Length > 200) // Headings are usually shorter
                return false;

            // Criteria:
            // 1. Font size significantly larger than average OR paragraph is bold.
            // 2. Not ending with typical sentence punctuation.
            // 3. Relatively short.

            // Threshold: More than 1 standard deviation above average, or slightly above average AND bold
            bool significantSize = paragraph.FontSize > globalAvgFontSize + globalStdDevFontSize * 0.75f;
            bool slightlyLargerAndBold = paragraph.IsBold && paragraph.FontSize > globalAvgFontSize + globalStdDevFontSize * 0.25f;

            bool fontCriteriaMet = significantSize || slightlyLargerAndBold;

            bool punctuationCriteriaMet = !Regex.IsMatch(paragraph.Text.TrimEnd(), @"[.?!:]$");

            // Additional check: Less likely to be a heading if it contains multiple sentences.
            bool likelySingleSentence = Regex.Matches(paragraph.Text, @"[.?!]").Count <= 1;

            return fontCriteriaMet && punctuationCriteriaMet && likelySingleSentence;
        }

        // DetermineHeadingLevel (consider using boldness)
        private int DetermineHeadingLevel(TextParagraph paragraph, List<TextParagraph> allParagraphs)
        {
            // Recalculate global stats for context
            var validFontSizes = allParagraphs.Where(p => p.FontSize > 0).Select(p => p.FontSize).ToList();
            float globalAvgFontSize = validFontSizes.Count > 0 ? validFontSizes.Average() : 10f;
            float globalStdDevFontSize = validFontSizes.Count > 1 ? (float)Math.Sqrt(validFontSizes.Average(v => Math.Pow(v - globalAvgFontSize, 2))) : 1f;


            var distinctHeadingStyles = allParagraphs
                .Where(p => IsLikelyHeading(p, globalAvgFontSize, globalStdDevFontSize))
                .Select(p => new { p.FontSize, p.IsBold }) // Consider both size and boldness
                .Distinct()
                .OrderByDescending(s => s.FontSize) // Primarily order by size
                .ThenByDescending(s => s.IsBold)   // Then by boldness (bold counts as 'higher')
                .ToList();

            if (distinctHeadingStyles.Count == 0) return 3; // Default

            // Find where the current paragraph's style fits in the sorted list
            int index = distinctHeadingStyles.FindIndex(s => Math.Abs(s.FontSize - paragraph.FontSize) < 0.1f && s.IsBold == paragraph.IsBold);

            if (index == -1) return 3; // Not found among distinct styles, treat as lowest level heading

            if (index == 0) return 1; // Top style
            if (index == 1) return 2; // Second style
            return 3;                 // Others
        }

        // IsLikelyTable (Can be significantly improved, basic version retained)
        private bool IsLikelyTable(TextParagraph paragraph)
        {
            int spaceSequences = Regex.Matches(paragraph.Text, @"\s{3,}").Count;
            int pipeCount = paragraph.Text.Count(c => c == '|');
            return (spaceSequences > 2 || pipeCount > 2) && paragraph.Text.Length > 15; // Slightly stricter
        }

        #endregion

        #region Custom Text Extraction Classes (Modified)

        // Internal class representing a paragraph candidate
        private class TextParagraph
        {
            public string Text { get; set; } = string.Empty;
            public float Y { get; set; }
            public float FontSize { get; set; }
            public bool IsBold { get; set; } // Added boldness
            public int PageNumber { get; set; } // Added page number
        }

        // Internal class representing a raw text chunk from iText
        private class TextChunk
        {
            public string Text { get; set; } = string.Empty;
            public float FontSize { get; set; }
            public required Rectangle Bounds { get; set; }
            public bool IsBold { get; set; } // Added boldness flag
        }

        // Modified strategy to extract boldness
        private class CustomLocationTextExtractionStrategy : LocationTextExtractionStrategy
        {
            private readonly List<TextChunk> _textChunks = new List<TextChunk>();

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT)
                {
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    string text = renderInfo.GetText();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        base.EventOccurred(data, type);
                        return;
                    }

                    float fontSize = renderInfo.GetFontSize();
                    LineSegment baseline = renderInfo.GetBaseline();
                    Vector startPoint = baseline.GetStartPoint();
                    Vector endPoint = baseline.GetEndPoint();
                    float ascent = renderInfo.GetAscentLine().GetStartPoint().Get(Vector.I2);
                    float descent = renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2);

                    Rectangle textRectangle = new Rectangle(
                         startPoint.Get(Vector.I1),
                         descent,
                         Math.Max(1, endPoint.Get(Vector.I1) - startPoint.Get(Vector.I1)),
                         Math.Max(1, ascent - descent)
                     );

                    // --- Detect Boldness ---
                    bool isBold = false;
                    try
                    {
                        var font = renderInfo.GetFont().GetFontProgram();
                        if (font != null)
                        {
                            string fontName = font.GetFontNames().GetFontName().ToLowerInvariant();
                            isBold = BoldSubstrings.Any(sub => fontName.Contains(sub));
                        }
                    }
                    catch { /* Ignore font access errors */ }
                    // ---------------------

                    _textChunks.Add(new TextChunk
                    {
                        Text = text,
                        FontSize = fontSize,
                        Bounds = textRectangle,
                        IsBold = isBold // Store boldness
                    });
                }
                base.EventOccurred(data, type);
            }

            public List<TextChunk> GetTextChunks() => _textChunks;
        }
        #endregion
    }
}