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

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Processor for PDF documents using iText7
    /// </summary>
    public class PdfDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public PdfDocumentProcessor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public string[] SupportedExtensions => new[] { ".pdf" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractTextFromPdf");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "PdfDocumentProcessor",
                    $"Extracting text from PDF: {IOPath.GetFileName(filePath)}");

                // Use Task.Run to execute file I/O and processing on a background thread
                return await Task.Run(() =>
                {
                    var fullText = new StringBuilder();

                    // Use direct synchronous reads inside Task.Run to avoid double async calls
                    using (PdfReader pdfReader = new PdfReader(filePath))
                    using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
                    {
                        int numPages = pdfDocument.GetNumberOfPages();
                        _diagnostics.Log(DiagnosticLevel.Info, "PdfDocumentProcessor",
                            $"PDF has {numPages} pages");

                        for (int i = 1; i <= numPages; i++)
                        {
                            // Add page header
                            fullText.AppendLine($"--- Page {i} ---");

                            // Extract text from page
                            var strategy = new LocationTextExtractionStrategy();
                            string pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(i), strategy);

                            // Add to result
                            fullText.AppendLine(pageText);
                            fullText.AppendLine();
                        }
                    }

                    return fullText.ToString();
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor",
                    $"Error extracting text from PDF: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractTextFromPdf");
            }
        }

        public bool SupportsStructuredExtraction => true;

        public async Task<StructuredDocument> ExtractStructuredContentAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractStructuredContentFromPdf");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "PdfDocumentProcessor",
                    $"Extracting structured content from PDF: {IOPath.GetFileName(filePath)}");

                // Use Task.Run to execute file I/O and processing on a background thread
                return await Task.Run(() =>
                {
                    var structuredDoc = new StructuredDocument
                    {
                        Title = IOPath.GetFileNameWithoutExtension(filePath)
                    };

                    using (PdfReader pdfReader = new PdfReader(filePath))
                    using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
                    {
                        // Try to extract document metadata first
                        ExtractDocumentMetadata(pdfDocument, structuredDoc);

                        int numPages = pdfDocument.GetNumberOfPages();
                        for (int i = 1; i <= numPages; i++)
                        {
                            ProcessPage(pdfDocument.GetPage(i), i, numPages, structuredDoc);
                        }
                    }

                    // After extracting all content, try to identify structure
                    EnhanceDocumentStructure(structuredDoc);

                    return structuredDoc;
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor",
                    $"Error extracting structured content from PDF: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractStructuredContentFromPdf");
            }
        }

        #region Helper Methods

        private void ExtractDocumentMetadata(PdfDocument pdfDocument, StructuredDocument structuredDoc)
        {
            try
            {
                // Try to get title from document metadata
                PdfDocumentInfo info = pdfDocument.GetDocumentInfo();
                if (info != null && !string.IsNullOrWhiteSpace(info.GetTitle()))
                {
                    structuredDoc.Title = info.GetTitle();
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "PdfDocumentProcessor",
                    $"Error extracting PDF metadata: {ex.Message}");
                // Title will remain as filename (set in calling method)
            }
        }

        private void ProcessPage(PdfPage page, int pageNumber, int totalPages, StructuredDocument structuredDoc)
        {
            try
            {
                // First, extract text with location information
                var strategy = new CustomLocationTextExtractionStrategy();
                PdfCanvasProcessor processor = new PdfCanvasProcessor(strategy);
                processor.ProcessPageContent(page);

                // Get text chunks with positioning information
                var textChunks = strategy.GetTextChunks();

                // If no text was extracted, try image-based approach
                if (textChunks.Count == 0 && page.GetContentStreamCount() > 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "PdfDocumentProcessor",
                        $"Page {pageNumber} might be image-based. Attempting alternative extraction.");

                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = ElementType.Paragraph,
                        Text = $"[Image-based content detected on page {pageNumber}]",
                        Metadata = new Dictionary<string, string>
                        {
                            { "PageNumber", pageNumber.ToString() },
                            { "ContentType", "ImageBased" }
                        }
                    });
                    return;
                }

                // Group text chunks into paragraphs based on positioning
                var paragraphs = GroupTextChunksIntoParagraphs(textChunks);

                // Process each paragraph
                foreach (var paragraph in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(paragraph.Text))
                        continue;

                    // Determine if this is likely a heading based on formatting characteristics
                    bool isHeading = IsLikelyHeading(paragraph, paragraphs);
                    int? headingLevel = isHeading ? DetermineHeadingLevel(paragraph, paragraphs) : null;

                    // Check if this could be a table
                    bool isTable = IsLikelyTable(paragraph);

                    // Create appropriate element
                    ElementType elementType;
                    if (isHeading)
                    {
                        elementType = headingLevel switch
                        {
                            1 => ElementType.Heading1,
                            2 => ElementType.Heading2,
                            _ => ElementType.Heading3
                        };
                    }
                    else if (isTable)
                    {
                        elementType = ElementType.Table;
                    }
                    else
                    {
                        elementType = ElementType.Paragraph;
                    }

                    // Add element to document
                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = elementType,
                        Text = paragraph.Text,
                        HeadingLevel = headingLevel,
                        Metadata = new Dictionary<string, string>
                        {
                            { "PageNumber", pageNumber.ToString() },
                            { "YPosition", paragraph.Y.ToString() },
                            { "FontSize", paragraph.FontSize.ToString("F1") }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "PdfDocumentProcessor",
                    $"Error processing page {pageNumber}: {ex.Message}");

                // Add a simple element for this page
                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Paragraph,
                    Text = $"[Error processing content on page {pageNumber}]",
                    Metadata = new Dictionary<string, string>
                    {
                        { "PageNumber", pageNumber.ToString() },
                        { "Error", ex.Message }
                    }
                });
            }
        }

        private void EnhanceDocumentStructure(StructuredDocument structuredDoc)
        {
            // Create a section hierarchy
            var sectionStack = new Stack<(int Level, string Heading, int Index)>();

            // First pass: Identify all headings
            for (int i = 0; i < structuredDoc.Elements.Count; i++)
            {
                var element = structuredDoc.Elements[i];
                if (element.Type == ElementType.Heading1 || element.Type == ElementType.Heading2 || element.Type == ElementType.Heading3)
                {
                    int level = element.HeadingLevel ?? (element.Type == ElementType.Heading1 ? 1 :
                                                         element.Type == ElementType.Heading2 ? 2 : 3);

                    // Pop any headings of equal or lower importance
                    while (sectionStack.Count > 0 && sectionStack.Peek().Level >= level)
                    {
                        sectionStack.Pop();
                    }

                    // Push this heading
                    sectionStack.Push((level, element.Text, i));
                }
            }

            // Reset for second pass
            sectionStack.Clear();

            // Second pass: Update all elements with section paths
            for (int i = 0; i < structuredDoc.Elements.Count; i++)
            {
                var element = structuredDoc.Elements[i];

                if (element.Type == ElementType.Heading1 || element.Type == ElementType.Heading2 || element.Type == ElementType.Heading3)
                {
                    int level = element.HeadingLevel ?? (element.Type == ElementType.Heading1 ? 1 :
                                                         element.Type == ElementType.Heading2 ? 2 : 3);

                    // Pop any headings of equal or lower importance
                    while (sectionStack.Count > 0 && sectionStack.Peek().Level >= level)
                    {
                        sectionStack.Pop();
                    }

                    // Push this heading
                    sectionStack.Push((level, element.Text, i));
                }

                // Create section path
                if (sectionStack.Count > 0)
                {
                    element.SectionPath = string.Join("/", sectionStack.Reverse().Select(s => s.Heading));
                }
            }
        }

        private List<TextParagraph> GroupTextChunksIntoParagraphs(List<TextChunk> textChunks)
        {
            if (textChunks.Count == 0)
                return new List<TextParagraph>();

            // Sort chunks by Y position (top to bottom) and then by X position (left to right)
            var sortedChunks = textChunks
                .OrderByDescending(c => c.Bounds.GetY())
                .ThenBy(c => c.Bounds.GetX())
                .ToList();

            var paragraphs = new List<TextParagraph>();
            TextParagraph? currentParagraph = null;
            float lastY = sortedChunks[0].Bounds.GetY();
            float lastFontSize = sortedChunks[0].FontSize;

            foreach (var chunk in sortedChunks)
            {
                // Check if this chunk starts a new paragraph
                bool isNewParagraph = currentParagraph == null ||
                                    Math.Abs(chunk.Bounds.GetY() - lastY) > chunk.FontSize * 0.5f ||  // Significant Y change indicates new paragraph
                                    Math.Abs(chunk.FontSize - lastFontSize) > 0.1f;              // Font size change indicates new paragraph

                if (isNewParagraph)
                {
                    // Finish previous paragraph
                    if (currentParagraph != null)
                    {
                        paragraphs.Add(currentParagraph);
                    }

                    // Start a new paragraph
                    currentParagraph = new TextParagraph
                    {
                        Text = chunk.Text,
                        Y = chunk.Bounds.GetY(),
                        FontSize = chunk.FontSize
                    };
                }
                else
                {
                    // Add to current paragraph
                    if (currentParagraph != null)
                    {
                        // Only add space between chunks if the X position indicates it
                        bool needsSpace = chunk.Bounds.GetX() > lastY + 1; // Simplified space detection
                        if (needsSpace)
                        {
                            currentParagraph.Text += " ";
                        }
                        currentParagraph.Text += chunk.Text;
                    }
                }

                lastY = chunk.Bounds.GetY();
                lastFontSize = chunk.FontSize;
            }

            // Add the last paragraph
            if (currentParagraph != null)
            {
                paragraphs.Add(currentParagraph);
            }

            return paragraphs;
        }

        private bool IsLikelyHeading(TextParagraph paragraph, List<TextParagraph> allParagraphs)
        {
            // Calculate average font size across all paragraphs
            float averageFontSize = allParagraphs.Average(p => p.FontSize);

            // A paragraph is likely a heading if:
            // 1. It's significantly larger than average font size
            // 2. It's relatively short (headings are usually not long)
            // 3. It doesn't end with punctuation like period, which is common for regular paragraphs

            bool isLargerThanAverage = paragraph.FontSize > averageFontSize * 1.2f;
            bool isRelativelyShort = paragraph.Text.Length < 100;
            bool doesNotEndWithPeriod = !paragraph.Text.TrimEnd().EndsWith(".");

            return isLargerThanAverage && isRelativelyShort;
        }

        private int DetermineHeadingLevel(TextParagraph paragraph, List<TextParagraph> allParagraphs)
        {
            // Get all paragraphs that appear to be headings
            var headingParagraphs = allParagraphs
                .Where(p => IsLikelyHeading(p, allParagraphs))
                .OrderByDescending(p => p.FontSize)
                .ToList();

            if (headingParagraphs.Count == 0)
                return 3; // Default

            // The largest font size becomes level 1, the next largest becomes level 2, and everything else is level 3
            float largestSize = headingParagraphs[0].FontSize;
            float secondLargestSize = headingParagraphs.Count > 1 ? headingParagraphs[1].FontSize : largestSize * 0.9f;

            if (Math.Abs(paragraph.FontSize - largestSize) < 0.1f)
                return 1;
            if (Math.Abs(paragraph.FontSize - secondLargestSize) < 0.1f)
                return 2;

            return 3;
        }

        private bool IsLikelyTable(TextParagraph paragraph)
        {
            // Tables often have consistent pipe or tab characters, or lots of spaces
            bool hasPipes = paragraph.Text.Count(c => c == '|') > 2;
            bool hasTabs = paragraph.Text.Count(c => c == '\t') > 2;
            bool hasRepeatedSpaces = Regex.IsMatch(paragraph.Text, @"\s{3,}");

            // Tables also often have numeric alignments
            bool hasNumericColumns = Regex.IsMatch(paragraph.Text, @"\s+\d+(\.\d+)?\s+");

            return (hasPipes || hasTabs || hasRepeatedSpaces) && hasNumericColumns;
        }

        #endregion

        #region Custom Text Extraction

        // Simple class to hold paragraph information
        private class TextParagraph
        {
            public string Text { get; set; } = string.Empty;
            public float Y { get; set; }
            public float FontSize { get; set; }
        }

        // Custom class to track text chunks with font information
        private class TextChunk
        {
            public string Text { get; set; } = string.Empty;
            public float FontSize { get; set; }
            public required Rectangle Bounds { get; set; }
        }

        // Custom location strategy to extract text with positioning and font information
        private class CustomLocationTextExtractionStrategy : LocationTextExtractionStrategy
        {
            private readonly List<TextChunk> _textChunks = new List<TextChunk>();

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT)
                {
                    TextRenderInfo renderInfo = (TextRenderInfo)data;

                    // Get the text and its bounds
                    string text = renderInfo.GetText();
                    if (string.IsNullOrEmpty(text))
                        return;

                    // Get font details
                    float fontSize = renderInfo.GetFontSize();

                    // Get the bounds
                    Rectangle textRectangle = renderInfo.GetBaseline().GetBoundingRectangle();

                    _textChunks.Add(new TextChunk
                    {
                        Text = text,
                        FontSize = fontSize,
                        Bounds = textRectangle
                    });
                }

                base.EventOccurred(data, type);
            }

            public List<TextChunk> GetTextChunks()
            {
                return _textChunks;
            }
        }

        #endregion
    }
}