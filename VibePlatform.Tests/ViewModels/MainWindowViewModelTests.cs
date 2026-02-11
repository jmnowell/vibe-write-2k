using System.IO;
using VibePlatform.Editor;
using VibePlatform.ViewModels;
using NUnit.Framework;

namespace VibePlatform.Tests.ViewModels;

[TestFixture]
public class MainWindowViewModelTests
{
    [Test]
    public void MarkDirty_AppendsAsteriskInTitle()
    {
        var viewModel = new MainWindowViewModel();
        var path = Path.Combine("docs", "notes.md");

        viewModel.SetFilePath(path);
        viewModel.MarkDirty();

        Assert.That(viewModel.WindowTitle, Is.EqualTo("Vibe - notes.md *"));
    }

    [Test]
    public void ToggleOutline_FlipsVisibility()
    {
        var viewModel = new MainWindowViewModel();

        Assert.That(viewModel.IsOutlineVisible, Is.False);

        viewModel.ToggleOutline();
        Assert.That(viewModel.IsOutlineVisible, Is.True);

        viewModel.ToggleOutline();
        Assert.That(viewModel.IsOutlineVisible, Is.False);
    }

    [Test]
    public void UpdateOutline_CollectsHeadings()
    {
        var viewModel = new MainWindowViewModel();
        var parser = new MarkdownParserService();
        var markdown = "# Title\n\n## Section\n\nNot a heading";

        var ast = parser.Parse(markdown);
        viewModel.UpdateOutline(ast);

        Assert.That(viewModel.OutlineItems, Has.Count.EqualTo(2));
        Assert.That(viewModel.OutlineItems[0].Title, Is.EqualTo("Title"));
        Assert.That(viewModel.OutlineItems[0].Level, Is.EqualTo(1));
        Assert.That(viewModel.OutlineItems[0].LineNumber, Is.EqualTo(0));

        Assert.That(viewModel.OutlineItems[1].Title, Is.EqualTo("Section"));
        Assert.That(viewModel.OutlineItems[1].Level, Is.EqualTo(2));
        Assert.That(viewModel.OutlineItems[1].LineNumber, Is.EqualTo(2));
    }
}
