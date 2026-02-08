namespace VibePlatform.Models;

public class Document
{
    public string? FilePath { get; set; }
    public string FileName => FilePath != null ? System.IO.Path.GetFileName(FilePath) : "Untitled";
}
