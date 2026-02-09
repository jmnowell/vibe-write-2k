using System;
using System.IO;
using System.Threading.Tasks;
using VibePlatform.Services;
using Xunit;

namespace VibePlatform.Tests.Services;

public class VersioningServiceTests
{
    [Fact]
    public async Task LoadHistoryAsync_ReturnsEmpty_WhenMissing()
    {
        var service = new VersioningService();
        var tempRoot = CreateTempRoot();
        var filePath = Path.Combine(tempRoot, "doc.md");

        try
        {
            await File.WriteAllTextAsync(filePath, "hello");

            var history = await service.LoadHistoryAsync(filePath);

            Assert.Empty(history.Commits);
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task CommitAsync_CreatesHistoryAndSnapshot()
    {
        var service = new VersioningService();
        var tempRoot = CreateTempRoot();
        var filePath = Path.Combine(tempRoot, "doc.md");

        try
        {
            await File.WriteAllTextAsync(filePath, "hello");

            Assert.False(service.HasHistory(filePath));

            var commit = await service.CommitAsync(filePath, "first");

            Assert.True(service.HasHistory(filePath));

            var history = await service.LoadHistoryAsync(filePath);
            Assert.Single(history.Commits);
            Assert.Equal(commit.Id, history.Commits[0].Id);
            Assert.Equal(new FileInfo(filePath).Length, history.Commits[0].FileSizeBytes);

            var snapshot = await service.GetSnapshotContentAsync(filePath, commit.Id);
            Assert.Equal("hello", snapshot);
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
