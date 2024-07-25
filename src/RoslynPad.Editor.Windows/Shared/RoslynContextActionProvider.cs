﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Roslyn;
using RoslynPad.Roslyn.CodeActions;
using RoslynPad.Roslyn.CodeFixes;
using RoslynPad.Roslyn.CodeRefactorings;

namespace RoslynPad.Editor;

public sealed class RoslynContextActionProvider : IContextActionProvider
{
    private static readonly ImmutableArray<string> s_excludedRefactoringProviders =
        ["ExtractInterface"];

    private readonly DocumentId _documentId;
    private readonly IRoslynHost _roslynHost;
    private readonly ICodeFixService _codeFixService;

    public RoslynContextActionProvider(DocumentId documentId, IRoslynHost roslynHost)
    {
        _documentId = documentId;
        _roslynHost = roslynHost;
        _codeFixService = _roslynHost.GetService<ICodeFixService>();
    }

    public async Task<IEnumerable<object>> GetActions(int offset, int length, CancellationToken cancellationToken)
    {
        var textSpan = new TextSpan(offset, length);
        var document = _roslynHost.GetDocument(_documentId);
        if (document == null)
        {
            return Array.Empty<object>();
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (textSpan.End >= text.Length) return Array.Empty<object>();

        var codeFixes = await _codeFixService.StreamFixesAsync(document, textSpan, cancellationToken).ToArrayAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var codeRefactorings = await _roslynHost.GetService<ICodeRefactoringService>().GetRefactoringsAsync(
            document,
            textSpan, cancellationToken).ConfigureAwait(false);

        return ((IEnumerable<object>) codeFixes.SelectMany(x => x.Fixes))
            .Concat(codeRefactorings
                .Where(x => s_excludedRefactoringProviders.All(p => !x.Provider.GetType().Name.Contains(p)))
                .SelectMany(x => x.Actions));
    }

    public ICommand? GetActionCommand(object action)
    {
        if (action is CodeAction codeAction)
        {
            return new CodeActionCommand(this, codeAction);
        }
        if (action is not CodeFix codeFix || codeFix.Action.HasCodeActions()) return null;
        return new CodeActionCommand(this, codeFix.Action);
    }

    private async Task ExecuteCodeAction(CodeAction codeAction)
    {
        var operations = await codeAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(true);
        foreach (var operation in operations)
        {
            var document = _roslynHost.GetDocument(_documentId);
            if (document != null)
            {
                operation.Apply(document.Project.Solution.Workspace,
                    CancellationToken.None);
            }
        }
    }

    private class CodeActionCommand(RoslynContextActionProvider provider, CodeAction codeAction) : ICommand
    {
        private readonly RoslynContextActionProvider _provider = provider;
        private readonly CodeAction _codeAction = codeAction;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter)
        {
            await _provider.ExecuteCodeAction(_codeAction).ConfigureAwait(true);
        }
    }
}
