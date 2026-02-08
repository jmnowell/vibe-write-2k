using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private readonly MarkdownSyntaxHider _syntaxHider;
    private readonly FileService _fileService;
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
        _syntaxHider = new MarkdownSyntaxHider();
        _fileService = new FileService();

        // Set up debounce timer for re-parsing
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounceTimer.Tick += OnDebounceTimerTick;

        // Wire up editor transformers
        // DISABLED FOR DEBUG: Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        // DISABLED FOR DEBUG: Editor.TextArea.TextView.ElementGenerators.Add(_syntaxHider);

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

        // TEST: Load sample text after window is shown
        Opened += (_, _) =>
        {
            Editor.Document.Text = "Hello World - this is a test.";
        };
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
        _colorizer.UpdateAst(ast);
        _syntaxHider.UpdateAst(ast, text);
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
        ReparseAndRedraw();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var path = await _fileService.SaveFileAsync(this, Editor.Text, _viewModel.Document.FilePath);
        if (path != null)
        {
            _viewModel.SetFilePath(path);
            _viewModel.MarkClean();
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

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = "Do you want to save changes to the current document?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };

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
