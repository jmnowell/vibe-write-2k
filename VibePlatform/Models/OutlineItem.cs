using Avalonia;

namespace VibePlatform.Models;

public class OutlineItem
{
    public string Title { get; set; } = "";
    public int Level { get; set; }
    public int LineNumber { get; set; }

    public Thickness IndentMargin => Level switch
    {
        1 => new Thickness(12, 0, 0, 0),
        2 => new Thickness(28, 0, 0, 0),
        3 => new Thickness(44, 0, 0, 0),
        _ => new Thickness(12, 0, 0, 0)
    };
}
