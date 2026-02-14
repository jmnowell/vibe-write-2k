using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VibePlatform.Services;

public class SpellCheckService
{
    private HashSet<string>? _dictionary;
    private HashSet<string>? _userDictionary;
    private string? _userDictionaryPath;

    public async Task EnsureLoadedAsync()
    {
        if (_dictionary != null) return;

        _dictionary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("english-words.txt"));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var word = line.Trim();
                if (word.Length > 0)
                    _dictionary.Add(word);
            }
        }

        // Load user dictionary
        _userDictionaryPath = GetUserDictionaryPath();
        _userDictionary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(_userDictionaryPath))
        {
            var lines = await File.ReadAllLinesAsync(_userDictionaryPath);
            foreach (var line in lines)
            {
                var word = line.Trim();
                if (word.Length > 0)
                    _userDictionary.Add(word);
            }
        }
    }

    public bool IsCorrect(string word)
    {
        if (_dictionary == null) throw new InvalidOperationException("Dictionary not loaded. Call EnsureLoadedAsync first.");
        return _dictionary.Contains(word) || _userDictionary!.Contains(word);
    }

    public List<string> GetSuggestions(string word)
    {
        if (_dictionary == null) throw new InvalidOperationException("Dictionary not loaded.");

        var wordLower = word.ToLowerInvariant();
        int wordLen = wordLower.Length;

        var candidates = _dictionary
            .Where(w => Math.Abs(w.Length - wordLen) <= 2)
            .Select(w => (Word: w, Distance: LevenshteinDistance(wordLower, w.ToLowerInvariant())))
            .Where(x => x.Distance > 0 && x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Word)
            .Take(8)
            .Select(x => x.Word)
            .ToList();

        return candidates;
    }

    public async Task AddToUserDictionaryAsync(string word)
    {
        if (_userDictionary == null || _userDictionaryPath == null)
            throw new InvalidOperationException("Dictionary not loaded.");

        if (_userDictionary.Add(word))
        {
            var dir = Path.GetDirectoryName(_userDictionaryPath)!;
            Directory.CreateDirectory(dir);
            await File.AppendAllTextAsync(_userDictionaryPath, word + Environment.NewLine);
        }
    }

    private static string GetUserDictionaryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Vibe", "user-dictionary.txt");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".vibe", "user-dictionary.txt");
        }
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }
}
