﻿namespace RoslynPad.Editor;

internal static class HighlightColorExtensions
{
    public static HighlightingColor AsFrozen(this HighlightingColor color)
    {
        if (!color.IsFrozen)
        {
            color.Freeze();
        }

        return color;
    }
}
