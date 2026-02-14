using Markdig;
using Markdig.Syntax;
using Markdig.Extensions.EmphasisExtras;

namespace VibePlatform.Editor;

public class MarkdownParserService
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownParserService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseEmphasisExtras(EmphasisExtraOptions.Inserted)
            .Build();
    }

    public MarkdownDocument Parse(string markdown)
    {
        return Markdown.Parse(markdown, _pipeline);
    }
}
