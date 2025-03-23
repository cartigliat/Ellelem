using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Enhanced processor for Markdown files using the Markdig library
    /// </summary>
    public class MarkdownDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;
        private readonly MarkdownPipeline _pipeline;

        public MarkdownDocumentProcessor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Configure Markdig pipeline with all common extensions
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions() // Enables tables, task lists, custom containers, etc.
                .UsePreciseSourceLocation()
                .Build();
        }

        public string[] SupportedExtensions => new[] { ".md", ".markdown" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractTextFromMarkdown");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "MarkdownDocumentProcessor",
                    $"Reading markdown file: {Path.GetFileName(filePath)}");

                string markdown = await File.ReadAllTextAsync(filePath);

                // Parse markdown to HTML then strip HTML tags for plain text
                string plainText = ConvertMarkdownToPlainText(markdown);

                _diagnostics.Log(DiagnosticLevel.Info, "MarkdownDocumentProcessor",
                    $"Successfully converted markdown to {plainText.Length} characters of plain text");

                return plainText;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "MarkdownDocumentProcessor",
                    $"Error reading markdown file: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractTextFromMarkdown");
            }
        }

        public bool SupportsStructuredExtraction => true;

        public async Task<StructuredDocument> ExtractStructuredContentAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractStructuredContentFromMarkdown");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "MarkdownDocumentProcessor",
                    $"Extracting structured content from {Path.GetFileName(filePath)}");

                string markdown = await File.ReadAllTextAsync(filePath);

                // Parse the markdown into an abstract syntax tree
                MarkdownDocument document = Markdig.Markdown.Parse(markdown, _pipeline);

                var structuredDoc = new StructuredDocument
                {
                    Title = ExtractTitle(document) ?? Path.GetFileNameWithoutExtension(filePath)
                };

                // Process document into structural elements with hierarchical context
                ProcessMarkdownDocument(document, structuredDoc);

                _diagnostics.Log(DiagnosticLevel.Info, "MarkdownDocumentProcessor",
                    $"Created structured document with {structuredDoc.Elements.Count} elements");

                return structuredDoc;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "MarkdownDocumentProcessor",
                    $"Error extracting structured content: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractStructuredContentFromMarkdown");
            }
        }

        private string ConvertMarkdownToPlainText(string markdown)
        {
            // Parse markdown to an AST
            MarkdownDocument document = Markdig.Markdown.Parse(markdown, _pipeline);

            // Convert AST to plain text
            return ExtractPlainTextFromDocument(document);
        }

        private string? ExtractTitle(MarkdownDocument document)
        {
            // Look for the first heading as the title
            var headingBlock = document.Descendants<HeadingBlock>().FirstOrDefault();
            if (headingBlock != null && headingBlock.Level == 1)
            {
                return ExtractTextFromInlineContainer(headingBlock.Inline);
            }

            return null;
        }

        private void ProcessMarkdownDocument(MarkdownDocument document, StructuredDocument structuredDoc)
        {
            // Track section hierarchy using a stack of headings
            var sectionStack = new Stack<(int Level, string Heading)>();

            // Process each block in the markdown document
            foreach (var block in document)
            {
                if (block is HeadingBlock headingBlock)
                {
                    ProcessHeading(headingBlock, structuredDoc, sectionStack);
                }
                else if (block is ParagraphBlock paragraphBlock)
                {
                    ProcessParagraph(paragraphBlock, structuredDoc, sectionStack);
                }
                else if (block is ListBlock listBlock)
                {
                    ProcessList(listBlock, structuredDoc, sectionStack);
                }
                else if (block is FencedCodeBlock codeBlock)
                {
                    ProcessCodeBlock(codeBlock, structuredDoc, sectionStack);
                }
                else if (block is QuoteBlock quoteBlock)
                {
                    ProcessQuoteBlock(quoteBlock, structuredDoc, sectionStack);
                }
                else if (block is Markdig.Extensions.Tables.Table tableBlock)
                {
                    ProcessTable(tableBlock, structuredDoc, sectionStack);
                }
                // Add more block types as needed
            }
        }

        private void ProcessHeading(HeadingBlock headingBlock, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string headingText = ExtractTextFromInlineContainer(headingBlock.Inline);

            // Pop any headings of equal or lower importance from the stack
            while (sectionStack.Count > 0 && sectionStack.Peek().Level >= headingBlock.Level)
            {
                sectionStack.Pop();
            }

            // Push this heading onto the stack
            sectionStack.Push((headingBlock.Level, headingText));

            // Create the section path
            string sectionPath = string.Join("/", sectionStack.Reverse().Select(s => s.Heading));

            // Determine the element type based on heading level
            ElementType elementType = headingBlock.Level switch
            {
                1 => ElementType.Heading1,
                2 => ElementType.Heading2,
                _ => ElementType.Heading3
            };

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = elementType,
                Text = headingText,
                HeadingLevel = headingBlock.Level,
                SectionPath = sectionPath
            });
        }

        private void ProcessParagraph(ParagraphBlock paragraphBlock, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string paragraphText = ExtractTextFromInlineContainer(paragraphBlock.Inline);
            if (string.IsNullOrWhiteSpace(paragraphText))
                return;

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.Paragraph,
                Text = paragraphText,
                SectionPath = sectionPath
            });
        }

        private void ProcessList(ListBlock listBlock, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            foreach (var item in listBlock)
            {
                if (item is ListItemBlock listItem)
                {
                    // Extract text from each list item
                    string itemText = ExtractTextFromListItem(listItem);
                    if (!string.IsNullOrWhiteSpace(itemText))
                    {
                        structuredDoc.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.ListItem,
                            Text = itemText,
                            SectionPath = sectionPath,
                            Metadata = new Dictionary<string, string>
                            {
                                { "IsOrdered", listBlock.IsOrdered.ToString() },
                                { "BulletType", listBlock.BulletType.ToString() }
                            }
                        });
                    }
                }
            }
        }

        private void ProcessCodeBlock(FencedCodeBlock codeBlock, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string code = codeBlock.Lines.ToString();
            if (string.IsNullOrWhiteSpace(code))
                return;

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(codeBlock.Info))
            {
                metadata["Language"] = codeBlock.Info;
            }

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.CodeBlock,
                Text = code,
                SectionPath = sectionPath,
                Metadata = metadata
            });
        }

        private void ProcessQuoteBlock(QuoteBlock quoteBlock, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            string quoteText = ExtractTextFromQuoteBlock(quoteBlock);
            if (string.IsNullOrWhiteSpace(quoteText))
                return;

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.Quote,
                Text = quoteText,
                SectionPath = sectionPath
            });
        }

        private void ProcessTable(Markdig.Extensions.Tables.Table table, StructuredDocument structuredDoc, Stack<(int Level, string Heading)> sectionStack)
        {
            var tableText = new System.Text.StringBuilder();
            bool hasHeader = false;

            // Process header row
            var tableRows = table.ToList(); // Convert to list for safer access

            if (tableRows.Count > 0 && tableRows[0] is Markdig.Extensions.Tables.TableRow headerRow)
            {
                hasHeader = true;
                tableText.Append("| ");
                foreach (var cell in headerRow)
                {
                    if (cell is Markdig.Extensions.Tables.TableCell headerCell)
                    {
                        string cellText = ExtractTextFromTableCell(headerCell);
                        tableText.Append(cellText).Append(" | ");
                    }
                }
                tableText.AppendLine();
                tableText.Append("| ");

                // Get the count of cells in the header row
                var headerCells = headerRow.ToList();
                for (int i = 0; i < headerCells.Count; i++)
                {
                    tableText.Append("--- | ");
                }
                tableText.AppendLine();
            }

            // Process data rows
            for (int i = hasHeader ? 1 : 0; i < tableRows.Count; i++)
            {
                if (tableRows[i] is Markdig.Extensions.Tables.TableRow row)
                {
                    tableText.Append("| ");
                    foreach (var cell in row)
                    {
                        if (cell is Markdig.Extensions.Tables.TableCell dataCell)
                        {
                            string cellText = ExtractTextFromTableCell(dataCell);
                            tableText.Append(cellText).Append(" | ");
                        }
                    }
                    tableText.AppendLine();
                }
            }

            string sectionPath = sectionStack.Count > 0
                ? string.Join("/", sectionStack.Reverse().Select(s => s.Heading))
                : string.Empty;

            // Fix for line 354 error - properly count cells in the first row
            var cellCount = "0";
            if (tableRows.Count > 0)
            {
                var firstRow = tableRows[0] as Markdig.Extensions.Tables.TableRow;
                if (firstRow != null)
                {
                    cellCount = firstRow.Count().ToString();
                }
            }

            structuredDoc.Elements.Add(new DocumentElement
            {
                Type = ElementType.Table,
                Text = tableText.ToString(),
                SectionPath = sectionPath,
                Metadata = new Dictionary<string, string>
                {
                    { "HasHeader", hasHeader.ToString() },
                    { "RowCount", tableRows.Count.ToString() },
                    { "ColumnCount", cellCount }
                }
            });
        }

        #region Helper Methods

        private string ExtractPlainTextFromDocument(MarkdownDocument document)
        {
            var textBuilder = new System.Text.StringBuilder();

            foreach (var block in document)
            {
                ExtractPlainTextFromBlock(block, textBuilder);
                textBuilder.AppendLine();
            }

            return textBuilder.ToString();
        }

        private void ExtractPlainTextFromBlock(Block block, System.Text.StringBuilder textBuilder)
        {
            if (block is HeadingBlock headingBlock)
            {
                string heading = ExtractTextFromInlineContainer(headingBlock.Inline);
                textBuilder.AppendLine(heading);
                textBuilder.AppendLine();
            }
            else if (block is ParagraphBlock paragraphBlock)
            {
                string paragraph = ExtractTextFromInlineContainer(paragraphBlock.Inline);
                textBuilder.AppendLine(paragraph);
            }
            else if (block is ListBlock listBlock)
            {
                foreach (var item in listBlock)
                {
                    if (item is ListItemBlock listItem)
                    {
                        string prefix = listBlock.IsOrdered ? "* " : "- ";
                        textBuilder.Append(prefix).AppendLine(ExtractTextFromListItem(listItem));
                    }
                }
            }
            else if (block is FencedCodeBlock codeBlock)
            {
                textBuilder.AppendLine("[Code Block]");
                textBuilder.AppendLine(codeBlock.Lines.ToString());
            }
            else if (block is QuoteBlock quoteBlock)
            {
                textBuilder.AppendLine(ExtractTextFromQuoteBlock(quoteBlock));
            }
            else if (block is Markdig.Extensions.Tables.Table table)
            {
                textBuilder.AppendLine("[Table]");
                foreach (var row in table)
                {
                    if (row is Markdig.Extensions.Tables.TableRow tableRow)
                    {
                        foreach (var cell in tableRow)
                        {
                            if (cell is Markdig.Extensions.Tables.TableCell tableCell)
                            {
                                textBuilder.Append(ExtractTextFromTableCell(tableCell)).Append(" | ");
                            }
                        }
                        textBuilder.AppendLine();
                    }
                }
            }
            else if (block is ContainerBlock containerBlock)
            {
                // Recursively process container blocks (like quote blocks)
                foreach (var childBlock in containerBlock)
                {
                    ExtractPlainTextFromBlock(childBlock, textBuilder);
                }
            }
        }

        private string ExtractTextFromInlineContainer(ContainerInline? container)
        {
            if (container == null)
                return string.Empty;

            var textBuilder = new System.Text.StringBuilder();

            foreach (var inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    textBuilder.Append(literal.Content);
                }
                else if (inline is EmphasisInline emphasis)
                {
                    textBuilder.Append(ExtractTextFromInlineContainer(emphasis));
                }
                else if (inline is LineBreakInline)
                {
                    textBuilder.AppendLine();
                }
                else if (inline is LinkInline link)
                {
                    // For links, we want the text not the URL
                    textBuilder.Append(ExtractTextFromInlineContainer(link));
                }
                else if (inline is CodeInline code)
                {
                    textBuilder.Append(code.Content);
                }
                else if (inline is ContainerInline container2)
                {
                    textBuilder.Append(ExtractTextFromInlineContainer(container2));
                }
            }

            return textBuilder.ToString();
        }

        private string ExtractTextFromListItem(ListItemBlock listItem)
        {
            var textBuilder = new System.Text.StringBuilder();

            foreach (var block in listItem)
            {
                if (block is ParagraphBlock paragraphBlock)
                {
                    textBuilder.Append(ExtractTextFromInlineContainer(paragraphBlock.Inline));
                }
                else
                {
                    ExtractPlainTextFromBlock(block, textBuilder);
                }
            }

            return textBuilder.ToString();
        }

        private string ExtractTextFromQuoteBlock(QuoteBlock quoteBlock)
        {
            var textBuilder = new System.Text.StringBuilder();

            foreach (var block in quoteBlock)
            {
                ExtractPlainTextFromBlock(block, textBuilder);
            }

            return textBuilder.ToString();
        }

        private string ExtractTextFromTableCell(Markdig.Extensions.Tables.TableCell cell)
        {
            var textBuilder = new System.Text.StringBuilder();

            foreach (var block in cell)
            {
                ExtractPlainTextFromBlock(block, textBuilder);
            }

            return textBuilder.ToString().Trim();
        }

        #endregion
    }
}