using System;
using System.IO;
using System.Threading.Tasks;
using VibePlatform.Services;
using NUnit.Framework;

namespace VibePlatform.Tests.Services;

[TestFixture]
public class VersioningServiceTests
{
    [Test]
    public async Task LoadHistoryAsync_ReturnsEmpty_WhenMissing()
    {
        var service = new VersioningService();
        var tempRoot = CreateTempRoot();
        var filePath = Path.Combine(tempRoot, "doc.md");

        try
        {
            await File.WriteAllTextAsync(filePath, "hello");

            var history = await service.LoadHistoryAsync(filePath);

            Assert.That(history.Commits, Is.Empty);
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }
    }

    [Test]
    public async Task CommitAsync_CreatesHistoryAndSnapshot()
    {
        var service = new VersioningService();
        var tempRoot = CreateTempRoot();
        var filePath = Path.Combine(tempRoot, "doc.md");

        try
        {
            await File.WriteAllTextAsync(filePath, "hello");

            Assert.That(service.HasHistory(filePath), Is.False);

            var commit = await service.CommitAsync(filePath, "first");

            Assert.That(service.HasHistory(filePath), Is.True);

            var history = await service.LoadHistoryAsync(filePath);
            Assert.That(history.Commits, Has.Count.EqualTo(1));
            Assert.That(history.Commits[0].Id, Is.EqualTo(commit.Id));
            Assert.That(history.Commits[0].FileSizeBytes, Is.EqualTo(new FileInfo(filePath).Length));

            var snapshot = await service.GetSnapshotContentAsync(filePath, commit.Id);
            Assert.That(snapshot, Is.EqualTo("hello"));
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibePlatformTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static void CleanupTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
