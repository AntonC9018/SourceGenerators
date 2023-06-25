using System;
using System.Diagnostics;
using NetStandard;

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

    public static string Join(this ReadOnlySpan<char> str0, ReadOnlySpan<char> str1)
    {
        unsafe
        {
            var result = new string('\0', checked(str0.Length + str1.Length));
            fixed (char* resultPtr = result)
            {
                var resultSpan = new Span<char>(resultPtr, result.Length);

                str0.CopyTo(resultSpan);
                resultSpan = resultSpan.Slice(str0.Length);

                str1.CopyTo(resultSpan);
            }
            return result;
        }
    }
}
