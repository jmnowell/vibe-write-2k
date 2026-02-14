using System;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace VibePlatform.Editor;

public static class FormattingCommands
{
    public static void ToggleBold(TextEditor editor)
    {
        ToggleInlineMarkdown(editor, "**");
    }

    public static void ToggleItalic(TextEditor editor)
    {
        ToggleInlineMarkdown(editor, "*");
    }

    public static void ToggleUnderline(TextEditor editor)
    {
        ToggleInlineMarkdown(editor, "++");
    }

    public static void CycleHeader(TextEditor editor, int targetLevel)
    {
        var doc = editor.Document;
        var line = doc.GetLineByOffset(editor.CaretOffset);
        string lineText = doc.GetText(line.Offset, line.Length);

        // Determine current header level
        int currentLevel = 0;
        int prefixLen = 0;
        if (lineText.StartsWith("### "))
        {
            currentLevel = 3;
            prefixLen = 4;
        }
        else if (lineText.StartsWith("## "))
        {
            currentLevel = 2;
            prefixLen = 3;
        }
        else if (lineText.StartsWith("# "))
        {
            currentLevel = 1;
            prefixLen = 2;
        }

        // If targetLevel is 0 (Normal), just strip the prefix
        // If targetLevel matches current, strip it (toggle off)
        // Otherwise apply the new level
        string newPrefix;
        if (targetLevel == 0 || targetLevel == currentLevel)
        {
            newPrefix = "";
        }
        else
        {
            newPrefix = new string('#', targetLevel) + " ";
        }

        string contentText = lineText.Substring(prefixLen);
        int caretInLine = editor.CaretOffset - line.Offset;

        doc.Replace(line.Offset, line.Length, newPrefix + contentText);

        // Adjust caret position
        int newCaretInLine = Math.Max(0, caretInLine - prefixLen + newPrefix.Length);
        editor.CaretOffset = line.Offset + Math.Min(newCaretInLine, newPrefix.Length + contentText.Length);
    }

    private static void ToggleInlineMarkdown(TextEditor editor, string delimiter)
    {
        var doc = editor.Document;
        int selStart = editor.SelectionStart;
        int selLength = editor.SelectionLength;

        if (selLength > 0)
        {
            string selected = doc.GetText(selStart, selLength);

            // Check if already wrapped with delimiter
            int dLen = delimiter.Length;
            bool alreadyWrapped = selStart >= dLen
                && selStart + selLength + dLen <= doc.TextLength
                && doc.GetText(selStart - dLen, dLen) == delimiter
                && doc.GetText(selStart + selLength, dLen) == delimiter;

            if (alreadyWrapped)
            {
                // Remove delimiters
                doc.BeginUpdate();
                doc.Remove(selStart + selLength, dLen);
                doc.Remove(selStart - dLen, dLen);
                doc.EndUpdate();
                editor.Select(selStart - dLen, selLength);
            }
            else
            {
                // Check if selection itself contains the delimiters at edges
                bool innerWrapped = selected.StartsWith(delimiter) && selected.EndsWith(delimiter)
                    && selected.Length >= dLen * 2;

                if (innerWrapped)
                {
                    string unwrapped = selected.Substring(dLen, selected.Length - dLen * 2);
                    doc.Replace(selStart, selLength, unwrapped);
                    editor.Select(selStart, unwrapped.Length);
                }
                else
                {
                    // Wrap with delimiter
                    string wrapped = delimiter + selected + delimiter;
                    doc.Replace(selStart, selLength, wrapped);
                    editor.Select(selStart + dLen, selLength);
                }
            }
        }
        else
        {
            // No selection: insert paired delimiters with caret between
            int offset = editor.CaretOffset;
            doc.Insert(offset, delimiter + delimiter);
            editor.CaretOffset = offset + delimiter.Length;
        }
    }
}
