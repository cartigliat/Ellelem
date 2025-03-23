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

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Processor for Word documents (.docx) using DocumentFormat.OpenXml
    /// </summary>
    public class WordDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public WordDocumentProcessor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public string[] SupportedExtensions => new[] { ".docx" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractTextFromWord");

            try
            {
                // Enhanced validation and logging
                if (string.IsNullOrEmpty(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                        "FilePath is null or empty");
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
                }

                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                        $"File not found: {filePath}");
                    throw new FileNotFoundException($"Word document not found: {filePath}");
                }

                var fileInfo = new FileInfo(filePath);
                _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor",
                    $"Extracting text from Word document: {Path.GetFileName(filePath)} | Size: {fileInfo.Length / 1024.0:F2} KB");

                // Use Task.Run to execute file I/O and processing on a background thread
                return await Task.Run(() =>
                {
                    var textBuilder = new StringBuilder();
                    _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                        "Beginning document processing on background thread");

                    try
                    {
                        using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, false))
                        {
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                "Successfully opened Word document");

                            if (doc.MainDocumentPart?.Document?.Body == null)
                            {
                                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                                    "Document does not contain a body element");
                                return $"[Empty or invalid Word document: {Path.GetFileName(filePath)}]";
                            }

                            // Fixed null reference warning on line 88
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                $"Document has MainDocumentPart: {(doc.MainDocumentPart != null)}, " +
                                $"Document: {(doc.MainDocumentPart != null && doc.MainDocumentPart.Document != null)}, " +
                                $"Body: {(doc.MainDocumentPart != null && doc.MainDocumentPart.Document != null ? doc.MainDocumentPart.Document.Body != null : false)}");

                            // Extract text from the document body
                            int paragraphCount = 0;
                            foreach (var paragraph in doc.MainDocumentPart.Document.Body.Elements<Paragraph>())
                            {
                                var paragraphText = ExtractTextFromParagraph(paragraph);
                                if (!string.IsNullOrWhiteSpace(paragraphText))
                                {
                                    textBuilder.AppendLine(paragraphText);
                                    paragraphCount++;
                                }
                            }
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                $"Processed {paragraphCount} paragraphs from document body");

                            // Also check for tables
                            int tableCount = 0;
                            foreach (var table in doc.MainDocumentPart.Document.Body.Elements<Table>())
                            {
                                ExtractTextFromTable(table, textBuilder);
                                tableCount++;
                            }

                            if (tableCount > 0)
                            {
                                _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                    $"Processed {tableCount} tables from document");
                            }
                        }

                        string result = textBuilder.ToString();
                        _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor",
                            $"Successfully extracted {result.Length} characters from document");
                        return result;
                    }
                    catch (OpenXmlPackageException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"OpenXML package error (possibly corrupt document): {ex.Message}");
                        throw new DocumentProcessingException(
                            $"The Word document appears to be corrupt or invalid: {Path.GetFileName(filePath)}", ex);
                    }
                    catch (FileFormatException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"File format error (may be older .doc format): {ex.Message}");
                        return $"[Unsupported Word document format. Please convert to .docx: {Path.GetFileName(filePath)}]";
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"Unexpected error processing Word document: {ex.Message} | Stack: {ex.StackTrace}");
                        throw new DocumentProcessingException(
                            $"Failed to process Word document: {Path.GetFileName(filePath)}", ex);
                    }
                });
            }
            catch (Exception ex) when (ex is not DocumentProcessingException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                    $"Error extracting text from Word document: {ex.Message} | Stack: {ex.StackTrace}");
                throw new DocumentProcessingException(
                    $"Error extracting text from Word document: {Path.GetFileName(filePath)}", ex);
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

            try
            {
                // Enhanced validation and logging
                if (string.IsNullOrEmpty(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                        "FilePath is null or empty for structured extraction");
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
                }

                if (!File.Exists(filePath))
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                        $"File not found for structured extraction: {filePath}");
                    throw new FileNotFoundException($"Word document not found: {filePath}");
                }

                var fileInfo = new FileInfo(filePath);
                _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor",
                    $"Extracting structured content from Word document: {Path.GetFileName(filePath)} | Size: {fileInfo.Length / 1024.0:F2} KB");

                // Use Task.Run to execute file I/O and processing on a background thread
                return await Task.Run(() =>
                {
                    var structuredDoc = new StructuredDocument
                    {
                        Title = Path.GetFileNameWithoutExtension(filePath)
                    };

                    try
                    {
                        using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, false))
                        {
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                "Successfully opened Word document for structured extraction");

                            if (doc.MainDocumentPart?.Document?.Body == null)
                            {
                                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                                    "Document does not contain a body element for structured extraction");
                                structuredDoc.Elements.Add(new DocumentElement
                                {
                                    Type = ElementType.Paragraph,
                                    Text = $"[Empty or invalid Word document: {Path.GetFileName(filePath)}]"
                                });
                                return structuredDoc;
                            }

                            // Try to extract document metadata first
                            ExtractDocumentTitle(doc, structuredDoc);
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                $"Extracted document title: \"{structuredDoc.Title}\"");

                            // Process the document content
                            ProcessDocumentContent(doc.MainDocumentPart.Document.Body, structuredDoc);
                            _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor",
                                $"Processed document content into {structuredDoc.Elements.Count} structured elements");
                        }

                        return structuredDoc;
                    }
                    catch (OpenXmlPackageException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"OpenXML package error during structured extraction: {ex.Message}");
                        structuredDoc.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.Paragraph,
                            Text = $"[Error: The Word document appears to be corrupt or invalid: {Path.GetFileName(filePath)}]"
                        });
                        return structuredDoc;
                    }
                    catch (FileFormatException ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"File format error (may be older .doc format): {ex.Message}");

                        structuredDoc.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.Paragraph,
                            Text = $"[Unsupported Word document format. Please convert to .docx: {Path.GetFileName(filePath)}]"
                        });
                        return structuredDoc;
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                            $"Unexpected error processing structured document: {ex.Message} | Stack: {ex.StackTrace}");
                        throw new DocumentProcessingException(
                            $"Failed to process structured Word document: {Path.GetFileName(filePath)}", ex);
                    }
                });
            }
            catch (Exception ex) when (ex is not DocumentProcessingException)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                    $"Error extracting structured content from Word document: {ex.Message} | Stack: {ex.StackTrace}");
                throw new DocumentProcessingException(
                    $"Error extracting structured content from Word document: {Path.GetFileName(filePath)}", ex);
            }
            finally
            {
                _diagnostics.EndOperation("ExtractStructuredContentFromWord");
            }
        }

        #region Helper Methods

        private void ExtractDocumentTitle(WordprocessingDocument doc, StructuredDocument structuredDoc)
        {
            try
            {
                _diagnostics.StartOperation("ExtractWordDocumentTitle");

                // Try to get title from core properties
                if (doc.PackageProperties?.Title != null && !string.IsNullOrWhiteSpace(doc.PackageProperties.Title))
                {
                    structuredDoc.Title = doc.PackageProperties.Title;
                    _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                        $"Found title in document properties: \"{doc.PackageProperties.Title}\"");
                    return;
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                        "No title found in document properties");
                }

                // If no title in properties, look for first heading
                if (doc.MainDocumentPart?.Document?.Body != null)
                {
                    var firstHeading = doc.MainDocumentPart.Document.Body
                        .Descendants<Paragraph>()
                        .FirstOrDefault(p => IsHeading(p));

                    if (firstHeading != null)
                    {
                        string headingText = ExtractTextFromParagraph(firstHeading);
                        structuredDoc.Title = headingText;
                        _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                            $"Using first heading as title: \"{headingText}\"");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                            "No headings found in document to use as title");
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                    $"Error extracting document title: {ex.Message}");
                // Fall back to filename (already set in calling method)
            }
            finally
            {
                _diagnostics.EndOperation("ExtractWordDocumentTitle");
            }
        }

        private void ProcessDocumentContent(Body body, StructuredDocument structuredDoc)
        {
            _diagnostics.StartOperation("ProcessWordDocumentContent");

            try
            {
                // Track section hierarchy
                var sectionStack = new Stack<(int Level, string Heading)>();
                var currentList = new List<OpenXmlElement>();
                bool insideList = false;
                int processedElements = 0;

                // Process each element in the body
                foreach (var element in body.ChildElements)
                {
                    processedElements++;

                    if (element is Paragraph paragraph)
                    {
                        // If we were in a list and now found a paragraph, process the accumulated list items
                        if (insideList && currentList.Count > 0)
                        {
                            ProcessListItems(currentList, structuredDoc, sectionStack);
                            currentList.Clear();
                            insideList = false;
                            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                                $"Processed list with {currentList.Count} items");
                        }

                        // Process paragraph based on its style
                        if (IsHeading(paragraph))
                        {
                            ProcessHeading(paragraph, structuredDoc, sectionStack);
                        }
                        else if (HasListStyle(paragraph))
                        {
                            // Start accumulating list items
                            insideList = true;
                            currentList.Add(paragraph);
                        }
                        else
                        {
                            ProcessRegularParagraph(paragraph, structuredDoc, sectionStack);
                        }
                    }
                    else if (element is Table table)
                    {
                        // If we were in a list, process the accumulated list items before handling the table
                        if (insideList && currentList.Count > 0)
                        {
                            ProcessListItems(currentList, structuredDoc, sectionStack);
                            currentList.Clear();
                            insideList = false;
                        }

                        ProcessTable(table, structuredDoc, sectionStack);
                    }
                }

                // Process any remaining list items
                if (insideList && currentList.Count > 0)
                {
                    ProcessListItems(currentList, structuredDoc, sectionStack);
                }

                _diagnostics.Log(DiagnosticLevel.Info, "WordDocumentProcessor",
                    $"Processed {processedElements} document elements resulting in {structuredDoc.Elements.Count} structured elements");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                    $"Error processing document content: {ex.Message}");
                throw; // Let the caller handle this exception
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordDocumentContent");
            }
        }

        private void ProcessHeading(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string headingText = ExtractTextFromParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(headingText))
                return;

            int headingLevel = GetHeadingLevel(paragraph);

            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                $"Processing heading level {headingLevel}: \"{headingText}\"");

            // Pop any headings of equal or lower importance from the stack
            while (sectionStack.Count > 0 && sectionStack.Peek().Level >= headingLevel)
            {
                sectionStack.Pop();
            }

            // Push this heading onto the stack
            sectionStack.Push((headingLevel, headingText));

            // Create the section path
            string sectionPath = string.Join("/", sectionStack.Reverse().Select(s => s.Heading));

            // Determine the element type based on heading level
            ElementType elementType = headingLevel switch
            {
                1 => ElementType.Heading1,
                2 => ElementType.Heading2,
                _ => ElementType.Heading3  // Default for levels 3 and above
            };

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = elementType,
                Text = headingText,
                HeadingLevel = headingLevel,
                SectionPath = sectionPath
            });
        }

        private void ProcessRegularParagraph(Paragraph paragraph, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string paragraphText = ExtractTextFromParagraph(paragraph);
            if (string.IsNullOrWhiteSpace(paragraphText))
                return;

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.Paragraph,
                Text = paragraphText,
                SectionPath = sectionPath,
                Metadata = ExtractParagraphMetadata(paragraph)
            });
        }

        private void ProcessListItems(List<OpenXmlElement> listItems, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            if (listItems.Count == 0)
                return;

            _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                $"Processing {listItems.Count} list items");

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            bool isOrdered = DetermineIfOrderedList(listItems);

            foreach (var item in listItems)
            {
                if (item is Paragraph paragraph)
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
        }

        private void ProcessTable(Table table, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            _diagnostics.StartOperation("ProcessWordTable");

            try
            {
                var tableContent = new StringBuilder();
                var rows = table.Elements<TableRow>().ToList();
                bool hasHeader = HasHeaderRow(table);

                if (rows.Count == 0)
                {
                    _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                        "Skipping empty table (no rows)");
                    return;
                }

                _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                    $"Processing table with {rows.Count} rows | Has header: {hasHeader}");

                string sectionPath = sectionStack.Count > 0
                    ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                    : string.Empty;

                int rowCount = rows.Count;
                int columnCount = rows[0].Elements<TableCell>().Count();

                // Process all rows
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var cells = row.Elements<TableCell>().ToList();

                    if (rowIndex == 0)
                    {
                        // First row (potential header)
                        foreach (var cell in cells)
                        {
                            tableContent.Append("| ").Append(ExtractTextFromTableCell(cell)).Append(" ");
                        }
                        tableContent.AppendLine("|");

                        if (hasHeader)
                        {
                            // Add separator row for markdown-style tables
                            foreach (var _ in cells)
                            {
                                tableContent.Append("| --- ");
                            }
                            tableContent.AppendLine("|");
                        }
                    }
                    else
                    {
                        // Data rows
                        foreach (var cell in cells)
                        {
                            tableContent.Append("| ").Append(ExtractTextFromTableCell(cell)).Append(" ");
                        }
                        tableContent.AppendLine("|");
                    }
                }

                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Table,
                    Text = tableContent.ToString(),
                    SectionPath = sectionPath,
                    Metadata = new Dictionary<string, string>
                    {
                        { "RowCount", rowCount.ToString() },
                        { "ColumnCount", columnCount.ToString() },
                        { "HasHeader", hasHeader.ToString() }
                    }
                });

                _diagnostics.Log(DiagnosticLevel.Debug, "WordDocumentProcessor",
                    $"Table processed successfully: {rowCount} rows, {columnCount} columns");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "WordDocumentProcessor",
                    $"Error processing table: {ex.Message}");

                // Add error element instead of throwing to maintain partial results
                structuredDoc.Elements.Add(new DocumentElement
                {
                    Type = ElementType.Paragraph,
                    Text = "[Error processing table in document]",
                    Metadata = new Dictionary<string, string> { { "Error", "ProcessingFailure" } }
                });
            }
            finally
            {
                _diagnostics.EndOperation("ProcessWordTable");
            }
        }

        private bool IsHeading(Paragraph paragraph)
        {
            if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val != null)
            {
                string styleId = paragraph.ParagraphProperties.ParagraphStyleId.Val.Value;
                return styleId.StartsWith("Heading") || styleId == "Title" || styleId == "Subtitle";
            }

            return false;
        }

        private int GetHeadingLevel(Paragraph paragraph)
        {
            if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val != null)
            {
                string styleId = paragraph.ParagraphProperties.ParagraphStyleId.Val.Value;

                if (styleId == "Title")
                    return 1;
                if (styleId == "Subtitle")
                    return 2;
                if (styleId.StartsWith("Heading") && int.TryParse(styleId.Substring(7), out int level))
                    return level;
            }

            // Default to level 3 if we can't determine the level
            return 3;
        }

        private bool HasListStyle(Paragraph paragraph)
        {
            // Check for numbered list
            if (paragraph.ParagraphProperties?.NumberingProperties?.NumberingId != null)
                return true;

            // Check for bullet points in the text
            var runs = paragraph.Elements<Run>().ToList();
            if (runs.Count > 0)
            {
                var firstRunText = runs[0].InnerText;
                return firstRunText.StartsWith("•") || firstRunText.StartsWith("-") || firstRunText.StartsWith("*");
            }

            return false;
        }

        private int GetListIndentLevel(Paragraph paragraph)
        {
            if (paragraph.ParagraphProperties?.Indentation?.Left != null)
            {
                if (int.TryParse(paragraph.ParagraphProperties.Indentation.Left.Value, out int indentValue))
                {
                    // Roughly estimate indent level based on common indent values
                    return indentValue / 720 + 1; // 720 twips = 1/2 inch, common indent
                }
            }

            // If no indent specified or couldn't parse, assume level 1
            return 1;
        }

        private bool DetermineIfOrderedList(List<OpenXmlElement> listItems)
        {
            // Check for NumberingProperties or look for patterns like "1.", "2.", etc.
            foreach (var item in listItems)
            {
                if (item is Paragraph paragraph)
                {
                    if (paragraph.ParagraphProperties?.NumberingProperties?.NumberingId != null)
                        return true;

                    var text = ExtractTextFromParagraph(paragraph).Trim();
                    if (text.Length > 2 && char.IsDigit(text[0]) && text[1] == '.')
                        return true;
                }
            }
            return false;
        }

        private bool HasHeaderRow(Table table)
        {
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow == null)
                return false;

            // Check if the table has at least two rows
            var secondRow = table.Elements<TableRow>().Skip(1).FirstOrDefault();
            if (secondRow == null)
                return false; // Need at least 2 rows to determine if first is a header

            // Compare formatting between first and second rows
            var firstRowCells = firstRow.Elements<TableCell>().ToList();
            var secondRowCells = secondRow.Elements<TableCell>().ToList();

            // 1. Check if first row has bold formatting but second doesn't
            bool firstRowHasBold = firstRowCells.Any(cell => cell.Descendants<Bold>().Any());
            bool secondRowHasBold = secondRowCells.Any(cell => cell.Descendants<Bold>().Any());
            if (firstRowHasBold && !secondRowHasBold)
                return true;

            // 2. Check if first row has different run properties (font, size, etc.)
            bool firstRowHasRunProps = firstRowCells.Any(cell =>
                cell.Descendants<RunProperties>().Any(rp =>
                    rp.Bold != null || rp.Italic != null || rp.Color != null));
            bool secondRowHasRunProps = secondRowCells.Any(cell =>
                cell.Descendants<RunProperties>().Any(rp =>
                    rp.Bold != null || rp.Italic != null || rp.Color != null));
            if (firstRowHasRunProps && !secondRowHasRunProps)
                return true;

            // 3. Fall back to heuristic - if first row is centered and others aren't, likely a header
            bool firstRowCentered = firstRowCells.Any(cell =>
                cell.Descendants<Paragraph>().Any(p =>
                    p.ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center));

            return firstRowCentered;
        }

        private Dictionary<string, string> ExtractParagraphMetadata(Paragraph paragraph)
        {
            var metadata = new Dictionary<string, string>();

            if (paragraph?.ParagraphProperties != null)
            {
                // Extract style information - Fixed null reference warning on lines 602-603
                if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val != null)
                {
                    string styleId = paragraph.ParagraphProperties.ParagraphStyleId.Val.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(styleId))
                    {
                        metadata["Style"] = styleId;
                    }
                }

                // Extract alignment information - Fixed null reference warning on lines 613-619
                if (paragraph.ParagraphProperties?.Justification?.Val != null)
                {
                    string alignment = paragraph.ParagraphProperties.Justification.Val.Value.ToString();
                    metadata["Alignment"] = alignment;
                }

                // Extract formatting information
                bool hasBold = paragraph.Descendants<Bold>().Any();
                bool hasItalic = paragraph.Descendants<Italic>().Any();
                bool hasUnderline = paragraph.Descendants<Underline>().Any();

                if (hasBold) metadata["Bold"] = "true";
                if (hasItalic) metadata["Italic"] = "true";
                if (hasUnderline) metadata["Underline"] = "true";
            }

            return metadata;
        }

        private string ExtractTextFromParagraph(Paragraph paragraph)
        {
            try
            {
                var sb = new StringBuilder();

                foreach (var run in paragraph.Elements<Run>())
                {
                    string text = run.InnerText;
                    sb.Append(text);
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                    $"Error extracting text from paragraph: {ex.Message}");
                return "[Error extracting paragraph text]";
            }
        }

        private void ExtractTextFromTable(Table table, StringBuilder textBuilder)
        {
            try
            {
                textBuilder.AppendLine("\nTable:");

                foreach (var row in table.Elements<TableRow>())
                {
                    foreach (var cell in row.Elements<TableCell>())
                    {
                        textBuilder.Append(ExtractTextFromTableCell(cell)).Append(" | ");
                    }
                    textBuilder.AppendLine();
                }

                textBuilder.AppendLine();
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                    $"Error extracting text from table: {ex.Message}");
                textBuilder.AppendLine("[Error extracting table content]");
            }
        }

        private string ExtractTextFromTableCell(TableCell cell)
        {
            try
            {
                var sb = new StringBuilder();

                foreach (var paragraph in cell.Elements<Paragraph>())
                {
                    sb.AppendLine(ExtractTextFromParagraph(paragraph));
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "WordDocumentProcessor",
                    $"Error extracting text from table cell: {ex.Message}");
                return "[Error]";
            }
        }

        #endregion
    }
}