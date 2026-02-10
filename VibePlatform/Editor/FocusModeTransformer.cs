using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VibePlatform.Editor;

public class FocusModeTransformer : DocumentColorizingTransformer
{
    private int _focusedLineNumber = 1; // 1-based (AvaloniaEdit convention)
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#999999"));

    public void SetFocusedLine(int lineNumber) => _focusedLineNumber = lineNumber;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.LineNumber == _focusedLineNumber) return;

        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            element.TextRunProperties.SetForegroundBrush(DimBrush);
        });
    }
}
