using System.IO;

namespace VibePlatform.Models;

public class ProjectFile
{
    public string RelativePath { get; set; } = "";
    public string FileName => Path.GetFileName(RelativePath);
    public string AbsolutePath { get; set; } = "";
}
