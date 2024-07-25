﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynPad.Build;

internal interface IExecutionHost
{
    ExecutionPlatform Platform { get; set; }
    string Name { get; set; }
    string DotNetExecutable { get; set; }
    ImmutableArray<MetadataReference> MetadataReferences { get; }
    ImmutableArray<AnalyzerFileReference> Analyzers { get; }
    DocumentId? DocumentId { get; set; }

    event Action<IList<CompilationErrorResultObject>>? CompilationErrors;
    event Action<string>? Disassembled;
    event Action<ResultObject>? Dumped;
    event Action<ExceptionResultObject>? Error;
    event Action? ReadInput;
    event Action? RestoreStarted;
    event Action<RestoreResult>? RestoreCompleted;
    event Action<ProgressResultObject>? ProgressChanged;

    void ClearRestoreCache();
    Task UpdateReferencesAsync(bool alwaysRestore);
    Task SendInputAsync(string input);
    Task ExecuteAsync(string path, bool disassemble, OptimizationLevel? optimizationLevel, CancellationToken cancellationToken);
    Task TerminateAsync();
}
