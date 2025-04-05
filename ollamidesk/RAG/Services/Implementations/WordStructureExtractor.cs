// ollamidesk/RAG/Services/Implementations/WordStructureExtractor.cs
// MODIFIED VERSION with optimizations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Services.Interfaces; // Ensure this using is present

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    public class WordStructureExtractor : IWordStructureExtractor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public WordStructureExtractor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public void ExtractStructure(Body? body, StructuredDocument structuredDoc)
        {
            if (body == null)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "WordStructureExtractor", "Cannot process document content: Body is null");
                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Paragraph,
                    Text = "[Document body is empty or could not be read]"
                });
                return;
            }

            _diagnostics.StartOperation("ProcessWordDocumentContent (Helper)");
            try
            {
                var sectionStack = new Stack<(int Level, string Heading)>();
                var currentListItems = new List<Paragraph>();
                int processedElements = 0;
                float? defaultFontSize = null; // Estimate default font size

                foreach (var element in body.ChildElements)
                {
                    processedElements++;
                    if (element is Paragraph paragraph)
                    {
                        // Try to estimate default font size early on
                        if (!defaultFontSize.HasValue && !IsHeadingWithFormattingCheck(paragraph, null) && !HasListStyle(paragraph))
                        { // Pass null initially for defaultFontSize check
                            defaultFontSize = GetParagraphDominantFontSize(paragraph);
                            // Optionally log the estimated default size:
                            // if(defaultFontSize.HasValue) _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Estimated default font size: {defaultFontSize.Value / 2f}pt");
                        }

                        bool isListItem = HasListStyle(paragraph);
                        // Clear pending list if current paragraph is not a list item
                        if (!isListItem && currentListItems.Count > 0)
                        {
                            ProcessListItems(currentListItems, structuredDoc, sectionStack);
                            currentListItems.Clear();
                        }

                        // ***** CORRECTED LINE: Ensure this call uses the new method name *****
                        if (IsHeadingWithFormattingCheck(paragraph, defaultFontSize))
                        {
                            // Clear pending list before processing a heading
                            if (currentListItems.Count > 0)
                            {
                                ProcessListItems(currentListItems, structuredDoc, sectionStack);
                                currentListItems.Clear();
                            }
                            ProcessHeading(paragraph, structuredDoc, sectionStack);
                        }
                        else if (isListItem)
                        {
                            currentListItems.Add(paragraph);
                        }
                        else // Regular paragraph
                        {
                            ProcessRegularParagraph(paragraph, structuredDoc, sectionStack);
                        }
                    }
                    else if (element is Table table)
                    {
                        // Process any pending list items before a table
                        if (currentListItems.Count > 0)
                        {
                            ProcessListItems(currentListItems, structuredDoc, sectionStack);
                            currentListItems.Clear();
                        }
                        ProcessTable(table, structuredDoc, sectionStack);
                    }
                    // Handle other element types like SdtBlock (Content Controls) if necessary
                }
                // Process any remaining list items at the end
                if (currentListItems.Count > 0) { ProcessListItems(currentListItems, structuredDoc, sectionStack); }

                _diagnostics.Log(DiagnosticLevel.Info, "WordStructureExtractor", $"Processed {processedElements} body elements resulting in {structuredDoc.Elements.Count} structured elements");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordStructureExtractor", $"Error processing document content: {ex.Message}");
                // Add error element gracefully
                structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Error processing document structure: {ex.Message}]", Metadata = new Dictionary<string, string> { { "Error", "StructureExtractionFailed" } } });
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordDocumentContent (Helper)");
            }
        }

        // --- Private Helper Methods (Modified) ---

        private void ProcessHeading(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string headingText = ExtractTextFromParagraph(paragraph); // Use the simpler text extraction
            if (string.IsNullOrWhiteSpace(headingText)) return;

            int headingLevel = GetHeadingLevel(paragraph); // Get level based on style/outline
            _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing heading level {headingLevel}: \"{headingText}\"");

            // Manage heading stack
            while (sectionStack.Count > 0 && sectionStack.Peek().Level >= headingLevel) { sectionStack.Pop(); }
            sectionStack.Push((headingLevel, headingText));

            string sectionPath = string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading));
            ElementType elementType = headingLevel switch { 1 => ElementType.Heading1, 2 => ElementType.Heading2, _ => ElementType.Heading3 };

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = elementType,
                Text = headingText,
                HeadingLevel = headingLevel,
                SectionPath = sectionPath,
                Metadata = ExtractParagraphMetadata(paragraph) // Add formatting metadata
            });
        }

        private void ProcessRegularParagraph(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string paragraphText = ExtractTextFromParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(paragraphText)) return;

            string sectionPath = sectionStack.Count > 0 ? string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.Paragraph,
                Text = paragraphText,
                SectionPath = sectionPath,
                Metadata = ExtractParagraphMetadata(paragraph) // Add formatting metadata
            });
        }

        private void ProcessListItems(List<Paragraph> listItems, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            if (listItems.Count == 0) return;
            _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing {listItems.Count} list items");

            string sectionPath = sectionStack.Count > 0 ? string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;
            bool isOrdered = DetermineIfOrderedList(listItems); // Remains the same

            foreach (var paragraph in listItems)
            {
                string itemText = ExtractTextFromParagraph(paragraph); // Use simpler text extraction
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    int indentLevel = GetListIndentLevel(paragraph); // Remains the same
                    var metadata = ExtractParagraphMetadata(paragraph); // Get base metadata
                    metadata["IsOrdered"] = isOrdered.ToString();
                    metadata["IndentLevel"] = indentLevel.ToString();
                    // Could add NumFmt (e.g., 'bullet', 'decimal') if needed by parsing NumberingProperties further

                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = ElementType.ListItem,
                        Text = itemText,
                        SectionPath = sectionPath,
                        Metadata = metadata
                    });
                }
            }
        }

        private void ProcessTable(Table table, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            // (Consider enhancing table text extraction to preserve internal paragraph breaks if needed)
            // Current implementation retains basic row/column structure in Markdown format.
            _diagnostics.StartOperation("ProcessWordTable (Helper)");
            try
            {
                var tableContent = new StringBuilder();
                var rows = table.Elements<TableRow>().ToList();
                if (rows.Count == 0) { _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Skipping empty table"); return; }

                bool hasHeader = HasHeaderRow(table); // Logic remains the same
                string sectionPath = sectionStack.Count > 0 ? string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;
                int rowCount = rows.Count;
                int columnCount = rows.Max(r => r.Elements<TableCell>().Count()); // Logic remains the same
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing table: {rowCount} rows, max {columnCount} columns, HasHeader={hasHeader}");

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var cells = row.Elements<TableCell>().ToList();
                    tableContent.Append("| ");
                    // Extract text more carefully, maybe joining paragraphs within a cell with newlines?
                    // For simplicity, keeping the previous approach for now:
                    foreach (var cell in cells) { tableContent.Append(ExtractTextFromTableCell(cell).Replace("|", "\\|").Replace("\n", " ").Replace("\r", "")).Append(" | "); } // Replace newlines for Markdown table
                    // Pad missing cells if row is short
                    tableContent.Append(string.Concat(Enumerable.Repeat(" | ", Math.Max(0, columnCount - cells.Count))));
                    tableContent.AppendLine();

                    if (rowIndex == 0 && hasHeader)
                    {
                        tableContent.Append("|");
                        for (int k = 0; k < columnCount; k++) { tableContent.Append("---|"); }
                        tableContent.AppendLine();
                    }
                }
                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Table,
                    Text = tableContent.ToString(),
                    SectionPath = sectionPath,
                    Metadata = new Dictionary<string, string> {
                        { "RowCount", rowCount.ToString() },
                        { "ColumnCount", columnCount.ToString() },
                        { "HasHeader", hasHeader.ToString() }
                    }
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordStructureExtractor", $"Error processing table: {ex.Message}");
                structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = "[Error processing table structure]", SectionPath = sectionStack.Count > 0 ? string.Join(" / ", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty });
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordTable (Helper)");
            }
        }


        // --- Refined Identification Helpers ---

        // Combined check using Style ID and Formatting
        private bool IsHeadingWithFormattingCheck(Paragraph? paragraph, float? defaultFontSize)
        {
            if (paragraph == null) return false;

            // Priority 1: Check standard Heading styles or Outline Level
            string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (!string.IsNullOrEmpty(styleId))
            {
                if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
                    styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                    styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            var outlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val;
            if (outlineLevel?.HasValue == true && outlineLevel.Value < 9)
            { // Outline levels 0-8 usually map to headings
                return true;
            }

            // Priority 2: Heuristic based on formatting (if default size is known)
            if (defaultFontSize.HasValue)
            {
                float? paraSize = GetParagraphDominantFontSize(paragraph);
                bool isBold = IsParagraphDominantlyBold(paragraph);
                if (paraSize.HasValue && paraSize.Value > defaultFontSize.Value + 2f && isBold) // Example: Bold and > 2pt larger
                {
                    // Additional check: often short
                    if (paragraph.InnerText.Length < 150) return true;
                }
            }

            return false; // Default to false
        }


        // Get heading level, prioritizing OutlineLvl then StyleId
        private int GetHeadingLevel(Paragraph paragraph)
        {
            // Priority 1: Outline Level property
            var outlineLevel = paragraph?.ParagraphProperties?.OutlineLevel?.Val;
            // CORRECTED: Check HasValue before accessing Value to resolve warnings
            if (outlineLevel?.HasValue == true && outlineLevel.Value < 9)
            {
                return outlineLevel.Value + 1; // OutlineLevel is 0-based, return 1-based
            }

            // Priority 2: Style ID
            string? styleId = paragraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (!string.IsNullOrEmpty(styleId))
            {
                if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
                if (styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase)) return 2;
                if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(styleId.AsSpan(7), out int level))
                {
                    return Math.Max(1, level);
                }
            }

            // Fallback if detected via formatting heuristic (assign a default level, e.g., 3)
            return 3;
        }

        // Check if paragraph uses a list style
        private bool HasListStyle(Paragraph paragraph) => paragraph?.ParagraphProperties?.NumberingProperties != null; // Same as before

        // Get list level (check Ilvl first)
        private int GetListIndentLevel(Paragraph paragraph)
        {
            var levelReference = paragraph?.ParagraphProperties?.NumberingProperties?.NumberingLevelReference; // Ilvl
                                                                                                               // CORRECTED: Check HasValue before accessing Value to resolve warnings
            if (levelReference?.Val?.HasValue == true)
            {
                return levelReference.Val.Value + 1; // 0-based -> 1-based
            }
            // Fallback removed for simplicity, rely on Ilvl or default to 1
            return 1; // Default level
        }

        // Check if list is ordered (NumId > 0 implies a specific list definition is used)
        private bool DetermineIfOrderedList(List<Paragraph> listItems)
        {
            if (listItems == null || listItems.Count == 0) return false;
            var firstParaProps = listItems[0].ParagraphProperties;
            var numberingId = firstParaProps?.NumberingProperties?.NumberingId?.Val;
            // Check if numberingId is not null, has a value, and the value is greater than 0
            // (NumId=0 usually means bullet/no specific order from definition)
            // CORRECTED: Check HasValue before accessing Value to resolve warnings
            return numberingId?.HasValue == true && numberingId.Value > 0;
        }

        // Check if table has header row (heuristic remains)
        private bool HasHeaderRow(Table table)
        {
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            // CORRECTED: Null check on firstRow
            if (firstRow?.TableRowProperties?.OfType<TableHeader>().Any() ?? false)
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Table header identified by TableHeader property.");
                return true;
            }
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count < 2) return false;
            var firstRowCells = rows[0].Elements<TableCell>().ToList();
            var secondRowCells = rows[1].Elements<TableCell>().ToList();
            // Use refined IsParagraphDominantlyBold check
            bool firstRowBold = firstRowCells.Any(c => c != null && c.Elements<Paragraph>().Any(p => IsParagraphDominantlyBold(p)));
            bool secondRowBold = secondRowCells.Any(c => c != null && c.Elements<Paragraph>().Any(p => IsParagraphDominantlyBold(p)));
            if (firstRowBold && !secondRowBold)
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Table header identified by bold heuristic.");
                return true;
            }
            return false;
        }

        // Extract basic formatting metadata from paragraph/runs
        private Dictionary<string, string> ExtractParagraphMetadata(Paragraph? paragraph)
        {
            var metadata = new Dictionary<string, string>();
            if (paragraph == null) return metadata;

            // Paragraph level properties
            string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (!string.IsNullOrEmpty(styleId)) metadata["Style"] = styleId;
            var alignmentVal = paragraph.ParagraphProperties?.Justification?.Val;
            if (alignmentVal != null) metadata["Alignment"] = alignmentVal.Value.ToString();
            var outlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val;
            // CORRECTED: Check HasValue before accessing Value to resolve warnings
            if (outlineLevel?.HasValue == true) metadata["OutlineLevel"] = outlineLevel.Value.ToString();

            // Run level properties (dominant style)
            float? fontSize = GetParagraphDominantFontSize(paragraph);
            if (fontSize.HasValue) metadata["FontSize"] = (fontSize.Value / 2f).ToString("F1"); // Font size in points (OpenXML uses half-points)
            if (IsParagraphDominantlyBold(paragraph)) metadata["Bold"] = "true";
            if (IsParagraphDominantlyItalic(paragraph)) metadata["Italic"] = "true";
            // Add more as needed (font name, underline, etc.)

            return metadata;
        }

        // Extract text (simple InnerText for now)
        private string ExtractTextFromParagraph(Paragraph paragraph) => paragraph.InnerText.Trim();

        // Extract text from cell (simple InnerText for now)
        private string ExtractTextFromTableCell(TableCell? cell)
        {
            if (cell == null) return string.Empty;
            // Join paragraph text within the cell
            return string.Join(" ", cell.Elements<Paragraph>().Select(p => p.InnerText.Trim())).Trim();
        }

        // --- NEW Run Property Helpers ---

        // Helper to get dominant font size in a paragraph (size applied to most characters)
        private float? GetParagraphDominantFontSize(Paragraph? p)
        {
            if (p == null) return null;
            return p.Descendants<Run>()
                    .SelectMany(r => r.Descendants<FontSize>()) // Get all FontSize elements
                                                                // CORRECTED: Check HasValue before accessing Value to resolve warnings
                    .Where(fs => fs.Val?.HasValue == true && int.TryParse(fs.Val.Value, out _)) // Ensure Val has value and is parseable
                    .Select(fs => int.Parse(fs.Val!.Value!)) // Parse to int (half-points)
                    .GroupBy(size => size)
                    .OrderByDescending(g => g.Count())
                    .Select(g => (float?)g.Key) // Return float?
                    .FirstOrDefault();
        }

        // Helper to check if a paragraph is dominantly bold
        private bool IsParagraphDominantlyBold(Paragraph? p)
        {
            if (p == null) return false;
            int boldChars = 0;
            int totalChars = 0;
            foreach (var run in p.Descendants<Run>())
            {
                int runChars = run.InnerText.Length;
                totalChars += runChars;
                // Check RunProperties for Bold tag
                if (run.RunProperties?.Bold?.Val?.HasValue ?? false ? run.RunProperties.Bold.Val.Value : run.RunProperties?.Bold != null)
                { // Checks <w:b/> or <w:b w:val="true"/> or <w:b w:val="1"/>
                    boldChars += runChars;
                }
            }
            return totalChars > 0 && (double)boldChars / totalChars > 0.5; // Threshold: >50% bold
        }

        // Helper to check if a paragraph is dominantly italic
        private bool IsParagraphDominantlyItalic(Paragraph? p)
        {
            if (p == null) return false;
            int italicChars = 0;
            int totalChars = 0;
            foreach (var run in p.Descendants<Run>())
            {
                int runChars = run.InnerText.Length;
                totalChars += runChars;
                // Check RunProperties for Italic tag
                if (run.RunProperties?.Italic?.Val?.HasValue ?? false ? run.RunProperties.Italic.Val.Value : run.RunProperties?.Italic != null)
                {
                    italicChars += runChars;
                }
            }
            return totalChars > 0 && (double)italicChars / totalChars > 0.5; // Threshold: >50% italic
        }
    }
}