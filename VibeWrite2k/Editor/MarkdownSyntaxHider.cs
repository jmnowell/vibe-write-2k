using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VibePlatform.Editor;

public class MarkdownSyntaxHider : VisualLineElementGenerator
{
    private MarkdownDocument? _ast;
    private List<(int Start, int Length)> _hiddenRanges = new();

    public IReadOnlyList<(int Start, int Length)> HiddenRanges => _hiddenRanges;

    public void UpdateAst(MarkdownDocument? ast, string text)
    {
        _ast = ast;
        RebuildHiddenRanges(text);
    }

    private void RebuildHiddenRanges(string text)
    {
        _hiddenRanges.Clear();
        if (_ast == null) return;

        foreach (var block in _ast)
        {
            if (block is HeadingBlock heading)
            {
                // Hide "# ", "## ", "### " prefix
                int prefixLen = heading.Level;
                int start = heading.Span.Start;
                // Include the space after the # chars
                if (start + prefixLen < text.Length && text[start + prefixLen] == ' ')
                    prefixLen++;
                _hiddenRanges.Add((start, prefixLen));

                if (heading.Inline != null)
                    CollectInlineRanges(heading.Inline);
            }
            else if (block is ParagraphBlock paragraph && paragraph.Inline != null)
            {
                CollectInlineRanges(paragraph.Inline);
            }
        }

        _hiddenRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    private void CollectInlineRanges(ContainerInline container)
    {
        foreach (var inline in container)
        {
            if (inline is EmphasisInline emphasis)
            {
                int delimLen = emphasis.DelimiterCount;
                int contentStart = emphasis.Span.Start + delimLen;
                int contentEnd = emphasis.Span.End + 1 - delimLen;

                if (contentStart <= contentEnd)
                {
                    // Opening delimiter
                    _hiddenRanges.Add((emphasis.Span.Start, delimLen));
                    // Closing delimiter
                    _hiddenRanges.Add((contentEnd, delimLen));
                }

                // Recurse for nested emphasis
                CollectInlineRanges(emphasis);
            }
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        foreach (var (start, length) in _hiddenRanges)
        {
            if (start >= startOffset)
                return start;
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        foreach (var (start, length) in _hiddenRanges)
        {
            if (start == offset)
            {
                var currentLine = CurrentContext.VisualLine;
                // Find the text elements that correspond to these characters
                return new HiddenTextElement(length);
            }
        }
        return null;
    }
}

public class HiddenTextElement : FormattedTextElement
{
    public HiddenTextElement(int documentLength)
        : base("", documentLength)
    {
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        // Return a zero-width text run that effectively hides the characters
        return new FormattedTextRun(this, new ZeroWidthTextProperties(context.GlobalTextRunProperties));
    }
}

public class ZeroWidthTextProperties : TextRunProperties
{
    private readonly TextRunProperties _base;

    public ZeroWidthTextProperties(TextRunProperties baseProperties)
    {
        _base = baseProperties;
    }

    public override Typeface Typeface => _base.Typeface;
    public override double FontRenderingEmSize => 0.001; // Near-zero to hide
    public override IBrush? ForegroundBrush => Brushes.Transparent;
    public override IBrush? BackgroundBrush => null;
    public override CultureInfo? CultureInfo => _base.CultureInfo;
    public override BaselineAlignment BaselineAlignment => _base.BaselineAlignment;
    public override TextDecorationCollection? TextDecorations => null;
}
