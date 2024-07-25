﻿namespace RoslynPad.Editor;

public class CommonPropertyChangedArgs<T>(T oldValue, T newValue)
{
    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
