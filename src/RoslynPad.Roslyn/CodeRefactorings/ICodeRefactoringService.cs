﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPad.Roslyn.CodeRefactorings;

public interface ICodeRefactoringService
{
    Task<bool> HasRefactoringsAsync(Document document, TextSpan textSpan, CancellationToken cancellationToken);

    Task<IEnumerable<CodeRefactoring>> GetRefactoringsAsync(Document document, TextSpan textSpan, CancellationToken cancellationToken);
}