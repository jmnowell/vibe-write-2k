using CommunityToolkit.Mvvm.ComponentModel;
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
}
