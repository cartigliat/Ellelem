using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.Diagnostics;
using System.Linq;

namespace ollamidesk.RAG.DocumentProcessors.Implementations
{
    /// <summary>
    /// Processor for plain text files (.txt)
    /// </summary>
    public class TextDocumentProcessor : IDocumentProcessor
    {
        private readonly RagDiagnosticsService _diagnostics;

        public TextDocumentProcessor(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public string[] SupportedExtensions => new[] { ".txt" };

        public bool CanProcess(string fileExtension)
        {
            return Array.IndexOf(SupportedExtensions, fileExtension.ToLowerInvariant()) >= 0;
        }

        public async Task<string> ExtractTextAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractTextFromTxt");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "TextDocumentProcessor",
                    $"Reading text file: {Path.GetFileName(filePath)}");

                string content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

                _diagnostics.Log(DiagnosticLevel.Info, "TextDocumentProcessor",
                    $"Successfully read {content.Length} characters from text file");

                return content;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "TextDocumentProcessor",
                    $"Error reading text file: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractTextFromTxt");
            }
        }

        public bool SupportsStructuredExtraction => true;

        public async Task<StructuredDocument> ExtractStructuredContentAsync(string filePath)
        {
            _diagnostics.StartOperation("ExtractStructuredContentFromTxt");

            try
            {
                _diagnostics.Log(DiagnosticLevel.Info, "TextDocumentProcessor",
                    $"Extracting structured content from {Path.GetFileName(filePath)}");

                string content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

                var structuredDoc = new StructuredDocument
                {
                    Title = Path.GetFileNameWithoutExtension(filePath)
                };

                // Try to identify structure based on common patterns
                string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // Check if the first non-empty line could be a title
                string? potentialTitle = null;
                int titleLineIndex = -1;

                for (int i = 0; i < Math.Min(5, lines.Length); i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        potentialTitle = lines[i].Trim();
                        titleLineIndex = i;
                        break;
                    }
                }

                // Check if the next line is a separator (like ===== or -----)
                bool hasTitleSeparator = false;

                if (titleLineIndex >= 0 && titleLineIndex + 1 < lines.Length)
                {
                    string nextLine = lines[titleLineIndex + 1].Trim();
                    hasTitleSeparator = Regex.IsMatch(nextLine, @"^[=\-]{3,}$");
                }

                if (potentialTitle != null && (hasTitleSeparator || potentialTitle.Length < 100))
                {
                    structuredDoc.Title = potentialTitle;
                    int startIndex = hasTitleSeparator ? titleLineIndex + 2 : titleLineIndex + 1;

                    // Process the rest of the content
                    ProcessTextContent(lines, startIndex, structuredDoc);
                }
                else
                {
                    // No clear title, process all content
                    ProcessTextContent(lines, 0, structuredDoc);
                }

                _diagnostics.Log(DiagnosticLevel.Info, "TextDocumentProcessor",
                    $"Created structured document with {structuredDoc.Elements.Count} elements");

                return structuredDoc;
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "TextDocumentProcessor",
                    $"Error extracting structured content: {ex.Message}");
                throw;
            }
            finally
            {
                _diagnostics.EndOperation("ExtractStructuredContentFromTxt");
            }
        }

        private void ProcessTextContent(string[] lines, int startIndex, StructuredDocument document)
        {
            var currentParagraph = new System.Text.StringBuilder();
            bool inCodeBlock = false;

            for (int i = startIndex; i < lines.Length; i++)
            {
                string line = lines[i];

                // Check for code block delimiter
                if (line.Trim() == "```")
                {
                    if (inCodeBlock)
                    {
                        // End of code block
                        document.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.CodeBlock,
                            Text = currentParagraph.ToString()
                        });
                        currentParagraph.Clear();
                    }
                    else
                    {
                        // Start of code block - add any pending paragraph
                        if (currentParagraph.Length > 0)
                        {
                            document.Elements.Add(new DocumentElement
                            {
                                Type = ElementType.Paragraph,
                                Text = currentParagraph.ToString().Trim()
                            });
                            currentParagraph.Clear();
                        }
                    }

                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                // If we're in a code block, just append the line
                if (inCodeBlock)
                {
                    currentParagraph.AppendLine(line);
                    continue;
                }

                // Check for section headings (# Style)
                if (Regex.IsMatch(line, @"^#{1,6}\s+.+"))
                {
                    // Add any pending paragraph
                    if (currentParagraph.Length > 0)
                    {
                        document.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.Paragraph,
                            Text = currentParagraph.ToString().Trim()
                        });
                        currentParagraph.Clear();
                    }

                    int level = line.TakeWhile(c => c == '#').Count();
                    string headingText = line.Substring(level).Trim();

                    var elementType = level switch
                    {
                        1 => ElementType.Heading1,
                        2 => ElementType.Heading2,
                        _ => ElementType.Heading3  // Default for levels 3-6
                    };

                    document.Elements.Add(new DocumentElement
                    {
                        Type = elementType,
                        Text = headingText,
                        HeadingLevel = level
                    });

                    continue;
                }

                // Check for list items
                if (Regex.IsMatch(line, @"^\s*[\*\-\+]\s+.+"))
                {
                    // Add any pending paragraph
                    if (currentParagraph.Length > 0)
                    {
                        document.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.Paragraph,
                            Text = currentParagraph.ToString().Trim()
                        });
                        currentParagraph.Clear();
                    }

                    document.Elements.Add(new DocumentElement
                    {
                        Type = ElementType.ListItem,
                        Text = Regex.Replace(line, @"^\s*[\*\-\+]\s+", "").Trim()
                    });

                    continue;
                }

                // Handle paragraph breaks
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentParagraph.Length > 0)
                    {
                        document.Elements.Add(new DocumentElement
                        {
                            Type = ElementType.Paragraph,
                            Text = currentParagraph.ToString().Trim()
                        });
                        currentParagraph.Clear();
                    }
                }
                else
                {
                    // Add to current paragraph
                    currentParagraph.AppendLine(line);
                }
            }

            // Add any remaining paragraph content
            if (currentParagraph.Length > 0)
            {
                document.Elements.Add(new DocumentElement
                {
                    Type = inCodeBlock ? ElementType.CodeBlock : ElementType.Paragraph,
                    Text = currentParagraph.ToString().Trim()
                });
            }
        }
    }
}