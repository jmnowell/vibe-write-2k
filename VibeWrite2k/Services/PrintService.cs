using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VibePlatform.Services;

public class PrintService
{
    private static bool _licenseConfigured;
    private readonly MarkdownPipeline _pipeline;

    public PrintService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseEmphasisExtras(EmphasisExtraOptions.Inserted)
            .Build();

        if (!_licenseConfigured)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            _licenseConfigured = true;
        }
    }

    public void GeneratePdf(string markdown, Stream output)
    {
        var doc = Markdown.Parse(markdown, _pipeline);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(1, Unit.Inch);
                page.MarginBottom(1, Unit.Inch);
                page.MarginLeft(1, Unit.Inch);
                page.MarginRight(1, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(14));

                page.Content().Column(column =>
                {
                    foreach (var block in doc)
                    {
                        RenderBlock(column, block);
                    }
                });
            });
        }).GeneratePdf(output);
    }

    private void RenderBlock(ColumnDescriptor column, Block block)
    {
        if (block is HeadingBlock heading)
        {
            float fontSize = heading.Level switch
            {
                1 => 28,
                2 => 22,
                3 => 18,
                _ => 14
            };

            column.Item().PaddingTop(4).PaddingBottom(2).Text(text =>
            {
                if (heading.Inline != null)
                {
                    WalkInlines(text, heading.Inline, bold: true, italic: false, underline: false, fontSize: fontSize);
                }
            });
        }
        else if (block is ParagraphBlock paragraph)
        {
            column.Item().PaddingBottom(4).Text(text =>
            {
                if (paragraph.Inline != null)
                {
                    WalkInlines(text, paragraph.Inline, bold: false, italic: false, underline: false, fontSize: 14);
                }
            });
        }
        else
        {
            // Fallback: render as plain text
            var plainText = block.ToString();
            if (!string.IsNullOrEmpty(plainText))
            {
                column.Item().PaddingBottom(4).Text(plainText).FontSize(14);
            }
        }
    }

    private void WalkInlines(TextDescriptor text, ContainerInline container, bool bold, bool italic, bool underline, float fontSize)
    {
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
            {
                EmitSpan(text, literal.Content.ToString(), bold, italic, underline, fontSize);
            }
            else if (inline is EmphasisInline emphasis)
            {
                bool newBold = bold;
                bool newItalic = italic;
                bool newUnderline = underline;

                if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 2)
                    newBold = true;
                else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 1)
                    newItalic = true;
                else if (emphasis.DelimiterChar == '+' && emphasis.DelimiterCount == 2)
                    newUnderline = true;

                WalkInlines(text, emphasis, newBold, newItalic, newUnderline, fontSize);
            }
            else if (inline is LineBreakInline)
            {
                text.Span("\n");
            }
            else if (inline is ContainerInline childContainer)
            {
                WalkInlines(text, childContainer, bold, italic, underline, fontSize);
            }
        }
    }

    private void EmitSpan(TextDescriptor text, string content, bool bold, bool italic, bool underline, float fontSize)
    {
        var span = text.Span(content).FontSize(fontSize);

        if (bold)
            span.Bold();
        if (italic)
            span.Italic();
        if (underline)
            span.Underline();
    }

    public async Task ExportToPdfAsync(Window owner, string markdown)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to PDF",
            DefaultExtension = "pdf",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF Files") { Patterns = new[] { "*.pdf" } }
            }
        });

        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        GeneratePdf(markdown, stream);
    }

    public List<string> GetAvailablePrinters()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return new List<string>();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ')[0])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public string? GetDefaultPrinter()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-d",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Output format: "system default destination: PrinterName"
            var parts = output.Split(':');
            if (parts.Length >= 2)
                return parts[1].Trim();

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task PrintAsync(Window owner, string markdown)
    {
        var printers = GetAvailablePrinters();

        if (printers.Count == 0)
        {
            await ShowMessageAsync(owner, "Print", "No printers found. Make sure CUPS is installed and configured.");
            return;
        }

        var defaultPrinter = GetDefaultPrinter();
        var selectedPrinter = await ShowPrinterDialogAsync(owner, printers, defaultPrinter);
        if (selectedPrinter == null) return;

        // Generate temp PDF
        var tempFile = Path.Combine(Path.GetTempPath(), $"vibe-print-{Guid.NewGuid()}.pdf");
        try
        {
            using (var stream = File.Create(tempFile))
            {
                GeneratePdf(markdown, stream);
            }

            // Send to printer via CUPS lp command
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "lp",
                Arguments = $"-d {selectedPrinter} \"{tempFile}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process != null)
            {
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    await ShowMessageAsync(owner, "Print Error", $"Failed to send to printer: {error}");
                }
            }
        }
        finally
        {
            // Clean up temp file after a delay to let the spooler read it
            _ = Task.Run(async () =>
            {
                await Task.Delay(30000);
                try { File.Delete(tempFile); } catch { }
            });
        }
    }

    private async Task<string?> ShowPrinterDialogAsync(Window owner, List<string> printers, string? defaultPrinter)
    {
        var dialog = new Window
        {
            Title = "Print",
            Width = 350,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Select a printer:" });

        var listBox = new ListBox { Height = 150 };
        int defaultIndex = -1;
        for (int i = 0; i < printers.Count; i++)
        {
            listBox.Items.Add(printers[i]);
            if (printers[i] == defaultPrinter)
                defaultIndex = i;
        }

        if (defaultIndex >= 0)
            listBox.SelectedIndex = defaultIndex;
        else if (printers.Count > 0)
            listBox.SelectedIndex = 0;

        panel.Children.Add(listBox);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var printBtn = new Button { Content = "Print" };
        printBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is string selected)
            {
                result = selected;
                dialog.Close();
            }
        };

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(printBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        return result;
    }

    private async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 350,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var okBtn = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        okBtn.Click += (_, _) => dialog.Close();
        panel.Children.Add(okBtn);

        dialog.Content = panel;
        await dialog.ShowDialog(owner);
    }
}
