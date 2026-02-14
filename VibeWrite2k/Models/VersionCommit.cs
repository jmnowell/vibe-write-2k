using System;

namespace VibePlatform.Models;

public class VersionCommit
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
    public long FileSizeBytes { get; set; }
}
