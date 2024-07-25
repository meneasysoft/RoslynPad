// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Reactive.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Roslyn;
using System.Reactive.Subjects;

namespace RoslynPad.Editor;

internal sealed class RoslynSemanticHighlighter : IHighlighter
{
    private const int CacheSize = 512;
    private const int DelayInMs = 100;
    private readonly TextView _textView;
    private readonly IDocument _document;
    private readonly DocumentId _documentId;
    private readonly IRoslynHost _roslynHost;
    private readonly IClassificationHighlightColors _highlightColors;
    private readonly List<CachedLine>? _cachedLines;
    private readonly Subject<FrozenLine> _subject;
    private readonly List<(FrozenLine line, List<HighlightedSection> sections)> _changes;
    private readonly SynchronizationContext? _syncContext;

    private volatile bool _inHighlightingGroup;
    private int? _updatedLine;

    public RoslynSemanticHighlighter(TextView textView, IDocument document, DocumentId documentId, IRoslynHost roslynHost, IClassificationHighlightColors highlightColors)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _documentId = documentId;
        _roslynHost = roslynHost;
        _highlightColors = highlightColors;
        _subject = new Subject<FrozenLine>();
        _subject.GroupBy(c => c.LineNumber).Subscribe(SubscribeToLineGroup);

        if (document is TextDocument)
        {
            // Use the cache only for the live AvalonEdit document
            // Highlighting in read-only documents (e.g. search results) does
            // not need the cache as it does not need to highlight the same line multiple times
            _cachedLines = [];
        }

        _changes = [];
        _syncContext = SynchronizationContext.Current;
    }

    public void Dispose()
    {
        _subject.Dispose();
    }

    public event HighlightingStateChangedEventHandler? HighlightingStateChanged;

    private void UpdateHighlightingSections(FrozenLine line, List<HighlightedSection> sections)
    {
        if (_inHighlightingGroup && line.LineNumber == _updatedLine)
        {
            lock (_changes)
            {
                _changes.Add((line, sections));
            }

            return;
        }

        _syncContext?.Post(o => UpdateHighlightingSectionsNoCheck(line, sections), null);
    }

    private void UpdateHighlightingSectionsNoCheck(FrozenLine line, List<HighlightedSection> sections)
    {
        if (!IsCurrentLine(line))
        {
            return;
        }

        var lineNumber = line.LineNumber;

        line.HighlightedLine.Sections.Clear();
        foreach (var section in sections)
        {
            line.HighlightedLine.Sections.Add(section);
        }

        if (_textView.GetVisualLine(line.LineNumber) != null)
        {
            HighlightingStateChanged?.Invoke(lineNumber, lineNumber);
        }
    }

    private bool IsCurrentLine(FrozenLine line)
    {
        return !line.IsDeleted &&
               line.HighlightedLine.Document.Version.CompareAge(_document.Version) == 0 &&
               line.LineNumber <= _document.LineCount &&
               _document.GetLineByNumber(line.LineNumber) is var currentLine &&
               currentLine?.Length == line.Length;
    }

    IDocument IHighlighter.Document => _document;

    IEnumerable<HighlightingColor>? IHighlighter.GetColorStack(int lineNumber) => null;

    public void UpdateHighlightingState(int lineNumber)
    {
        if (_inHighlightingGroup && _updatedLine == null)
        {
            _updatedLine = lineNumber;
        }
    }

    public HighlightedLine HighlightLine(int lineNumber)
    {
        var documentLine = _document.GetLineByNumber(lineNumber);
        var newVersion = _document.Version;
        CachedLine? cachedLine = null;
        if (_cachedLines != null)
        {
            for (var i = 0; i < _cachedLines.Count; i++)
            {
                var line = _cachedLines[i];
                if (line.DocumentLine != documentLine) continue;
                if (newVersion == null || !newVersion.BelongsToSameDocumentAs(line.OldVersion))
                {
                    // cannot list changes from old to new: we can't update the cache, so we'll remove it
                    _cachedLines.RemoveAt(i);
                }
                else
                {
                    cachedLine = line;
                }
            }

            if (cachedLine != null && cachedLine.IsValid && newVersion?.CompareAge(cachedLine.OldVersion) == 0 &&
                cachedLine.DocumentLine.Length == documentLine.Length)
            {
                // the file hasn't changed since the cache was created, so just reuse the old highlighted line
                return cachedLine.HighlightedLine;
            }
        }

        var wasInHighlightingGroup = _inHighlightingGroup;
        if (!_inHighlightingGroup)
        {
            BeginHighlighting();
        }
        try
        {
            return DoHighlightLine(documentLine, cachedLine);
        }
        finally
        {
            if (!wasInHighlightingGroup)
            {
                EndHighlighting();
            }
        }
    }

    private HighlightedLine DoHighlightLine(IDocumentLine documentLine, CachedLine? previousCachedLine)
    {
        var line = new HighlightedLine(_document, documentLine);

        // If we have previous cached data, use it in the meantime since our request is asynchronous
        if (previousCachedLine != null && previousCachedLine.HighlightedLine is var previousHighlight &&
            previousHighlight.Sections.Count > 0)
        {
            var offsetShift = documentLine.Offset - previousCachedLine.Offset;

            foreach (var section in previousHighlight.Sections)
            {
                var offset = section.Offset + offsetShift;

                // stop if section is outside the line
                if (offset < documentLine.Offset)
                    continue;

                if (offset >= documentLine.EndOffset)
                    break;

                // clamp section to not be longer than line
                int length = Math.Min(section.Length, documentLine.EndOffset - offset);
                line.Sections.Add(new HighlightedSection
                {
                    Color = section.Color,
                    Offset = offset,
                    Length = length,
                });
            }
        }

        // since we don't want to block the UI thread
        // we'll enqueue the request and process it asynchronously
        _subject.OnNext(new FrozenLine(line));

        CacheLine(line);
        return line;
    }

    private void SubscribeToLineGroup(IObservable<FrozenLine> observable)
    {
        var connectible = observable.Throttle(TimeSpan.FromMilliseconds(DelayInMs))
            .SelectMany(SubscribeToLine)
            .Replay();
        connectible.Connect();
    }

    private async Task<object?> SubscribeToLine(FrozenLine line)
    {
        var document = _roslynHost.GetDocument(_documentId);
        if (document == null)
            return null;

        IEnumerable<ClassifiedSpan> spans;
        try
        {
            spans = await GetClassifiedSpansAsync(document, line).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return null;
        }

        // rebuild sections
        var sections = new List<HighlightedSection>();
        foreach (var classifiedSpan in spans)
        {
            var textSpan = AdjustTextSpan(classifiedSpan, line);
            if (textSpan == null)
            {
                continue;
            }

            sections.Add(new HighlightedSection
            {
                Color = _highlightColors.GetBrush(classifiedSpan.ClassificationType),
                Offset = textSpan.Value.Start,
                Length = textSpan.Value.Length
            });
        }

        // post update on UI thread
        UpdateHighlightingSections(line, sections);
        return null;
    }

    private static TextSpan? AdjustTextSpan(ClassifiedSpan classifiedSpan, FrozenLine line)
    {
        if (classifiedSpan.TextSpan.Start > line.EndOffset)
        {
            return null;
        }

        var result = TextSpan.FromBounds(
            Math.Max(classifiedSpan.TextSpan.Start, line.Offset),
            Math.Min(classifiedSpan.TextSpan.End, line.EndOffset));

        return result;
    }

    private void CacheLine(HighlightedLine line)
    {
        if (_cachedLines != null && _document.Version != null)
        {
            _cachedLines.Add(new CachedLine(line, _document.Version));

            // Clean cache once it gets too big
            if (_cachedLines.Count > CacheSize)
            {
                _cachedLines.RemoveRange(0, CacheSize / 2);
            }
        }
    }

    private async Task<IEnumerable<ClassifiedSpan>> GetClassifiedSpansAsync(Document document, FrozenLine line)
    {
        if (!line.IsDeleted)
        {
            var text = await document.GetTextAsync().ConfigureAwait(false);
            if (text.Length >= line.Offset + line.TotalLength)
            {
                return await Classifier.GetClassifiedSpansAsync(document,
                        new TextSpan(line.Offset, line.TotalLength), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return Array.Empty<ClassifiedSpan>();
    }

    HighlightingColor IHighlighter.DefaultTextColor => _highlightColors.DefaultBrush;

    public void BeginHighlighting()
    {
        if (_inHighlightingGroup)
            throw new InvalidOperationException();
        _inHighlightingGroup = true;
    }

    public void EndHighlighting()
    {
        _inHighlightingGroup = false;
        _updatedLine = null;

        lock (_changes)
        {
            foreach (var change in _changes)
            {
                UpdateHighlightingSectionsNoCheck(change.line, change.sections);
            }

            _changes.Clear();
        }
    }

    public HighlightingColor? GetNamedColor(string name) => null;

    // If a line gets edited and we need to display it while no parse information is ready for the
    // changed file, the line would flicker (semantic highlightings disappear temporarily).
    // We avoid this issue by storing the semantic highlightings and updating them on document changes
    // (using anchor movement)
    private class CachedLine
    {
        public readonly HighlightedLine HighlightedLine;
        public readonly ITextSourceVersion OldVersion;
        public readonly int Offset;

        /// <summary>
        /// Gets whether the cache line is valid (no document changes since it was created).
        /// This field gets set to false when Update() is called.
        /// </summary>
        public readonly bool IsValid;

        public IDocumentLine DocumentLine => HighlightedLine.DocumentLine;

        public CachedLine(HighlightedLine highlightedLine, ITextSourceVersion fileVersion)
        {
            HighlightedLine = highlightedLine ?? throw new ArgumentNullException(nameof(highlightedLine));
            OldVersion = fileVersion ?? throw new ArgumentNullException(nameof(fileVersion));
            IsValid = true;
            Offset = HighlightedLine.DocumentLine.Offset;
        }
    }

    private class FrozenLine
    {
        public FrozenLine(HighlightedLine line)
        {
            HighlightedLine = line;
            
            var documentLine = line.DocumentLine;
            TotalLength = documentLine.TotalLength;
            DelimiterLength = documentLine.DelimiterLength;
            LineNumber = documentLine.LineNumber;
            IsDeleted = documentLine.IsDeleted;
            Offset = documentLine.Offset;
            Length = documentLine.Length;
            EndOffset = documentLine.EndOffset;
        }

        public HighlightedLine HighlightedLine { get; }
        
        public int TotalLength { get; }
        public int DelimiterLength { get; }
        public int LineNumber { get; }
        public bool IsDeleted { get; }
        public int Offset { get; }
        public int Length { get; }
        public int EndOffset { get; }
    }
}
