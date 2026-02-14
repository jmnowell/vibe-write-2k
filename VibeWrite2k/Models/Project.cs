using System.Collections.Generic;

namespace VibePlatform.Models;

public class Project
{
    public int Version { get; set; } = 1;
    public List<string> FileOrder { get; set; } = new();
    public string DirectoryPath { get; set; } = "";
}
