using System;

namespace SourceGeneration.Helpers;

public static class SpanHelper
{
    public static TBase[] ToArrayT<T, TBase>(ReadOnlySpan<T> span)
        where T : TBase
    {
        var result = new TBase[span.Length];
        for (int i = 0; i < span.Length; i++)
            result[i] = span[i];
        return result;
    }
    
    public static TBase[] ToArrayT<T, TBase>(Span<T> span)
        where T : TBase
    {
        var result = new TBase[span.Length];
        for (int i = 0; i < span.Length; i++)
            result[i] = span[i];
        return result;
    }
}