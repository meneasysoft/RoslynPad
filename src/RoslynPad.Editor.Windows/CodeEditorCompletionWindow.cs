﻿namespace RoslynPad.Editor;

partial class CodeEditorCompletionWindow
{
    static CodeEditorCompletionWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CodeEditorCompletionWindow), new FrameworkPropertyMetadata(typeof(CodeEditorCompletionWindow)));
    }

    partial void Initialize()
    {
        CompletionList.ListBox.BorderThickness = new Thickness(0);
        CompletionList.ListBox.PreviewMouseDown +=
            (o, e) => _isSoftSelectionActive = false;
    }
}
