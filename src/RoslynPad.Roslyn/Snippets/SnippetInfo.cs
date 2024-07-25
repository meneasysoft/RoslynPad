namespace RoslynPad.Roslyn.Snippets;

public sealed class SnippetInfo(string shortcut, string title, string description)
{
    public string Shortcut { get; } = shortcut;

    public string Title { get; } = title;

    public string Description { get; } = description;
}