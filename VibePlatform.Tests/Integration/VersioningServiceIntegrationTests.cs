using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VibePlatform.Services;

namespace VibePlatform.Tests.Integration;

[TestFixture]
public class VersioningServiceIntegrationTests
{
    [Test]
    [Category("Integration")]
    public async Task CommitAsync_PersistsMultipleSnapshots()
    {
        var service = new VersioningService();
        var tempRoot = CreateTempRoot();
        var filePath = Path.Combine(tempRoot, "doc.md");

        try
        {
            await File.WriteAllTextAsync(filePath, "first");
            var firstCommit = await service.CommitAsync(filePath, "first commit");

            await File.WriteAllTextAsync(filePath, "second");
            var secondCommit = await service.CommitAsync(filePath, "second commit");

            var history = await service.LoadHistoryAsync(filePath);
            Assert.That(history.Commits, Has.Count.EqualTo(2));
            Assert.That(history.Commits[0].Id, Is.EqualTo(firstCommit.Id));
            Assert.That(history.Commits[1].Id, Is.EqualTo(secondCommit.Id));

            var firstSnapshot = await service.GetSnapshotContentAsync(filePath, firstCommit.Id);
            var secondSnapshot = await service.GetSnapshotContentAsync(filePath, secondCommit.Id);

            Assert.That(firstSnapshot, Is.EqualTo("first"));
            Assert.That(secondSnapshot, Is.EqualTo("second"));
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibePlatformIntegration", Guid.NewGuid().ToString("N"));
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
