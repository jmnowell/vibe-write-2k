using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using VibePlatform.Editor;
using VibePlatform.Services;
using VibePlatform.ViewModels;

namespace VibePlatform.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly MarkdownParserService _parser;
    private readonly MarkdownColorizingTransformer _colorizer;
    private readonly FileService _fileService;
    private readonly VersioningService _versioningService;
    private readonly DispatcherTimer _debounceTimer;
    private bool _suppressTextChanged;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _parser = new MarkdownParserService();
        _colorizer = new MarkdownColorizingTransformer();
        _fileService = new FileService();
        _versioningService = new VersioningService();

        // Set up debounce timer for re-parsing
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounceTimer.Tick += OnDebounceTimerTick;

        // Wire up editor transformers
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // Wire up text change handler
        Editor.TextChanged += OnTextChanged;

        // Wire up keyboard shortcuts
        KeyDown += OnKeyDown;

        // Bind window title
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.WindowTitle))
                Title = _viewModel.WindowTitle;
        };

        _initialized = true;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged) return;

        _viewModel.MarkDirty();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        ReparseAndRedraw();
    }

    private void ReparseAndRedraw()
    {
        string text = Editor.Text;
        var ast = _parser.Parse(text);
        _colorizer.UpdateAst(ast, text);
        Editor.TextArea.TextView.Redraw();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.B:
                    FormattingCommands.ToggleBold(Editor);
                    e.Handled = true;
                    break;
                case Key.I:
                    FormattingCommands.ToggleItalic(Editor);
                    e.Handled = true;
                    break;
                case Key.U:
                    FormattingCommands.ToggleUnderline(Editor);
                    e.Handled = true;
                    break;
                case Key.N:
                    OnNewClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.O:
                    OnOpenClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        OnSaveAsClick(this, new RoutedEventArgs());
                    else
                        OnSaveClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.K:
                    OnCommitClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
    }

    // Toolbar button handlers
    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        FormattingCommands.ToggleBold(Editor);
        Editor.Focus();
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        FormattingCommands.ToggleItalic(Editor);
        Editor.Focus();
    }

    private void OnUnderlineClick(object? sender, RoutedEventArgs e)
    {
        FormattingCommands.ToggleUnderline(Editor);
        Editor.Focus();
    }

    private void OnHeaderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || HeaderCombo.SelectedIndex < 0) return;
        int level = HeaderCombo.SelectedIndex; // 0=Normal, 1=H1, 2=H2, 3=H3
        FormattingCommands.CycleHeader(Editor, level);
        Editor.Focus();
    }

    // File operations
    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var result = await PromptSave();
            if (result == null) return; // Cancelled
        }

        _suppressTextChanged = true;
        Editor.Text = "";
        _suppressTextChanged = false;
        _viewModel.NewDocument();
        _viewModel.UpdateVersionHistoryState(false);
        ReparseAndRedraw();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var result = await PromptSave();
            if (result == null) return; // Cancelled
        }

        var (path, content) = await _fileService.OpenFileAsync(this);
        if (path == null || content == null) return;

        _suppressTextChanged = true;
        Editor.Text = content;
        _suppressTextChanged = false;
        _viewModel.SetFilePath(path);
        _viewModel.MarkClean();
        _viewModel.UpdateVersionHistoryState(_versioningService.HasHistory(path));
        ReparseAndRedraw();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var path = await _fileService.SaveFileAsync(this, Editor.Text, _viewModel.Document.FilePath);
        if (path != null)
        {
            _viewModel.SetFilePath(path);
            _viewModel.MarkClean();
            _viewModel.UpdateVersionHistoryState(_versioningService.HasHistory(path));
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var path = await _fileService.SaveFileAsAsync(this, Editor.Text);
        if (path != null)
        {
            _viewModel.SetFilePath(path);
            _viewModel.MarkClean();
            _viewModel.UpdateVersionHistoryState(_versioningService.HasHistory(path));
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            e.Cancel = true;
            var result = await PromptSave();
            if (result != null)
            {
                _viewModel.MarkClean(); // Prevent re-prompt
                Close();
            }
        }

        base.OnClosing(e);
    }

    // Version operations
    private async void OnCommitClick(object? sender, RoutedEventArgs e)
    {
        var filePath = _viewModel.Document.FilePath;

        if (filePath == null)
        {
            await ShowMessageAsync("Commit", "Please save the file first before committing.");
            return;
        }

        if (_viewModel.IsDirty)
        {
            var choice = await ShowSaveBeforeCommitDialogAsync();
            if (choice == null) return; // Cancelled

            if (choice == true) // Save & Commit
            {
                var path = await _fileService.SaveFileAsync(this, Editor.Text, filePath);
                if (path == null) return;
                _viewModel.SetFilePath(path);
                _viewModel.MarkClean();
            }
            // choice == false means "Commit Saved Version" — proceed without saving
        }

        var message = await ShowCommitDialogAsync();
        if (message == null) return; // Cancelled

        await _versioningService.CommitAsync(filePath, message);
        _viewModel.UpdateVersionHistoryState(true);
    }

    private async void OnViewHistoryClick(object? sender, RoutedEventArgs e)
    {
        var filePath = _viewModel.Document.FilePath;
        if (filePath == null) return;

        var history = await _versioningService.LoadHistoryAsync(filePath);
        if (history.Commits.Count == 0) return;

        await ShowHistoryDialogAsync(history);
    }

    private async void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        var filePath = _viewModel.Document.FilePath;
        if (filePath == null) return;

        var history = await _versioningService.LoadHistoryAsync(filePath);
        if (history.Commits.Count == 0) return;

        var currentContent = Editor.Text;
        var commitId = await ShowRevertDialogAsync(history, filePath, currentContent);
        if (commitId == null) return;

        var content = await _versioningService.GetSnapshotContentAsync(filePath, commitId);

        _suppressTextChanged = true;
        Editor.Text = content;
        _suppressTextChanged = false;

        // Save reverted content to disk
        await System.IO.File.WriteAllTextAsync(filePath, content);
        _viewModel.MarkClean();
        ReparseAndRedraw();
    }

    // Dialog helpers

    private async System.Threading.Tasks.Task<string?> ShowCommitDialogAsync()
    {
        var dialog = new Window
        {
            Title = "Commit",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Enter a commit message:" });

        var textBox = new TextBox { Watermark = "e.g. First draft of chapter 1" };
        panel.Children.Add(textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var commitBtn = new Button { Content = "Commit" };
        commitBtn.Click += (_, _) =>
        {
            var msg = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                result = msg;
                dialog.Close();
            }
        };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(commitBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task ShowHistoryDialogAsync(Models.VersionHistory history)
    {
        var dialog = new Window
        {
            Title = "Version History",
            Width = 500,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var panel = new DockPanel { Margin = new Thickness(16) };

        var closeBtn = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        closeBtn.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(closeBtn, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(closeBtn);

        var listBox = new ListBox();
        foreach (var commit in history.Commits.AsEnumerable().Reverse())
        {
            var item = new TextBlock
            {
                Text = $"{commit.Timestamp:yyyy-MM-dd HH:mm:ss}  —  {commit.Message}  ({commit.FileSizeBytes:N0} bytes)",
                Margin = new Thickness(4, 2)
            };
            listBox.Items.Add(item);
        }

        panel.Children.Add(listBox);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private async System.Threading.Tasks.Task<string?> ShowRevertDialogAsync(
        Models.VersionHistory history, string filePath, string currentContent)
    {
        var dialog = new Window
        {
            Title = "Revert to Commit",
            Width = 1100,
            Height = 650,
            MinWidth = 800,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        string? result = null;

        // Build list items newest-first
        var commits = history.Commits.AsEnumerable().Reverse().ToList();

        var rootPanel = new DockPanel { Margin = new Thickness(12) };

        // Bottom buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var revertBtn = new Button { Content = "Revert", IsEnabled = false };
        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) => dialog.Close();
        buttonPanel.Children.Add(revertBtn);
        buttonPanel.Children.Add(cancelBtn);
        DockPanel.SetDock(buttonPanel, Avalonia.Controls.Dock.Bottom);
        rootPanel.Children.Add(buttonPanel);

        // Left: commit list
        var commitList = new ListBox { MinWidth = 240, MaxWidth = 300 };
        foreach (var commit in commits)
        {
            var item = new ListBoxItem
            {
                Content = $"{commit.Timestamp:yyyy-MM-dd HH:mm:ss}\n{commit.Message}",
                Tag = commit.Id,
                Padding = new Thickness(6, 4)
            };
            commitList.Items.Add(item);
        }

        // Right: diff panel (side-by-side)
        var diffLeftHeader = new TextBlock
        {
            Text = "Current Document",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var diffRightHeader = new TextBlock
        {
            Text = "Snapshot (select a commit)",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var diffLeftPanel = new StackPanel();
        var diffRightPanel = new StackPanel();

        var diffLeftScroll = new ScrollViewer
        {
            Content = diffLeftPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var diffRightScroll = new ScrollViewer
        {
            Content = diffRightPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        // Synchronize vertical scrolling between the two panels
        diffLeftScroll.ScrollChanged += (_, _) =>
        {
            if (Math.Abs(diffRightScroll.Offset.Y - diffLeftScroll.Offset.Y) > 0.5)
                diffRightScroll.Offset = new Vector(diffRightScroll.Offset.X, diffLeftScroll.Offset.Y);
        };
        diffRightScroll.ScrollChanged += (_, _) =>
        {
            if (Math.Abs(diffLeftScroll.Offset.Y - diffRightScroll.Offset.Y) > 0.5)
                diffLeftScroll.Offset = new Vector(diffLeftScroll.Offset.X, diffRightScroll.Offset.Y);
        };

        // Left column (header + scroll)
        var leftCol = new DockPanel();
        DockPanel.SetDock(diffLeftHeader, Avalonia.Controls.Dock.Top);
        leftCol.Children.Add(diffLeftHeader);
        leftCol.Children.Add(diffLeftScroll);

        // Right column (header + scroll)
        var rightCol = new DockPanel();
        DockPanel.SetDock(diffRightHeader, Avalonia.Controls.Dock.Top);
        rightCol.Children.Add(diffRightHeader);
        rightCol.Children.Add(diffRightScroll);

        // Side-by-side grid for the two diff columns
        var diffGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*")
        };
        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(rightCol, 2);
        diffGrid.Children.Add(leftCol);
        diffGrid.Children.Add(new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            [Grid.ColumnProperty] = 1
        });
        diffGrid.Children.Add(rightCol);

        // Main horizontal split: commit list | diff
        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*")
        };
        Grid.SetColumn(commitList, 0);
        Grid.SetColumn(diffGrid, 2);
        mainGrid.Children.Add(commitList);
        mainGrid.Children.Add(diffGrid);

        rootPanel.Children.Add(mainGrid);

        // Wire up commit selection to show diff
        commitList.SelectionChanged += async (_, _) =>
        {
            if (commitList.SelectedItem is not ListBoxItem selected) return;
            var selectedId = selected.Tag as string;
            if (selectedId == null) return;

            revertBtn.IsEnabled = true;
            diffRightHeader.Text = "Snapshot";

            var snapshotContent = await _versioningService.GetSnapshotContentAsync(filePath, selectedId);
            var diffResult = LineDiffService.Diff(snapshotContent, currentContent);

            PopulateDiffPanel(diffLeftPanel, diffResult.LeftLines, isLeft: true);
            PopulateDiffPanel(diffRightPanel, diffResult.RightLines, isLeft: false);
        };

        revertBtn.Click += (_, _) =>
        {
            if (commitList.SelectedItem is ListBoxItem selected)
            {
                result = selected.Tag as string;
                dialog.Close();
            }
        };

        dialog.Content = rootPanel;
        await dialog.ShowDialog(this);
        return result;
    }

    private static readonly IBrush DeletedBrush = new SolidColorBrush(Color.FromRgb(255, 220, 220));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromRgb(220, 255, 220));
    private static readonly IBrush DeletedTextBrush = new SolidColorBrush(Color.FromRgb(180, 40, 40));
    private static readonly IBrush AddedTextBrush = new SolidColorBrush(Color.FromRgb(40, 140, 40));

    private static void PopulateDiffPanel(StackPanel panel, List<DiffLine> lines, bool isLeft)
    {
        panel.Children.Clear();

        foreach (var line in lines)
        {
            IBrush? bg = null;
            IBrush fg = Brushes.Black;
            string prefix = "  ";

            switch (line.Type)
            {
                case DiffLineType.Deleted:
                    bg = DeletedBrush;
                    fg = DeletedTextBrush;
                    prefix = "- ";
                    break;
                case DiffLineType.Added:
                    bg = AddedBrush;
                    fg = AddedTextBrush;
                    prefix = "+ ";
                    break;
            }

            var tb = new TextBlock
            {
                Text = prefix + line.Text,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace"),
                FontSize = 12,
                Padding = new Thickness(4, 1),
                Foreground = fg
            };

            if (bg != null)
                tb.Background = bg;

            panel.Children.Add(tb);
        }
    }

    private async System.Threading.Tasks.Task<bool?> ShowSaveBeforeCommitDialogAsync()
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool? dialogResult = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = "You have unsaved changes. How would you like to proceed?",
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var saveCommitBtn = new Button { Content = "Save & Commit" };
        saveCommitBtn.Click += (_, _) =>
        {
            dialogResult = true;
            dialog.Close();
        };

        var commitSavedBtn = new Button { Content = "Commit Saved Version" };
        commitSavedBtn.Click += (_, _) =>
        {
            dialogResult = false;
            dialog.Close();
        };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) =>
        {
            dialogResult = null;
            dialog.Close();
        };

        buttons.Children.Add(saveCommitBtn);
        buttons.Children.Add(commitSavedBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return dialogResult;
    }

    private async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 350,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        var okBtn = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        okBtn.Click += (_, _) => dialog.Close();
        panel.Children.Add(okBtn);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private async System.Threading.Tasks.Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool result = false;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var yesBtn = new Button { Content = "Yes" };
        yesBtn.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        var noBtn = new Button { Content = "No" };
        noBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(yesBtn);
        buttons.Children.Add(noBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task<bool?> PromptSave()
    {
        var dialog = new Window
        {
            Title = "Save Changes?",
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool? dialogResult = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = "Do you want to save changes to the current document?",
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var saveBtn = new Button { Content = "Save" };
        saveBtn.Click += async (_, _) =>
        {
            var path = await _fileService.SaveFileAsync(this, Editor.Text, _viewModel.Document.FilePath);
            if (path != null)
            {
                _viewModel.SetFilePath(path);
                _viewModel.MarkClean();
                dialogResult = true;
                dialog.Close();
            }
        };

        var dontSaveBtn = new Button { Content = "Don't Save" };
        dontSaveBtn.Click += (_, _) =>
        {
            dialogResult = false;
            dialog.Close();
        };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) =>
        {
            dialogResult = null;
            dialog.Close();
        };

        buttons.Children.Add(saveBtn);
        buttons.Children.Add(dontSaveBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);

        return dialogResult;
    }
}
