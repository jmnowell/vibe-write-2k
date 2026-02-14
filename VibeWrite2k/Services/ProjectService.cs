using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VibePlatform.Models;

namespace VibePlatform.Services;

public class ProjectService
{
    private const string ProjectFileName = "project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Project> CreateProjectAsync(string dirPath)
    {
        Directory.CreateDirectory(dirPath);

        var project = new Project
        {
            DirectoryPath = dirPath,
            FileOrder = new List<string>()
        };

        await SaveProjectAsync(project);
        return project;
    }

    public async Task<Project> LoadProjectAsync(string dirPath)
    {
        var projectPath = Path.Combine(dirPath, ProjectFileName);
        var json = await File.ReadAllTextAsync(projectPath);
        var project = JsonSerializer.Deserialize<Project>(json, ReadOptions) ?? new Project();
        project.DirectoryPath = dirPath;
        return project;
    }

    public async Task SaveProjectAsync(Project project)
    {
        var projectPath = Path.Combine(project.DirectoryPath, ProjectFileName);
        var json = JsonSerializer.Serialize(project, JsonOptions);
        await File.WriteAllTextAsync(projectPath, json);
    }

    public void AddFile(Project project, string relativePath)
    {
        if (!project.FileOrder.Contains(relativePath))
            project.FileOrder.Add(relativePath);
    }

    public void RemoveFile(Project project, string relativePath)
    {
        project.FileOrder.Remove(relativePath);
    }

    public void MoveFile(Project project, int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= project.FileOrder.Count) return;
        if (toIndex < 0 || toIndex >= project.FileOrder.Count) return;

        var item = project.FileOrder[fromIndex];
        project.FileOrder.RemoveAt(fromIndex);
        project.FileOrder.Insert(toIndex, item);
    }

    public bool IsValidProjectDirectory(string dirPath)
    {
        return File.Exists(Path.Combine(dirPath, ProjectFileName));
    }
}
