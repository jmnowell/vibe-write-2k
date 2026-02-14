using System.Collections.Generic;

namespace VibePlatform.Models;

public record WordLocation(int Offset, int Length);

public class MisspelledWord
{
    public string Word { get; init; } = "";
    public List<WordLocation> Locations { get; init; } = new();
    public List<string> Suggestions { get; init; } = new();
}
