using CommunityToolkit.Mvvm.ComponentModel;

namespace VibePlatform.Models;

public partial class EditorTab : ObservableObject
{
    public ProjectFile ProjectFile { get; set; } = null!;
    public string Content { get; set; } = "";
    public int CaretOffset { get; set; }
    public double ScrollOffset { get; set; }

    [ObservableProperty]
    private bool _isDirty;

    public string DisplayName => IsDirty ? ProjectFile.FileName + " *" : ProjectFile.FileName;

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
