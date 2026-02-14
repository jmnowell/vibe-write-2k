using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VibePlatform.Services;

public class FileService
{
    private static readonly FilePickerFileType MarkdownFileType = new("Markdown Files")
    {
        Patterns = new[] { "*.md", "*.markdown", "*.txt" },
        MimeTypes = new[] { "text/markdown", "text/plain" }
    };

    private static readonly FilePickerFileType AllFilesType = new("All Files")
    {
        Patterns = new[] { "*" }
    };

    public async Task<(string? path, string? content)> OpenFileAsync(Window window)
    {
        var storageProvider = window.StorageProvider;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Document",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { MarkdownFileType, AllFilesType }
        });

        if (files.Count == 0) return (null, null);

        var file = files[0];
        var path = file.TryGetLocalPath();
        if (path == null) return (null, null);

        var content = await File.ReadAllTextAsync(path);
        return (path, content);
    }

    public async Task<string?> SaveFileAsync(Window window, string content, string? existingPath)
    {
        if (existingPath != null)
        {
            await File.WriteAllTextAsync(existingPath, content);
            return existingPath;
        }

        return await SaveFileAsAsync(window, content);
    }

    public async Task<string?> SaveFileAsAsync(Window window, string content)
    {
        var storageProvider = window.StorageProvider;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Document",
            DefaultExtension = "md",
            FileTypeChoices = new List<FilePickerFileType> { MarkdownFileType, AllFilesType },
            SuggestedFileName = "document.md"
        });

        if (file == null) return null;

        var path = file.TryGetLocalPath();
        if (path == null) return null;

        await File.WriteAllTextAsync(path, content);
        return path;
    }

    public async Task<string?> OpenFolderAsync(Window window, string title)
    {
        var storageProvider = window.StorageProvider;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (folders.Count == 0) return null;

        return folders[0].TryGetLocalPath();
    }
}
