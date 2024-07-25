namespace RoslynPad.Editor;

public interface IClassificationHighlightColors
{
    HighlightingColor DefaultBrush { get; }

    HighlightingColor GetBrush(string classificationTypeName);
}
