using System;
using System.Collections.Generic;

namespace VibePlatform.Services;

public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted
}

public readonly record struct DiffLine(string Text, DiffLineType Type);

public class DiffResult
{
    public List<DiffLine> LeftLines { get; } = new();
    public List<DiffLine> RightLines { get; } = new();
}

/// <summary>
/// Simple line-level diff using longest common subsequence.
/// Produces side-by-side output with blank padding for alignment.
/// </summary>
public static class LineDiffService
{
    public static DiffResult Diff(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        // Build LCS table
        int m = oldLines.Length;
        int n = newLines.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = m - 1; i >= 0; i--)
        {
            for (int j = n - 1; j >= 0; j--)
            {
                if (oldLines[i] == newLines[j])
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        // Walk the table to produce side-by-side diff
        var result = new DiffResult();
        int oi = 0, ni = 0;

        while (oi < m && ni < n)
        {
            if (oldLines[oi] == newLines[ni])
            {
                result.LeftLines.Add(new DiffLine(oldLines[oi], DiffLineType.Unchanged));
                result.RightLines.Add(new DiffLine(newLines[ni], DiffLineType.Unchanged));
                oi++;
                ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                // Delete from old
                result.LeftLines.Add(new DiffLine(oldLines[oi], DiffLineType.Deleted));
                result.RightLines.Add(new DiffLine("", DiffLineType.Unchanged));
                oi++;
            }
            else
            {
                // Add in new
                result.LeftLines.Add(new DiffLine("", DiffLineType.Unchanged));
                result.RightLines.Add(new DiffLine(newLines[ni], DiffLineType.Added));
                ni++;
            }
        }

        while (oi < m)
        {
            result.LeftLines.Add(new DiffLine(oldLines[oi], DiffLineType.Deleted));
            result.RightLines.Add(new DiffLine("", DiffLineType.Unchanged));
            oi++;
        }

        while (ni < n)
        {
            result.LeftLines.Add(new DiffLine("", DiffLineType.Unchanged));
            result.RightLines.Add(new DiffLine(newLines[ni], DiffLineType.Added));
            ni++;
        }

        return result;
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
}
