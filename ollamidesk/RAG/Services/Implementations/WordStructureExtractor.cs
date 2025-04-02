// ollamidesk/RAG/Services/Implementations/WordStructureExtractor.cs
// Corrected version - Implements Interface and uses correct namespace
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Services.Interfaces; // <--- Ensure this using is present

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    //          vvvvvvvvvvvvvvvvvvvvvvv --------> ADDED: Interface implementation
    public class WordStructureExtractor : IWordStructureExtractor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public WordStructureExtractor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        // ExtractStructure method and helpers remain the same
        public void ExtractStructure(Body? body, StructuredDocument structuredDoc)
        {
            // ... (rest of ExtractStructure method remains the same) ...
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

                foreach (var element in body.ChildElements)
                {
                    processedElements++;
                    if (element is Paragraph paragraph)
                    {
                        bool isListItem = HasListStyle(paragraph);
                        if (!isListItem && currentListItems.Count > 0)
                        {
                            ProcessListItems(currentListItems, structuredDoc, sectionStack);
                            currentListItems.Clear();
                        }

                        if (IsHeading(paragraph)) { ProcessHeading(paragraph, structuredDoc, sectionStack); }
                        else if (isListItem) { currentListItems.Add(paragraph); }
                        else { ProcessRegularParagraph(paragraph, structuredDoc, sectionStack); }
                    }
                    else if (element is Table table)
                    {
                        if (currentListItems.Count > 0)
                        {
                            ProcessListItems(currentListItems, structuredDoc, sectionStack);
                            currentListItems.Clear();
                        }
                        ProcessTable(table, structuredDoc, sectionStack);
                    }
                }
                if (currentListItems.Count > 0) { ProcessListItems(currentListItems, structuredDoc, sectionStack); }

                _diagnostics.Log(DiagnosticLevel.Info, "WordStructureExtractor", $"Processed {processedElements} body elements resulting in {structuredDoc.Elements.Count} structured elements");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordStructureExtractor", $"Error processing document content: {ex.Message}");
                structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = $"[Error processing document structure: {ex.Message}]", Metadata = new Dictionary<string, string> { { "Error", "StructureExtractionFailed" } } });
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordDocumentContent (Helper)");
            }
        }

        // --- Private Helper Methods ---
        // ProcessHeading, ProcessRegularParagraph, ProcessTable, etc remain the same...
        private void ProcessHeading(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string headingText = ExtractTextFromParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(headingText)) return;
            int headingLevel = GetHeadingLevel(paragraph);
            _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing heading level {headingLevel}: \"{headingText}\"");

            while (sectionStack.Count > 0 && sectionStack.Peek().Level >= headingLevel) { sectionStack.Pop(); }
            sectionStack.Push((headingLevel, headingText));

            string sectionPath = string.Join("/", sectionStack.Reverse().Select(s => s.Heading));
            ElementType elementType = headingLevel switch { 1 => ElementType.Heading1, 2 => ElementType.Heading2, _ => ElementType.Heading3 };
            structuredDoc.Elements.Add(new DocumentElement { Type = elementType, Text = headingText, HeadingLevel = headingLevel, SectionPath = sectionPath });
        }

        private void ProcessRegularParagraph(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string paragraphText = ExtractTextFromParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(paragraphText)) return;
            string sectionPath = sectionStack.Count > 0 ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;
            structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = paragraphText, SectionPath = sectionPath, Metadata = ExtractParagraphMetadata(paragraph) });
        }

        private void ProcessListItems(List<Paragraph> listItems, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            if (listItems.Count == 0) return;
            _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing {listItems.Count} list items");
            string sectionPath = sectionStack.Count > 0 ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;
            bool isOrdered = DetermineIfOrderedList(listItems);

            foreach (var paragraph in listItems)
            {
                string itemText = ExtractTextFromParagraph(paragraph);
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    int indentLevel = GetListIndentLevel(paragraph);
                    structuredDoc.Elements.Add(new DocumentElement
                    {
                        Type = ElementType.ListItem,
                        Text = itemText,
                        SectionPath = sectionPath,
                        Metadata = new Dictionary<string, string>
                        {
                            { "IsOrdered", isOrdered.ToString() },
                            { "IndentLevel", indentLevel.ToString() }
                        }
                    });
                }
            }
        }

        private void ProcessTable(Table table, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            _diagnostics.StartOperation("ProcessWordTable (Helper)");
            try
            {
                var tableContent = new StringBuilder();
                var rows = table.Elements<TableRow>().ToList();
                if (rows.Count == 0) { _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Skipping empty table"); return; }

                bool hasHeader = HasHeaderRow(table);
                string sectionPath = sectionStack.Count > 0 ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty;
                int rowCount = rows.Count;
                int columnCount = rows.Max(r => r.Elements<TableCell>().Count());
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", $"Processing table: {rowCount} rows, max {columnCount} columns, HasHeader={hasHeader}");

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var cells = row.Elements<TableCell>().ToList();
                    tableContent.Append("| ");
                    foreach (var cell in cells) { tableContent.Append(ExtractTextFromTableCell(cell).Replace("|", "\\|")).Append(" | "); }
                    tableContent.Append(string.Concat(Enumerable.Repeat(" | ", columnCount - cells.Count)));
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
                    Metadata = new Dictionary<string, string> { { "RowCount", rowCount.ToString() }, { "ColumnCount", columnCount.ToString() }, { "HasHeader", hasHeader.ToString() } }
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordStructureExtractor", $"Error processing table: {ex.Message}");
                structuredDoc.Elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = "[Error processing table structure]", SectionPath = sectionStack.Count > 0 ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading)) : string.Empty });
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordTable (Helper)");
            }
        }


        private bool IsHeading(Paragraph? paragraph)
        {
            string? styleId = paragraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (string.IsNullOrEmpty(styleId)) return false;
            return styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) || styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase);
        }

        private int GetHeadingLevel(Paragraph paragraph)
        {
            string? styleId = paragraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (string.IsNullOrEmpty(styleId)) return 9;
            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
            if (styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase)) return 2;
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(styleId.AsSpan(7), out int level)) { return Math.Max(1, level); }
            return 9;
        }

        private bool HasListStyle(Paragraph paragraph)
        {
            return paragraph?.ParagraphProperties?.NumberingProperties != null;
        }

        private int GetListIndentLevel(Paragraph paragraph)
        {
            // Use Ilvl (NumberingLevelReference) which holds the 0-based list level index.
            var levelReference = paragraph?.ParagraphProperties?.NumberingProperties?.NumberingLevelReference; // This is Ilvl element

            // CORRECTED: Check HasValue before accessing Value to resolve warnings
            if (levelReference?.Val?.HasValue ?? false)
            {
                // Level is 0-based, return 1-based for clarity
                return levelReference.Val.Value + 1;
            }

            // Fallback to Left indentation
            if (int.TryParse(paragraph?.ParagraphProperties?.Indentation?.Left?.Value, out int indentValue))
            {
                return Math.Max(1, (indentValue / 720) + 1); // Approximation
            }
            return 1; // Default level
        }

        private bool DetermineIfOrderedList(List<Paragraph> listItems)
        {
            if (listItems == null || listItems.Count == 0) return false;
            var firstParaProps = listItems[0].ParagraphProperties;

            // CORRECTED: Safely check NumberingId and its Value property using HasValue
            var numberingId = firstParaProps?.NumberingProperties?.NumberingId?.Val;
            // Check if numberingId is not null, has a value, and the value is greater than 0
            if (numberingId?.HasValue == true && numberingId.Value > 0)
            {
                return true;
            }
            return false;
        }

        private bool HasHeaderRow(Table table)
        {
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow?.TableRowProperties?.OfType<TableHeader>().Any() ?? false)
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Table header identified by TableHeader property.");
                return true;
            }
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count < 2) return false;
            var firstRowCells = rows[0].Elements<TableCell>().ToList();
            var secondRowCells = rows[1].Elements<TableCell>().ToList();
            bool firstRowBold = firstRowCells.Any(c => c != null && c.Descendants<Bold>().Any());
            bool secondRowBold = secondRowCells.Any(c => c != null && c.Descendants<Bold>().Any());
            if (firstRowBold && !secondRowBold)
            {
                _diagnostics.Log(DiagnosticLevel.Debug, "WordStructureExtractor", "Table header identified by bold heuristic.");
                return true;
            }
            return false;
        }

        private Dictionary<string, string> ExtractParagraphMetadata(Paragraph? paragraph)
        {
            var metadata = new Dictionary<string, string>();
            if (paragraph == null) return metadata;
            string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (!string.IsNullOrEmpty(styleId)) metadata["Style"] = styleId;
            var alignmentVal = paragraph.ParagraphProperties?.Justification?.Val;
            if (alignmentVal != null) metadata["Alignment"] = alignmentVal.Value.ToString();
            if (paragraph.Descendants<Bold>().Any()) metadata["Bold"] = "true";
            if (paragraph.Descendants<Italic>().Any()) metadata["Italic"] = "true";
            if (paragraph.Descendants<Underline>().Any()) metadata["Underline"] = "true";
            return metadata;
        }

        private string ExtractTextFromParagraph(Paragraph paragraph)
        {
            return paragraph.InnerText.Trim();
        }

        private string ExtractTextFromTableCell(TableCell? cell)
        {
            if (cell == null) return string.Empty;
            return string.Join(" ", cell.Elements<Paragraph>().Select(p => p.InnerText.Trim())).Trim();
        }

    }
}