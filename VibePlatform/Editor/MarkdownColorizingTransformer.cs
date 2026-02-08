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

    public void UpdateAst(MarkdownDocument? ast)
    {
        _ast = ast;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_ast == null) return;

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

    private void ColorizeHeading(DocumentLine line, HeadingBlock heading)
    {
        if (heading.Line != line.LineNumber) return;

        int start = line.Offset;
        int end = line.EndOffset;

        double fontSize = heading.Level switch
        {
            1 => 28,
            2 => 22,
            3 => 18,
            _ => 14
        };

        ChangeLinePart(start, end, element =>
        {
            element.TextRunProperties.SetFontRenderingEmSize(fontSize);
            element.TextRunProperties.SetTypeface(new Typeface(
                element.TextRunProperties.Typeface.FontFamily,
                FontStyle.Normal,
                FontWeight.Bold));
        });
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

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        int overlapStart = Math.Max(emStart, lineStart);
        int overlapEnd = Math.Min(emEnd, lineEnd);

        if (overlapStart >= overlapEnd) return;

        ChangeLinePart(overlapStart, overlapEnd, element =>
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

        // Recurse into nested emphasis (e.g. ***bold italic***)
        foreach (var child in emphasis)
        {
            if (child is EmphasisInline nested)
            {
                ColorizeEmphasis(line, nested);
            }
        }
    }
}
