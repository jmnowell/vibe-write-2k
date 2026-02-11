using System.Linq;
using VibePlatform.Services;
using NUnit.Framework;

namespace VibePlatform.Tests.Services;

[TestFixture]
public class LineDiffServiceTests
{
    [Test]
    public void Diff_ReturnsUnchanged_WhenTextsEqual()
    {
        var result = LineDiffService.Diff("a\nb\nc", "a\nb\nc");

        Assert.That(result.LeftLines, Has.Count.EqualTo(3));
        Assert.That(result.RightLines, Has.Count.EqualTo(3));
        Assert.That(result.LeftLines.Select(line => line.Type), Is.All.EqualTo(DiffLineType.Unchanged));
        Assert.That(result.RightLines.Select(line => line.Type), Is.All.EqualTo(DiffLineType.Unchanged));
    }

    [Test]
    public void Diff_TracksAddedLines()
    {
        var result = LineDiffService.Diff("a\nb", "a\nb\nc");

        var lastLeft = result.LeftLines.Last();
        var lastRight = result.RightLines.Last();

        Assert.That(lastLeft.Text, Is.EqualTo(""));
        Assert.That(lastLeft.Type, Is.EqualTo(DiffLineType.Unchanged));
        Assert.That(lastRight.Text, Is.EqualTo("c"));
        Assert.That(lastRight.Type, Is.EqualTo(DiffLineType.Added));
    }

    [Test]
    public void Diff_TracksDeletedLines()
    {
        var result = LineDiffService.Diff("a\nb\nc", "a\nc");

        var deletedIndex = result.LeftLines.FindIndex(line => line.Type == DiffLineType.Deleted);

        Assert.That(deletedIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.LeftLines[deletedIndex].Text, Is.EqualTo("b"));
        Assert.That(result.RightLines[deletedIndex].Type, Is.EqualTo(DiffLineType.Unchanged));
    }

    [Test]
    public void Diff_NormalizesLineEndings()
    {
        var result = LineDiffService.Diff("a\r\nb\r\nc", "a\nb\nc");

        Assert.That(result.LeftLines, Has.Count.EqualTo(3));
        Assert.That(result.RightLines, Has.Count.EqualTo(3));
        Assert.That(result.LeftLines.Select(line => line.Type), Is.All.EqualTo(DiffLineType.Unchanged));
        Assert.That(result.RightLines.Select(line => line.Type), Is.All.EqualTo(DiffLineType.Unchanged));
    }
}
