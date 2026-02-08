using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VibePlatform.Editor;

public class MarkdownColorizingTransformer : DocumentColorizingTransformer
{
    private MarkdownDocument? _ast;
    private string? _text;

    public void UpdateAst(MarkdownDocument? ast)
    {
        _ast = ast;
    }

    public void UpdateAst(MarkdownDocument? ast, string text)
    {
        _ast = ast;
        _text = text;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_ast == null) return;

        try
        {
            int lineStart = line.Offset;
            int lineEnd = line.EndOffset;

            foreach (var block in _ast)
            {
                if (block.Span.End < lineStart) continue;
                if (block.Span.Start > lineEnd) break;

                if (block is HeadingBlock heading)
                {
                    ColorizeHeading(line, heading);
                }

                if (block is ParagraphBlock paragraph && paragraph.Inline != null)
                {
                    ColorizeInlines(line, paragraph.Inline);
                }
                else if (block is HeadingBlock headingWithInline && headingWithInline.Inline != null)
                {
                    ColorizeInlines(line, headingWithInline.Inline);
                }
            }
        }
        catch
        {
            // Prevent stale-AST edge cases from crashing the app.
            // The next reparse will produce a fresh AST and retry.
        }
    }

    private void ColorizeHeading(DocumentLine line, HeadingBlock heading)
    {
        // Markdig heading.Line is 0-based; AvaloniaEdit line.LineNumber is 1-based
        if (heading.Line != line.LineNumber - 1) return;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        // Calculate the heading prefix length ("# ", "## ", "### ")
        int prefixLen = heading.Level;
        if (_text != null && lineStart + prefixLen < _text.Length && _text[lineStart + prefixLen] == ' ')
            prefixLen++;

        int contentStart = Math.Min(lineStart + prefixLen, lineEnd);

        double fontSize = heading.Level switch
        {
            1 => 28,
            2 => 22,
            3 => 18,
            _ => 14
        };

        // Hide the "# " prefix
        if (contentStart > lineStart)
        {
            HideRange(lineStart, contentStart, line);
        }

        // Style the content portion with heading font
        if (contentStart < lineEnd)
        {
            ChangeLinePart(contentStart, lineEnd, element =>
            {
                element.TextRunProperties.SetFontRenderingEmSize(fontSize);
                element.TextRunProperties.SetTypeface(new Typeface(
                    element.TextRunProperties.Typeface.FontFamily,
                    FontStyle.Normal,
                    FontWeight.Bold));
            });
        }
    }

    private void ColorizeInlines(DocumentLine line, ContainerInline container)
    {
        foreach (var inline in container)
        {
            if (inline is EmphasisInline emphasis)
            {
                ColorizeEmphasis(line, emphasis);
            }
        }
    }

    private void ColorizeEmphasis(DocumentLine line, EmphasisInline emphasis)
    {
        int emStart = emphasis.Span.Start;
        int emEnd = emphasis.Span.End + 1;
        int delimLen = emphasis.DelimiterCount;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        // Compute delimiter ranges
        int openStart = emStart;
        int openEnd = emStart + delimLen;
        int closeStart = emEnd - delimLen;
        int closeEnd = emEnd;

        // Content range (between delimiters)
        int contentStart = openEnd;
        int contentEnd = closeStart;

        // Clamp all ranges to line boundaries
        int clampedOpenStart = Math.Max(openStart, lineStart);
        int clampedOpenEnd = Math.Min(openEnd, lineEnd);
        int clampedCloseStart = Math.Max(closeStart, lineStart);
        int clampedCloseEnd = Math.Min(closeEnd, lineEnd);
        int clampedContentStart = Math.Max(contentStart, lineStart);
        int clampedContentEnd = Math.Min(contentEnd, lineEnd);

        // Hide opening delimiter if it's on this line
        if (clampedOpenStart < clampedOpenEnd)
        {
            HideRange(clampedOpenStart, clampedOpenEnd, line);
        }

        // Style the content
        if (clampedContentStart < clampedContentEnd)
        {
            ChangeLinePart(clampedContentStart, clampedContentEnd, element =>
            {
                var current = element.TextRunProperties.Typeface;

                if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 2)
                {
                    // Bold
                    element.TextRunProperties.SetTypeface(new Typeface(
                        current.FontFamily, current.Style, FontWeight.Bold));
                }
                else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 1)
                {
                    // Italic
                    element.TextRunProperties.SetTypeface(new Typeface(
                        current.FontFamily, FontStyle.Italic, current.Weight));
                }
                else if (emphasis.DelimiterChar == '+' && emphasis.DelimiterCount == 2)
                {
                    // Underline (inserted text via ++)
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                }
            });
        }

        // Hide closing delimiter if it's on this line
        if (clampedCloseStart < clampedCloseEnd)
        {
            HideRange(clampedCloseStart, clampedCloseEnd, line);
        }

        // Recurse into nested emphasis (e.g. ***bold italic***)
        foreach (var child in emphasis)
        {
            if (child is EmphasisInline nested)
            {
                ColorizeEmphasis(line, nested);
            }
        }
    }

    /// <summary>
    /// Hides text by making it transparent and near-zero font size.
    /// </summary>
    private void HideRange(int start, int end, DocumentLine line)
    {
        // Validate bounds against line
        start = Math.Max(start, line.Offset);
        end = Math.Min(end, line.EndOffset);
        if (start >= end) return;

        ChangeLinePart(start, end, element =>
        {
            element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
            element.TextRunProperties.SetFontRenderingEmSize(0.001);
        });
    }
}
