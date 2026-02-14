using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using VibePlatform.Models;

namespace VibePlatform.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private Document _document = new();

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _hasVersionHistory;

    [ObservableProperty]
    private string _windowTitle = "Vibe - Untitled";

    [ObservableProperty]
    private bool _isOutlineVisible;

    [ObservableProperty]
    private bool _isFocusMode;

    [ObservableProperty]
    private Project? _project;

    [ObservableProperty]
    private bool _isProjectMode;

    [ObservableProperty]
    private ProjectFile? _activeProjectFile;

    [ObservableProperty]
    private bool _isProjectPaneVisible;

    [ObservableProperty]
    private EditorTab? _activeTab;

    public ObservableCollection<OutlineItem> OutlineItems { get; } = new();
    public ObservableCollection<ProjectFile> ProjectFiles { get; } = new();
    public ObservableCollection<EditorTab> OpenTabs { get; } = new();

    public bool IsTabBarVisible => IsProjectMode && OpenTabs.Count > 0;

    public void UpdateTitle()
    {
        var name = Document.FileName;
        if (IsProjectMode && Project != null)
        {
            var projectName = System.IO.Path.GetFileName(Project.DirectoryPath);
            WindowTitle = IsDirty ? $"Vibe - {projectName} / {name} *" : $"Vibe - {projectName} / {name}";
        }
        else
        {
            WindowTitle = IsDirty ? $"Vibe - {name} *" : $"Vibe - {name}";
        }
    }

    public void NewDocument()
    {
        Document = new Document();
        IsDirty = false;
        UpdateTitle();
    }

    public void SetFilePath(string path)
    {
        Document.FilePath = path;
        UpdateTitle();
    }

    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            UpdateTitle();
        }
    }

    public void MarkClean()
    {
        IsDirty = false;
        UpdateTitle();
    }

    public void UpdateVersionHistoryState(bool hasHistory)
    {
        HasVersionHistory = hasHistory;
    }

    public EditorTab OpenTab(ProjectFile file)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.ProjectFile.AbsolutePath == file.AbsolutePath);
        if (existing != null)
        {
            ActiveTab = existing;
            return existing;
        }

        var tab = new EditorTab { ProjectFile = file };
        OpenTabs.Add(tab);
        ActiveTab = tab;
        OnPropertyChanged(nameof(IsTabBarVisible));
        return tab;
    }

    public EditorTab? CloseTab(EditorTab tab)
    {
        int idx = OpenTabs.IndexOf(tab);
        if (idx < 0) return ActiveTab;

        OpenTabs.RemoveAt(idx);
        OnPropertyChanged(nameof(IsTabBarVisible));

        if (ActiveTab == tab)
        {
            if (OpenTabs.Count == 0)
            {
                ActiveTab = null;
                return null;
            }
            // Select adjacent tab
            ActiveTab = OpenTabs[Math.Min(idx, OpenTabs.Count - 1)];
        }

        return ActiveTab;
    }

    public void ClearTabs()
    {
        OpenTabs.Clear();
        ActiveTab = null;
        OnPropertyChanged(nameof(IsTabBarVisible));
    }

    partial void OnIsProjectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTabBarVisible));
    }

    public void ToggleOutline()
    {
        IsOutlineVisible = !IsOutlineVisible;
    }

    public void ToggleFocusMode() => IsFocusMode = !IsFocusMode;

    public void UpdateOutline(MarkdownDocument? ast)
    {
        OutlineItems.Clear();
        if (ast == null) return;

        foreach (var block in ast)
        {
            if (block is HeadingBlock heading && heading.Level <= 3)
            {
                var title = heading.Inline != null
                    ? ExtractInlineText(heading.Inline)
                    : "";

                OutlineItems.Add(new OutlineItem
                {
                    Title = title,
                    Level = heading.Level,
                    LineNumber = heading.Line
                });
            }
        }
    }

    public void UpdateProjectOutline(Dictionary<string, MarkdownDocument> fileAsts)
    {
        OutlineItems.Clear();
        if (Project == null) return;

        foreach (var relativePath in Project.FileOrder)
        {
            var absPath = System.IO.Path.Combine(Project.DirectoryPath, relativePath);
            if (!fileAsts.TryGetValue(absPath, out var ast)) continue;

            foreach (var block in ast)
            {
                if (block is HeadingBlock heading && heading.Level <= 3)
                {
                    var title = heading.Inline != null
                        ? ExtractInlineText(heading.Inline)
                        : "";

                    OutlineItems.Add(new OutlineItem
                    {
                        Title = title,
                        Level = heading.Level,
                        LineNumber = heading.Line,
                        SourceFilePath = absPath
                    });
                }
            }
        }
    }

    private static string ExtractInlineText(ContainerInline container)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
                sb.Append(literal.Content);
            else if (inline is ContainerInline nested)
                sb.Append(ExtractInlineText(nested));
        }
        return sb.ToString();
    }
}
