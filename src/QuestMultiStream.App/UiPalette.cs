using System.Windows.Media;

namespace QuestMultiStream.App;

internal static class UiPalette
{
    public static Brush Accent { get; } = Create("#00E8FF");

    public static Brush Success { get; } = Create("#00FF99");

    public static Brush Warning { get; } = Create("#FFC73A");

    public static Brush Failure { get; } = Create("#FF4D98");

    public static Brush Muted { get; } = Create("#9FB0C4");

    public static Brush Border { get; } = Create("#1D4B70");

    public static Brush Create(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
