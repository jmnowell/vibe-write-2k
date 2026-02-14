using System.Collections.Generic;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VibePlatform.Services;

public record WordOccurrence(string Word, int Offset, int Length);

public class MarkdownWordExtractor
{
    private static readonly Regex WordRegex = new(@"[a-zA-Z]+(?:'[a-zA-Z]+)*", RegexOptions.Compiled);

    public List<WordOccurrence> ExtractWords(string text, MarkdownDocument ast)
    {
        var words = new List<WordOccurrence>();
        CollectLiterals(ast, text, words);
        return words;
    }

    private void CollectLiterals(MarkdownObject node, string text, List<WordOccurrence> words)
    {
        if (node is LiteralInline literal)
        {
            var span = literal.Span;
            if (span.Start < 0 || span.End < 0 || span.Start >= text.Length) return;

            int spanStart = span.Start;
            int spanLength = span.End - span.Start + 1;
            if (spanStart + spanLength > text.Length)
                spanLength = text.Length - spanStart;

            var content = text.Substring(spanStart, spanLength);

            foreach (Match match in WordRegex.Matches(content))
            {
                if (match.Length < 2) continue;
                words.Add(new WordOccurrence(match.Value, spanStart + match.Index, match.Length));
            }
            return;
        }

        // Skip code inlines and other non-text inlines
        if (node is CodeInline || node is AutolinkInline || node is HtmlInline || node is HtmlEntityInline)
            return;

        // Skip fenced code blocks and HTML blocks
        if (node is FencedCodeBlock || node is CodeBlock || node is HtmlBlock)
            return;

        // Skip link URLs (but process link text)
        if (node is LinkInline link)
        {
            // Only process children (the link text), not the URL
            if (link.FirstChild != null)
            {
                var child = link.FirstChild;
                while (child != null)
                {
                    CollectLiterals(child, text, words);
                    child = child.NextSibling;
                }
            }
            return;
        }

        // Recurse into container blocks
        if (node is ContainerBlock containerBlock)
        {
            foreach (var child in containerBlock)
            {
                CollectLiterals(child, text, words);
            }
        }

        // Recurse into container inlines
        if (node is ContainerInline containerInline)
        {
            var child = containerInline.FirstChild;
            while (child != null)
            {
                CollectLiterals(child, text, words);
                child = child.NextSibling;
            }
        }

        // Recurse into leaf blocks that have inline content
        if (node is LeafBlock leafBlock && leafBlock.Inline != null)
        {
            CollectLiterals(leafBlock.Inline, text, words);
        }
    }
}
