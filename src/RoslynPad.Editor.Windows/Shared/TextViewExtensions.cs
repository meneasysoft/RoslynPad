namespace RoslynPad.Editor;

internal static class TextViewExtensions
{
    public static Point GetPosition(this TextView textView, int line, int column)
    {
        var visualPosition = textView.GetVisualPosition(
            new TextViewPosition(line, column), VisualYPosition.LineBottom) - textView.ScrollOffset;
        return visualPosition;
    }
}
