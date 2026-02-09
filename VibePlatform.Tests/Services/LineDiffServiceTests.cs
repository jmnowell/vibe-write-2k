using System.Linq;
using VibePlatform.Services;
using Xunit;

namespace VibePlatform.Tests.Services;

public class LineDiffServiceTests
{
    [Fact]
    public void Diff_ReturnsUnchanged_WhenTextsEqual()
    {
        var result = LineDiffService.Diff("a\nb\nc", "a\nb\nc");

        Assert.Equal(3, result.LeftLines.Count);
        Assert.Equal(3, result.RightLines.Count);
        Assert.All(result.LeftLines, line => Assert.Equal(DiffLineType.Unchanged, line.Type));
        Assert.All(result.RightLines, line => Assert.Equal(DiffLineType.Unchanged, line.Type));
    }

    [Fact]
    public void Diff_TracksAddedLines()
    {
        var result = LineDiffService.Diff("a\nb", "a\nb\nc");

        var lastLeft = result.LeftLines.Last();
        var lastRight = result.RightLines.Last();

        Assert.Equal("", lastLeft.Text);
        Assert.Equal(DiffLineType.Unchanged, lastLeft.Type);
        Assert.Equal("c", lastRight.Text);
        Assert.Equal(DiffLineType.Added, lastRight.Type);
    }

    [Fact]
    public void Diff_TracksDeletedLines()
    {
        var result = LineDiffService.Diff("a\nb\nc", "a\nc");

        var deletedIndex = result.LeftLines.FindIndex(line => line.Type == DiffLineType.Deleted);

        Assert.True(deletedIndex >= 0);
        Assert.Equal("b", result.LeftLines[deletedIndex].Text);
        Assert.Equal(DiffLineType.Unchanged, result.RightLines[deletedIndex].Type);
    }
}
