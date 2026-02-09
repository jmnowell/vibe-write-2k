using System.IO;
using VibePlatform.Editor;
using VibePlatform.ViewModels;
using Xunit;

namespace VibePlatform.Tests.ViewModels;

public class MainWindowViewModelTests
{
    [Fact]
    public void MarkDirty_AppendsAsteriskInTitle()
    {
        var viewModel = new MainWindowViewModel();
        var path = Path.Combine("docs", "notes.md");

        viewModel.SetFilePath(path);
        viewModel.MarkDirty();

        Assert.Equal("Vibe - notes.md *", viewModel.WindowTitle);
    }

    [Fact]
    public void ToggleOutline_FlipsVisibility()
    {
        var viewModel = new MainWindowViewModel();

        Assert.False(viewModel.IsOutlineVisible);

        viewModel.ToggleOutline();
        Assert.True(viewModel.IsOutlineVisible);

        viewModel.ToggleOutline();
        Assert.False(viewModel.IsOutlineVisible);
    }

    [Fact]
    public void UpdateOutline_CollectsHeadings()
    {
        var viewModel = new MainWindowViewModel();
        var parser = new MarkdownParserService();
        var markdown = "# Title\n\n## Section\n\nNot a heading";

        var ast = parser.Parse(markdown);
        viewModel.UpdateOutline(ast);

        Assert.Equal(2, viewModel.OutlineItems.Count);
        Assert.Equal("Title", viewModel.OutlineItems[0].Title);
        Assert.Equal(1, viewModel.OutlineItems[0].Level);
        Assert.Equal(0, viewModel.OutlineItems[0].LineNumber);

        Assert.Equal("Section", viewModel.OutlineItems[1].Title);
        Assert.Equal(2, viewModel.OutlineItems[1].Level);
        Assert.Equal(2, viewModel.OutlineItems[1].LineNumber);
    }
}
