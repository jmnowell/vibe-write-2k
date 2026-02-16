using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Markdig.Syntax;
using VibePlatform.Editor;
using VibePlatform.Models;
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
    private readonly ProjectService _projectService;
    private readonly PrintService _printService;
    private readonly SpellCheckService _spellCheckService;
    private readonly DispatcherTimer _debounceTimer;
    private readonly FocusModeTransformer _focusTransformer = new();
    private bool _suppressTextChanged;
    private bool _initialized;
    private bool _wasOutlineVisible;
    private bool _wasProjectPaneVisible;
    private ITransform? _savedTextViewTransform;
    private TranslateTransform? _focusModeTransform;
    private double _savedFontSize;
    private double _savedMaxWidth;
    private HorizontalAlignment _savedHorizontalAlignment;
    private Vector? _savedEditorScrollOffset;

    // Project state
    private readonly Dictionary<string, string> _projectFileContents = new();
    private readonly Dictionary<string, MarkdownDocument> _projectFileAsts = new();
    private bool _suppressProjectFileSelection;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _parser = new MarkdownParserService();
        _colorizer = new MarkdownColorizingTransformer();
        _fileService = new FileService();
        _versioningService = new VersioningService();
        _projectService = new ProjectService();
        _printService = new PrintService();
        _spellCheckService = new SpellCheckService();

        // Set up debounce timer for re-parsing
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounceTimer.Tick += OnDebounceTimerTick;

        // Enable word wrap
        Editor.WordWrap = true;

        // Wire up editor transformers
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // Wire up text change handler
        Editor.TextChanged += OnTextChanged;

        // Wire up keyboard shortcuts (tunnel so Escape is caught before the editor)
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

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
        if (_viewModel.ActiveTab != null)
            _viewModel.ActiveTab.IsDirty = true;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        ReparseAndRedraw();
    }

    private void UpdateEditorMaxWidth()
    {
        var charWidth = Editor.TextArea.TextView.WideSpaceWidth;
        Editor.MaxWidth = charWidth * 80 + 40; // 80 chars + small extra for scrollbar
    }

    private void ReparseAndRedraw()
    {
        string text = Editor.Text;
        var ast = _parser.Parse(text);
        _colorizer.UpdateAst(ast, text);

        if (_viewModel.IsProjectMode && _viewModel.ActiveProjectFile != null)
        {
            var absPath = _viewModel.ActiveProjectFile.AbsolutePath;
            _projectFileContents[absPath] = text;
            _projectFileAsts[absPath] = ast;
            _viewModel.UpdateProjectOutline(_projectFileAsts);
        }
        else
        {
            _viewModel.UpdateOutline(ast);
        }

        Editor.TextArea.TextView.Redraw();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7 && e.KeyModifiers == KeyModifiers.None)
        {
            OnCheckSpellingClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            ToggleFocusMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _viewModel.IsFocusMode)
        {
            ToggleFocusMode();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.O)
        {
            _viewModel.ToggleOutline();
            e.Handled = true;
            return;
        }

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
                case Key.P:
                    OnPrintClick(this, new RoutedEventArgs());
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

    // Outline handlers
    private void OnToggleOutlineClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ToggleOutline();
    }

    private void OnOutlineItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is not OutlineItem item) return;

        // In project mode, switch files if needed
        if (_viewModel.IsProjectMode && item.SourceFilePath != null)
        {
            var activeFile = _viewModel.ActiveProjectFile;
            if (activeFile == null || activeFile.AbsolutePath != item.SourceFilePath)
            {
                // Find the project file and switch to it
                var targetFile = _viewModel.ProjectFiles.FirstOrDefault(
                    f => f.AbsolutePath == item.SourceFilePath);
                if (targetFile != null)
                {
                    SwitchToProjectFile(targetFile);
                }
            }
        }

        int line = item.LineNumber + 1; // Markdig 0-based -> AvaloniaEdit 1-based
        if (line > Editor.Document.LineCount) line = Editor.Document.LineCount;
        var offset = Editor.Document.GetLineByNumber(line).Offset;
        Editor.TextArea.Caret.Offset = offset;
        Editor.ScrollToLine(line);
        Editor.Focus();

        // Clear selection so the same item can be clicked again
        OutlineList.SelectedItem = null;
    }

    // Focus mode
    private void OnToggleFocusModeClick(object? sender, RoutedEventArgs e)
    {
        ToggleFocusMode();
    }

    private void ToggleFocusMode()
    {
        _viewModel.ToggleFocusMode();

        if (_viewModel.IsFocusMode)
        {
            // Save state to restore later
            _wasOutlineVisible = _viewModel.IsOutlineVisible;
            _wasProjectPaneVisible = _viewModel.IsProjectPaneVisible;
            _savedEditorScrollOffset = GetEditorScrollViewer()?.Offset;

            // Enter focus mode
            WindowState = WindowState.FullScreen;
            MainMenu.IsVisible = false;
            FormattingToolbar.IsVisible = false;
            TabBar.IsVisible = false;
            _viewModel.IsOutlineVisible = false;
            _viewModel.IsProjectPaneVisible = false;
            Editor.ShowLineNumbers = false;

            // Increase font size for fullscreen readability and constrain to 80 chars
            _savedFontSize = Editor.FontSize;
            _savedMaxWidth = Editor.MaxWidth;
            Editor.FontSize = _savedFontSize * 1.4;
            UpdateEditorMaxWidth();

            // Center the editor on screen (text stays left-aligned within)
            _savedHorizontalAlignment = Editor.HorizontalAlignment;
            Editor.HorizontalAlignment = HorizontalAlignment.Center;

            // Add dimming transformer
            Editor.TextArea.TextView.LineTransformers.Add(_focusTransformer);

            // Centering: translate the text view so the caret line stays in the middle.
            _savedTextViewTransform = Editor.TextArea.TextView.RenderTransform;
            _focusModeTransform = new TranslateTransform();
            Editor.TextArea.TextView.RenderTransform = _focusModeTransform;
            Editor.SizeChanged += OnFocusModeEditorSizeChanged;

            // Subscribe to caret changes
            Editor.TextArea.Caret.PositionChanged += OnFocusCaretPositionChanged;

            // Set initial focused line, redraw, and scroll to center (deferred for layout)
            _focusTransformer.SetFocusedLine(Editor.TextArea.Caret.Line);
            Editor.TextArea.TextView.Redraw();
            Dispatcher.UIThread.Post(ScrollCaretToCenter, DispatcherPriority.Loaded);
        }
        else
        {
            // Exit focus mode
            WindowState = WindowState.Normal;
            MainMenu.IsVisible = true;
            FormattingToolbar.IsVisible = true;
            Editor.ShowLineNumbers = true;

            // Restore outline visibility
            _viewModel.IsOutlineVisible = _wasOutlineVisible;

            // Restore project pane and tab bar visibility
            if (_viewModel.IsProjectMode)
            {
                _viewModel.IsProjectPaneVisible = _wasProjectPaneVisible;
                if (_viewModel.IsTabBarVisible)
                    TabBar.IsVisible = true;
            }

            // Restore font size, max width, and alignment
            Editor.FontSize = _savedFontSize;
            Editor.MaxWidth = _savedMaxWidth;
            Editor.HorizontalAlignment = _savedHorizontalAlignment;

            // Remove dimming transformer
            Editor.TextArea.TextView.LineTransformers.Remove(_focusTransformer);

            // Restore transform and unsubscribe from size changes
            Editor.TextArea.TextView.RenderTransform = _savedTextViewTransform;
            _savedTextViewTransform = null;
            _focusModeTransform = null;
            Editor.SizeChanged -= OnFocusModeEditorSizeChanged;

            // Unsubscribe from caret changes
            Editor.TextArea.Caret.PositionChanged -= OnFocusCaretPositionChanged;

            RestoreEditorScrollOffset();
            Editor.TextArea.TextView.Redraw();
        }
    }

    private void OnFocusCaretPositionChanged(object? sender, EventArgs e)
    {
        _focusTransformer.SetFocusedLine(Editor.TextArea.Caret.Line);
        Editor.TextArea.TextView.Redraw();
        ScrollCaretToCenter();
    }

    private void OnFocusModeEditorSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(ScrollCaretToCenter, DispatcherPriority.Loaded);
    }

    private void ScrollCaretToCenter()
    {
        if (!_viewModel.IsFocusMode) return;
        var textView = Editor.TextArea.TextView;
        Editor.TextArea.Caret.BringCaretToView();
        textView.EnsureVisualLines();
        var caretPosition = Editor.TextArea.Caret.Position;
        var caretVisual = textView.GetVisualPosition(caretPosition, VisualYPosition.LineMiddle);
        var viewportHeight = textView.Bounds.Height;
        if (viewportHeight <= 0 || double.IsNaN(caretVisual.Y) || double.IsInfinity(caretVisual.Y)) return;
        var offset = (viewportHeight / 2) - caretVisual.Y;
        if (double.IsNaN(offset) || double.IsInfinity(offset)) return;
        _focusModeTransform ??= new TranslateTransform();
        _focusModeTransform.X = 0;
        _focusModeTransform.Y = offset;
        textView.RenderTransform = _focusModeTransform;
    }

    private ScrollViewer? GetEditorScrollViewer()
    {
        return Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private void RestoreEditorScrollOffset()
    {
        if (!_savedEditorScrollOffset.HasValue) return;
        var offset = _savedEditorScrollOffset.Value;
        _savedEditorScrollOffset = null;
        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = GetEditorScrollViewer();
            if (scrollViewer != null)
                scrollViewer.Offset = offset;
        }, DispatcherPriority.Loaded);
    }

    // File operations
    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var result = await PromptSave();
            if (result == null) return; // Cancelled
        }

        CloseProject();

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

        CloseProject();

        _suppressTextChanged = true;
        Editor.Text = content;
        _suppressTextChanged = false;
        _viewModel.SetFilePath(path);
        _viewModel.MarkClean();
        _viewModel.UpdateVersionHistoryState(false);
        ReparseAndRedraw();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsProjectMode && _viewModel.ActiveProjectFile != null)
        {
            // In project mode, save directly to the file's path
            var absPath = _viewModel.ActiveProjectFile.AbsolutePath;
            await File.WriteAllTextAsync(absPath, Editor.Text);
            _projectFileContents[absPath] = Editor.Text;
            _viewModel.MarkClean();
            if (_viewModel.ActiveTab != null)
            {
                _viewModel.ActiveTab.Content = Editor.Text;
                _viewModel.ActiveTab.IsDirty = false;
            }
            if (_viewModel.Project != null)
                _viewModel.UpdateVersionHistoryState(
                    _versioningService.HasHistory(_viewModel.Project, absPath));
        }
        else
        {
            var path = await _fileService.SaveFileAsync(this, Editor.Text, _viewModel.Document.FilePath);
            if (path != null)
            {
                _viewModel.SetFilePath(path);
                _viewModel.MarkClean();
            }
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var path = await _fileService.SaveFileAsAsync(this, Editor.Text);
        if (path != null)
        {
            _viewModel.SetFilePath(path);
            _viewModel.MarkClean();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        await _printService.PrintAsync(this, Editor.Text);
    }

    private async void OnExportPdfClick(object? sender, RoutedEventArgs e)
    {
        await _printService.ExportToPdfAsync(this, Editor.Text);
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

    // Project operations

    private void CloseProject()
    {
        _viewModel.ClearTabs();
        _viewModel.Project = null;
        _viewModel.IsProjectMode = false;
        _viewModel.IsProjectPaneVisible = false;
        _viewModel.ActiveProjectFile = null;
        _viewModel.ProjectFiles.Clear();
        _projectFileContents.Clear();
        _projectFileAsts.Clear();
    }

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var result = await PromptSave();
            if (result == null) return;
        }

        var dirPath = await _fileService.OpenFolderAsync(this, "Select Folder for New Project");
        if (dirPath == null) return;

        if (_projectService.IsValidProjectDirectory(dirPath))
        {
            await ShowMessageAsync("New Project", "This folder already contains a project. Use Open Project instead.");
            return;
        }

        var project = await _projectService.CreateProjectAsync(dirPath);

        CloseProject();
        _viewModel.Project = project;
        _viewModel.IsProjectMode = true;
        _viewModel.IsProjectPaneVisible = true;

        _suppressTextChanged = true;
        Editor.Text = "";
        _suppressTextChanged = false;
        _viewModel.NewDocument();
        _viewModel.UpdateVersionHistoryState(false);
        ReparseAndRedraw();
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var result = await PromptSave();
            if (result == null) return;
        }

        var dirPath = await _fileService.OpenFolderAsync(this, "Open Project Folder");
        if (dirPath == null) return;

        if (!_projectService.IsValidProjectDirectory(dirPath))
        {
            await ShowMessageAsync("Open Project", "No project.json found in this folder.");
            return;
        }

        var project = await _projectService.LoadProjectAsync(dirPath);

        CloseProject();
        _viewModel.Project = project;
        _viewModel.IsProjectMode = true;
        _viewModel.IsProjectPaneVisible = true;

        // Load all project files
        RefreshProjectFileList();

        foreach (var pf in _viewModel.ProjectFiles)
        {
            if (File.Exists(pf.AbsolutePath))
            {
                var content = await File.ReadAllTextAsync(pf.AbsolutePath);
                _projectFileContents[pf.AbsolutePath] = content;
                _projectFileAsts[pf.AbsolutePath] = _parser.Parse(content);
            }
        }

        // Open first file
        if (_viewModel.ProjectFiles.Count > 0)
        {
            SwitchToProjectFile(_viewModel.ProjectFiles[0]);
        }
        else
        {
            _suppressTextChanged = true;
            Editor.Text = "";
            _suppressTextChanged = false;
            _viewModel.NewDocument();
        }

        _viewModel.UpdateProjectOutline(_projectFileAsts);
    }

    private void RefreshProjectFileList()
    {
        var project = _viewModel.Project;
        if (project == null) return;

        _suppressProjectFileSelection = true;
        var activeAbsPath = _viewModel.ActiveProjectFile?.AbsolutePath;
        _viewModel.ProjectFiles.Clear();

        foreach (var relativePath in project.FileOrder)
        {
            var absPath = Path.Combine(project.DirectoryPath, relativePath);
            _viewModel.ProjectFiles.Add(new ProjectFile
            {
                RelativePath = relativePath,
                AbsolutePath = absPath
            });
        }

        // Restore selection
        if (activeAbsPath != null)
        {
            var match = _viewModel.ProjectFiles.FirstOrDefault(f => f.AbsolutePath == activeAbsPath);
            if (match != null)
                _viewModel.ActiveProjectFile = match;
        }
        _suppressProjectFileSelection = false;
    }

    private void SwitchToProjectFile(ProjectFile targetFile)
    {
        // Save current tab state (content, caret, scroll)
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.Content = Editor.Text;
            _viewModel.ActiveTab.CaretOffset = Editor.TextArea.Caret.Offset;
            _viewModel.ActiveTab.ScrollOffset = GetEditorScrollViewer()?.Offset.Y ?? 0;
            _viewModel.ActiveTab.IsDirty = _viewModel.IsDirty;

            // Also sync to project file contents/disk
            if (_viewModel.IsDirty && _viewModel.ActiveProjectFile != null)
            {
                var currentPath = _viewModel.ActiveProjectFile.AbsolutePath;
                _projectFileContents[currentPath] = Editor.Text;
                File.WriteAllText(currentPath, Editor.Text);
            }
        }

        // Open or activate tab for target file
        var tab = _viewModel.OpenTab(targetFile);

        _suppressProjectFileSelection = true;
        _viewModel.ActiveProjectFile = targetFile;
        _suppressProjectFileSelection = false;

        // Load content from tab (or from project file contents as fallback)
        _suppressTextChanged = true;
        if (!string.IsNullOrEmpty(tab.Content))
            Editor.Text = tab.Content;
        else if (_projectFileContents.TryGetValue(targetFile.AbsolutePath, out var content))
        {
            Editor.Text = content;
            tab.Content = content;
        }
        else
            Editor.Text = "";
        _suppressTextChanged = false;

        _viewModel.SetFilePath(targetFile.AbsolutePath);
        if (tab.IsDirty)
            _viewModel.MarkDirty();
        else
            _viewModel.MarkClean();

        // Restore caret and scroll position
        if (tab.CaretOffset > 0 && tab.CaretOffset <= Editor.Document.TextLength)
            Editor.TextArea.Caret.Offset = tab.CaretOffset;
        if (tab.ScrollOffset > 0)
        {
            var scrollOffset = tab.ScrollOffset;
            Dispatcher.UIThread.Post(() =>
            {
                var sv = GetEditorScrollViewer();
                if (sv != null)
                    sv.Offset = new Vector(sv.Offset.X, scrollOffset);
            }, DispatcherPriority.Loaded);
        }

        if (_viewModel.Project != null)
            _viewModel.UpdateVersionHistoryState(
                _versioningService.HasHistory(_viewModel.Project, targetFile.AbsolutePath));

        ReparseAndRedraw();
        UpdateTabStyles();
    }

    private void OnProjectFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressProjectFileSelection) return;
        if (ProjectFileList.SelectedItem is not ProjectFile selectedFile) return;

        SwitchToProjectFile(selectedFile);
    }

    // Tab bar handlers
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not EditorTab tab) return;
        if (_viewModel.ActiveTab == tab) return;

        SwitchToProjectFile(tab.ProjectFile);
    }

    private async void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        // Walk up to find the tab DataContext
        var parent = btn.Parent; // StackPanel
        var border = parent?.Parent as Border; // TabBorder
        if (border?.DataContext is not EditorTab tab) return;

        // Prompt save if dirty
        if (tab.IsDirty)
        {
            var confirmed = await ShowConfirmDialogAsync("Unsaved Changes",
                $"Save changes to '{tab.ProjectFile.FileName}' before closing?");
            if (confirmed)
            {
                await File.WriteAllTextAsync(tab.ProjectFile.AbsolutePath, tab.Content);
                _projectFileContents[tab.ProjectFile.AbsolutePath] = tab.Content;
            }
        }

        var nextTab = _viewModel.CloseTab(tab);
        if (nextTab != null)
        {
            SwitchToProjectFile(nextTab.ProjectFile);
        }
        else
        {
            _viewModel.ActiveProjectFile = null;
            _suppressTextChanged = true;
            Editor.Text = "";
            _suppressTextChanged = false;
            _viewModel.NewDocument();
            _viewModel.UpdateVersionHistoryState(false);
        }
    }

    private void UpdateTabStyles()
    {
        if (TabItemsControl == null) return;

        // Defer to ensure the visual tree is updated
        Dispatcher.UIThread.Post(() =>
        {
            var containers = TabItemsControl.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Name == "TabBorder");

            foreach (var border in containers)
            {
                if (border.DataContext is EditorTab tab)
                {
                    bool isActive = tab == _viewModel.ActiveTab;
                    border.Background = isActive
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromRgb(232, 232, 232));
                    border.BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0, 122, 204))
                        : new SolidColorBrush(Color.FromRgb(204, 204, 204));
                    border.BorderThickness = isActive
                        ? new Thickness(1, 2, 1, 0)
                        : new Thickness(1, 1, 1, 0);
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private async void OnAddFileToProjectClick(object? sender, RoutedEventArgs e)
    {
        var project = _viewModel.Project;
        if (project == null) return;

        var choice = await ShowAddFileChoiceDialogAsync();
        if (choice == null) return;

        if (choice == "new")
        {
            var fileName = await ShowNewFileNameDialogAsync();
            if (string.IsNullOrWhiteSpace(fileName)) return;

            if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                fileName += ".md";

            var absPath = Path.Combine(project.DirectoryPath, fileName);
            if (File.Exists(absPath))
            {
                await ShowMessageAsync("Add File", "A file with that name already exists.");
                return;
            }

            await File.WriteAllTextAsync(absPath, "");
            _projectService.AddFile(project, fileName);
            await _projectService.SaveProjectAsync(project);

            _projectFileContents[absPath] = "";
            _projectFileAsts[absPath] = _parser.Parse("");

            RefreshProjectFileList();
            _viewModel.UpdateProjectOutline(_projectFileAsts);

            // Auto-switch to the newly created file
            var newFile = _viewModel.ProjectFiles.FirstOrDefault(f => f.AbsolutePath == absPath);
            if (newFile != null)
                SwitchToProjectFile(newFile);
        }
        else // "existing"
        {
            var (path, content) = await _fileService.OpenFileAsync(this);
            if (path == null || content == null) return;

            // Compute relative path from project dir
            var relativePath = Path.GetRelativePath(project.DirectoryPath, path);

            // If outside project dir, copy the file in
            if (relativePath.StartsWith(".."))
            {
                var fileName = Path.GetFileName(path);
                var destPath = Path.Combine(project.DirectoryPath, fileName);
                if (File.Exists(destPath))
                {
                    await ShowMessageAsync("Add File",
                        $"A file named '{fileName}' already exists in the project directory.");
                    return;
                }
                File.Copy(path, destPath);
                relativePath = fileName;
                path = destPath;
            }

            if (project.FileOrder.Contains(relativePath))
            {
                await ShowMessageAsync("Add File", "This file is already in the project.");
                return;
            }

            _projectService.AddFile(project, relativePath);
            await _projectService.SaveProjectAsync(project);

            _projectFileContents[path] = content;
            _projectFileAsts[path] = _parser.Parse(content);

            RefreshProjectFileList();
            _viewModel.UpdateProjectOutline(_projectFileAsts);

            // Auto-switch to the added file
            var addedFile = _viewModel.ProjectFiles.FirstOrDefault(f => f.AbsolutePath == path);
            if (addedFile != null)
                SwitchToProjectFile(addedFile);
        }
    }

    private async void OnRemoveFileFromProjectClick(object? sender, RoutedEventArgs e)
    {
        var project = _viewModel.Project;
        if (project == null || _viewModel.ActiveProjectFile == null) return;

        var confirmed = await ShowConfirmDialogAsync("Remove File",
            $"Remove '{_viewModel.ActiveProjectFile.FileName}' from the project?\n(The file will not be deleted from disk.)");
        if (!confirmed) return;

        var removedFile = _viewModel.ActiveProjectFile;
        _projectService.RemoveFile(project, removedFile.RelativePath);
        await _projectService.SaveProjectAsync(project);

        _projectFileContents.Remove(removedFile.AbsolutePath);
        _projectFileAsts.Remove(removedFile.AbsolutePath);

        // Close the tab for the removed file
        var tabToClose = _viewModel.OpenTabs.FirstOrDefault(
            t => t.ProjectFile.AbsolutePath == removedFile.AbsolutePath);
        EditorTab? nextTab = null;
        if (tabToClose != null)
            nextTab = _viewModel.CloseTab(tabToClose);

        RefreshProjectFileList();
        _viewModel.UpdateProjectOutline(_projectFileAsts);

        // Switch to adjacent tab or first remaining file
        if (nextTab != null)
        {
            SwitchToProjectFile(nextTab.ProjectFile);
        }
        else if (_viewModel.ProjectFiles.Count > 0)
        {
            SwitchToProjectFile(_viewModel.ProjectFiles[0]);
        }
        else
        {
            _suppressTextChanged = true;
            Editor.Text = "";
            _suppressTextChanged = false;
            _viewModel.NewDocument();
            _viewModel.UpdateVersionHistoryState(false);
        }
    }

    private async void OnMoveFileUpClick(object? sender, RoutedEventArgs e)
    {
        var project = _viewModel.Project;
        if (project == null || _viewModel.ActiveProjectFile == null) return;

        var idx = project.FileOrder.IndexOf(_viewModel.ActiveProjectFile.RelativePath);
        if (idx <= 0) return;

        _projectService.MoveFile(project, idx, idx - 1);
        await _projectService.SaveProjectAsync(project);

        RefreshProjectFileList();
        _viewModel.UpdateProjectOutline(_projectFileAsts);
    }

    private async void OnMoveFileDownClick(object? sender, RoutedEventArgs e)
    {
        var project = _viewModel.Project;
        if (project == null || _viewModel.ActiveProjectFile == null) return;

        var idx = project.FileOrder.IndexOf(_viewModel.ActiveProjectFile.RelativePath);
        if (idx < 0 || idx >= project.FileOrder.Count - 1) return;

        _projectService.MoveFile(project, idx, idx + 1);
        await _projectService.SaveProjectAsync(project);

        RefreshProjectFileList();
        _viewModel.UpdateProjectOutline(_projectFileAsts);
    }

    // Version operations
    private async void OnCommitClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsProjectMode || _viewModel.Project == null)
        {
            await ShowMessageAsync("Commit", "Versioning is only available in project mode.");
            return;
        }

        var filePath = _viewModel.Document.FilePath;
        if (filePath == null)
        {
            await ShowMessageAsync("Commit", "No file is currently open.");
            return;
        }

        if (_viewModel.IsDirty)
        {
            var choice = await ShowSaveBeforeCommitDialogAsync();
            if (choice == null) return; // Cancelled

            if (choice == true) // Save & Commit
            {
                await File.WriteAllTextAsync(filePath, Editor.Text);
                _projectFileContents[filePath] = Editor.Text;
                _viewModel.MarkClean();
            }
            // choice == false means "Commit Saved Version" -- proceed without saving
        }

        var message = await ShowCommitDialogAsync();
        if (message == null) return; // Cancelled

        await _versioningService.CommitAsync(_viewModel.Project, filePath, message);
        _viewModel.UpdateVersionHistoryState(true);
    }

    private async void OnViewHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsProjectMode || _viewModel.Project == null) return;
        var filePath = _viewModel.Document.FilePath;
        if (filePath == null) return;

        var history = await _versioningService.LoadHistoryAsync(_viewModel.Project, filePath);
        if (history.Commits.Count == 0) return;

        await ShowHistoryDialogAsync(history);
    }

    private async void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsProjectMode || _viewModel.Project == null) return;
        var filePath = _viewModel.Document.FilePath;
        if (filePath == null) return;

        var history = await _versioningService.LoadHistoryAsync(_viewModel.Project, filePath);
        if (history.Commits.Count == 0) return;

        var currentContent = Editor.Text;
        var commitId = await ShowRevertDialogAsync(history, filePath, currentContent);
        if (commitId == null) return;

        var content = await _versioningService.GetSnapshotContentAsync(_viewModel.Project, filePath, commitId);

        _suppressTextChanged = true;
        Editor.Text = content;
        _suppressTextChanged = false;

        // Save reverted content to disk
        await File.WriteAllTextAsync(filePath, content);
        _projectFileContents[filePath] = content;
        _viewModel.MarkClean();
        ReparseAndRedraw();
    }

    // Spell check
    private async void OnCheckSpellingClick(object? sender, RoutedEventArgs e)
    {
        await _spellCheckService.EnsureLoadedAsync();

        var text = Editor.Text;
        var ast = _parser.Parse(text);
        var extractor = new MarkdownWordExtractor();
        var allWords = extractor.ExtractWords(text, ast);

        // Group by word (case-insensitive), filter misspelled
        var misspelled = new List<MisspelledWord>();
        var grouped = new Dictionary<string, MisspelledWord>(StringComparer.OrdinalIgnoreCase);

        foreach (var occ in allWords)
        {
            if (_spellCheckService.IsCorrect(occ.Word)) continue;

            if (!grouped.TryGetValue(occ.Word, out var entry))
            {
                entry = new MisspelledWord
                {
                    Word = occ.Word,
                    Suggestions = _spellCheckService.GetSuggestions(occ.Word)
                };
                grouped[occ.Word] = entry;
                misspelled.Add(entry);
            }

            entry.Locations.Add(new WordLocation(occ.Offset, occ.Length));
        }

        if (misspelled.Count == 0)
        {
            await ShowMessageAsync("Spell Check", "No misspellings found.");
            return;
        }

        await ShowSpellCheckDialogAsync(misspelled);
    }

    private async System.Threading.Tasks.Task ShowSpellCheckDialogAsync(List<MisspelledWord> misspelled)
    {
        var dialog = new Window
        {
            Title = "Spell Check",
            Width = 700,
            Height = 450,
            MinWidth = 500,
            MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int currentOccurrenceIndex = 0;

        // Main layout
        var rootPanel = new DockPanel { Margin = new Thickness(12) };

        // Bottom close button
        var closeBtn = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        closeBtn.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(closeBtn, Avalonia.Controls.Dock.Bottom);
        rootPanel.Children.Add(closeBtn);

        // Main content grid: left (word list) | right (details)
        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,1.5*")
        };

        // Left panel - misspelled word list
        var leftPanel = new DockPanel();
        var wordCountLabel = new TextBlock
        {
            Text = $"Misspelled Words ({misspelled.Count})",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(wordCountLabel, Avalonia.Controls.Dock.Top);
        leftPanel.Children.Add(wordCountLabel);

        var wordList = new ListBox();
        foreach (var word in misspelled)
        {
            wordList.Items.Add(new ListBoxItem
            {
                Content = $"{word.Word}  ({word.Locations.Count})",
                Tag = word
            });
        }
        leftPanel.Children.Add(wordList);

        // Right panel - details and actions
        var rightPanel = new StackPanel { Spacing = 8 };

        var wordLabel = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 16 };
        var occurrenceLabel = new TextBlock { Foreground = Brushes.Gray };
        rightPanel.Children.Add(wordLabel);
        rightPanel.Children.Add(occurrenceLabel);

        var suggestionsLabel = new TextBlock { Text = "Suggestions:", Margin = new Thickness(0, 4, 0, 0) };
        rightPanel.Children.Add(suggestionsLabel);

        var suggestionsList = new ListBox { Height = 120 };
        rightPanel.Children.Add(suggestionsList);

        var replaceLabel = new TextBlock { Text = "Replace with:", Margin = new Thickness(0, 4, 0, 0) };
        rightPanel.Children.Add(replaceLabel);

        var replaceBox = new TextBox();
        rightPanel.Children.Add(replaceBox);

        // Action buttons
        var actionRow1 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var replaceBtn = new Button { Content = "Replace" };
        var replaceAllBtn = new Button { Content = "Replace All" };
        actionRow1.Children.Add(replaceBtn);
        actionRow1.Children.Add(replaceAllBtn);
        rightPanel.Children.Add(actionRow1);

        var actionRow2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var ignoreBtn = new Button { Content = "Ignore" };
        var ignoreAllBtn = new Button { Content = "Ignore All" };
        var addDictBtn = new Button { Content = "Add to Dictionary" };
        actionRow2.Children.Add(ignoreBtn);
        actionRow2.Children.Add(ignoreAllBtn);
        actionRow2.Children.Add(addDictBtn);
        rightPanel.Children.Add(actionRow2);

        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(rightPanel, 2);
        mainGrid.Children.Add(leftPanel);
        mainGrid.Children.Add(rightPanel);

        rootPanel.Children.Add(mainGrid);
        dialog.Content = rootPanel;

        // Track cumulative offset changes from replacements
        int cumulativeOffset = 0;

        // Helper to select/highlight word in editor
        void HighlightWord(MisspelledWord word, int occIndex)
        {
            if (occIndex < 0 || occIndex >= word.Locations.Count) return;
            var loc = word.Locations[occIndex];
            int adjustedOffset = loc.Offset + cumulativeOffset;
            if (adjustedOffset >= 0 && adjustedOffset + loc.Length <= Editor.Document.TextLength)
            {
                Editor.Select(adjustedOffset, loc.Length);
                var line = Editor.Document.GetLineByOffset(adjustedOffset);
                Editor.ScrollToLine(line.LineNumber);
            }
        }

        // Helper to update display for selected word
        void UpdateDisplay(MisspelledWord? word)
        {
            if (word == null)
            {
                wordLabel.Text = "";
                occurrenceLabel.Text = "";
                suggestionsList.Items.Clear();
                replaceBox.Text = "";
                return;
            }

            wordLabel.Text = $"\"{word.Word}\"";
            occurrenceLabel.Text = word.Locations.Count > 0
                ? $"Occurrence {currentOccurrenceIndex + 1} of {word.Locations.Count}"
                : "No occurrences";

            suggestionsList.Items.Clear();
            foreach (var s in word.Suggestions)
                suggestionsList.Items.Add(s);

            if (word.Suggestions.Count > 0)
            {
                replaceBox.Text = word.Suggestions[0];
                suggestionsList.SelectedIndex = 0;
            }
            else
            {
                replaceBox.Text = "";
            }

            if (word.Locations.Count > 0)
                HighlightWord(word, currentOccurrenceIndex);
        }

        // Helper to remove current word from list and advance
        void RemoveCurrentWordFromList()
        {
            int selectedIdx = wordList.SelectedIndex;
            if (selectedIdx < 0) return;

            wordList.Items.RemoveAt(selectedIdx);
            wordCountLabel.Text = $"Misspelled Words ({wordList.Items.Count})";

            if (wordList.Items.Count == 0)
            {
                UpdateDisplay(null);
                return;
            }

            if (selectedIdx >= wordList.Items.Count)
                selectedIdx = wordList.Items.Count - 1;
            wordList.SelectedIndex = selectedIdx;
        }

        // Helper to advance to next occurrence or next word
        void AdvanceOccurrence(MisspelledWord word)
        {
            currentOccurrenceIndex++;
            if (currentOccurrenceIndex >= word.Locations.Count)
            {
                // All occurrences handled, remove word from list
                RemoveCurrentWordFromList();
            }
            else
            {
                UpdateDisplay(word);
            }
        }

        // Wire up word list selection
        wordList.SelectionChanged += (_, _) =>
        {
            if (wordList.SelectedItem is ListBoxItem item && item.Tag is MisspelledWord word)
            {
                currentOccurrenceIndex = 0;
                UpdateDisplay(word);
            }
        };

        // Wire up suggestion selection to fill replace box
        suggestionsList.SelectionChanged += (_, _) =>
        {
            if (suggestionsList.SelectedItem is string suggestion)
                replaceBox.Text = suggestion;
        };

        // Replace button
        replaceBtn.Click += (_, _) =>
        {
            if (wordList.SelectedItem is not ListBoxItem item || item.Tag is not MisspelledWord word) return;
            if (currentOccurrenceIndex >= word.Locations.Count) return;
            var replacement = replaceBox.Text ?? "";

            var loc = word.Locations[currentOccurrenceIndex];
            int adjustedOffset = loc.Offset + cumulativeOffset;

            if (adjustedOffset >= 0 && adjustedOffset + loc.Length <= Editor.Document.TextLength)
            {
                Editor.Document.Replace(adjustedOffset, loc.Length, replacement);
                int delta = replacement.Length - loc.Length;
                cumulativeOffset += delta;

                // Adjust offsets of remaining locations for this word
                for (int i = currentOccurrenceIndex + 1; i < word.Locations.Count; i++)
                {
                    var nextLoc = word.Locations[i];
                    if (nextLoc.Offset > loc.Offset)
                    {
                        word.Locations[i] = new WordLocation(nextLoc.Offset + delta, nextLoc.Length);
                    }
                }
            }

            // Remove this occurrence
            word.Locations.RemoveAt(currentOccurrenceIndex);

            // Update the list item text
            if (word.Locations.Count > 0)
            {
                item.Content = $"{word.Word}  ({word.Locations.Count})";
            }

            if (word.Locations.Count == 0 || currentOccurrenceIndex >= word.Locations.Count)
            {
                if (word.Locations.Count == 0)
                    RemoveCurrentWordFromList();
                else
                {
                    currentOccurrenceIndex = 0;
                    UpdateDisplay(word);
                }
            }
            else
            {
                UpdateDisplay(word);
            }
        };

        // Replace All button
        replaceAllBtn.Click += (_, _) =>
        {
            if (wordList.SelectedItem is not ListBoxItem item || item.Tag is not MisspelledWord word) return;
            var replacement = replaceBox.Text ?? "";

            // Replace from end to start to maintain offset consistency
            var sortedLocs = word.Locations.OrderByDescending(l => l.Offset).ToList();
            foreach (var loc in sortedLocs)
            {
                int adjustedOffset = loc.Offset + cumulativeOffset;
                if (adjustedOffset >= 0 && adjustedOffset + loc.Length <= Editor.Document.TextLength)
                {
                    Editor.Document.Replace(adjustedOffset, loc.Length, replacement);
                    cumulativeOffset += replacement.Length - loc.Length;
                }
            }

            word.Locations.Clear();
            RemoveCurrentWordFromList();
        };

        // Ignore button
        ignoreBtn.Click += (_, _) =>
        {
            if (wordList.SelectedItem is not ListBoxItem item || item.Tag is not MisspelledWord word) return;
            AdvanceOccurrence(word);
        };

        // Ignore All button
        ignoreAllBtn.Click += (_, _) =>
        {
            if (wordList.SelectedItem is not ListBoxItem item || item.Tag is not MisspelledWord word) return;
            ignoredWords.Add(word.Word);
            RemoveCurrentWordFromList();
        };

        // Add to Dictionary button
        addDictBtn.Click += async (_, _) =>
        {
            if (wordList.SelectedItem is not ListBoxItem item || item.Tag is not MisspelledWord word) return;
            await _spellCheckService.AddToUserDictionaryAsync(word.Word);
            RemoveCurrentWordFromList();
        };

        // Select first word
        if (wordList.Items.Count > 0)
            wordList.SelectedIndex = 0;

        await dialog.ShowDialog(this);
    }

    // Dialog helpers

    private async System.Threading.Tasks.Task<string?> ShowAddFileChoiceDialogAsync()
    {
        var dialog = new Window
        {
            Title = "Add File",
            Width = 350,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "How would you like to add a file?" });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var newBtn = new Button { Content = "New File" };
        newBtn.Click += (_, _) => { result = "new"; dialog.Close(); };

        var existingBtn = new Button { Content = "Add Existing" };
        existingBtn.Click += (_, _) => { result = "existing"; dialog.Close(); };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(newBtn);
        buttons.Children.Add(existingBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task<string?> ShowNewFileNameDialogAsync()
    {
        var dialog = new Window
        {
            Title = "New File",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Enter file name:" });

        var textBox = new TextBox { Watermark = "e.g. chapter-1.md" };
        panel.Children.Add(textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var createBtn = new Button { Content = "Create" };
        createBtn.Click += (_, _) =>
        {
            var name = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                result = name;
                dialog.Close();
            }
        };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(createBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }

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

            var snapshotContent = await _versioningService.GetSnapshotContentAsync(
                _viewModel.Project!, filePath, selectedId);
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
            if (_viewModel.IsProjectMode && _viewModel.ActiveProjectFile != null)
            {
                var absPath = _viewModel.ActiveProjectFile.AbsolutePath;
                await File.WriteAllTextAsync(absPath, Editor.Text);
                _projectFileContents[absPath] = Editor.Text;
                _viewModel.MarkClean();
                dialogResult = true;
                dialog.Close();
            }
            else
            {
                var path = await _fileService.SaveFileAsync(this, Editor.Text, _viewModel.Document.FilePath);
                if (path != null)
                {
                    _viewModel.SetFilePath(path);
                    _viewModel.MarkClean();
                    dialogResult = true;
                    dialog.Close();
                }
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
