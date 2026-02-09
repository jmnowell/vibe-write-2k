using System.Collections.ObjectModel;
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

    public ObservableCollection<OutlineItem> OutlineItems { get; } = new();

    public void UpdateTitle()
    {
        var name = Document.FileName;
        WindowTitle = IsDirty ? $"Vibe - {name} *" : $"Vibe - {name}";
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

    public void ToggleOutline()
    {
        IsOutlineVisible = !IsOutlineVisible;
    }

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
