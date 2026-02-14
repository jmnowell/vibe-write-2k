using System.Collections.Generic;

namespace VibePlatform.Models;

public class VersionHistory
{
    public int Version { get; set; } = 1;
    public List<VersionCommit> Commits { get; set; } = new();
}
