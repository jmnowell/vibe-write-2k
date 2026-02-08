using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VibePlatform.Models;

namespace VibePlatform.Services;

public class VersioningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets the .vibe/filename/ directory for a given file path.
    /// </summary>
    public string GetVersionDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);
        return Path.Combine(dir, ".vibe", fileName);
    }

    /// <summary>
    /// Checks whether a history.json exists for the given file.
    /// </summary>
    public bool HasHistory(string filePath)
    {
        var historyPath = Path.Combine(GetVersionDir(filePath), "history.json");
        return File.Exists(historyPath);
    }

    /// <summary>
    /// Reads the current file from disk, writes a snapshot, and appends to history.json.
    /// </summary>
    public async Task<VersionCommit> CommitAsync(string filePath, string message)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var versionDir = GetVersionDir(filePath);
        var snapshotsDir = Path.Combine(versionDir, "snapshots");

        Directory.CreateDirectory(snapshotsDir);

        // Generate commit ID: yyyyMMdd-HHmmss-<4 random hex>
        var now = DateTime.UtcNow;
        var randomHex = Random.Shared.Next(0, 0xFFFF).ToString("x4");
        var commitId = $"{now:yyyyMMdd-HHmmss}-{randomHex}";

        // Write snapshot
        var snapshotPath = Path.Combine(snapshotsDir, commitId + ".md");
        await File.WriteAllTextAsync(snapshotPath, content);

        // Load or create history
        var history = await LoadHistoryAsync(filePath);

        var commit = new VersionCommit
        {
            Id = commitId,
            Timestamp = now,
            Message = message,
            FileSizeBytes = new FileInfo(filePath).Length
        };

        history.Commits.Add(commit);

        // Write history.json
        var historyPath = Path.Combine(versionDir, "history.json");
        var json = JsonSerializer.Serialize(history, JsonOptions);
        await File.WriteAllTextAsync(historyPath, json);

        return commit;
    }

    /// <summary>
    /// Reads history.json for the given file. Returns empty history if none exists.
    /// </summary>
    public async Task<VersionHistory> LoadHistoryAsync(string filePath)
    {
        var historyPath = Path.Combine(GetVersionDir(filePath), "history.json");

        if (!File.Exists(historyPath))
            return new VersionHistory();

        var json = await File.ReadAllTextAsync(historyPath);
        return JsonSerializer.Deserialize<VersionHistory>(json, ReadOptions) ?? new VersionHistory();
    }

    /// <summary>
    /// Reads the content of a specific snapshot file.
    /// </summary>
    public async Task<string> GetSnapshotContentAsync(string filePath, string commitId)
    {
        var snapshotPath = Path.Combine(GetVersionDir(filePath), "snapshots", commitId + ".md");
        return await File.ReadAllTextAsync(snapshotPath);
    }
}
