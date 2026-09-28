using System;
using Microsoft.CodeAnalysis;

namespace SourceGeneration.Testing;

/// <summary>
/// Marks source text as the span of an expected diagnostic.
/// </summary>
public readonly struct DiagnosticInterpolation : ISpanFormattable
{
    internal DiagnosticInterpolation(
        string source,
        string id,
        DiagnosticExpectation? expectation)
    {
        Source = source;
        Id = id;
        Expectation = expectation;
    }

    internal string Source { get; }

    internal string Id { get; }

    internal DiagnosticExpectation? Expectation { get; }

    public override string ToString()
    {
        return ToString(format: null, formatProvider: null);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (!string.IsNullOrEmpty(format))
        {
            throw new FormatException("Diagnostic markers do not support format strings.");
        }

        return string.Concat("{|", Id, ":", Source, "|}");
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (!format.IsEmpty)
        {
            throw new FormatException("Diagnostic markers do not support format strings.");
        }

        var length = Id.Length + Source.Length + 5;
        if (destination.Length < length)
        {
            charsWritten = 0;
            return false;
        }

        "{|".AsSpan().CopyTo(destination);
        Id.AsSpan().CopyTo(destination[2..]);
        destination[Id.Length + 2] = ':';
        Source.AsSpan().CopyTo(destination[(Id.Length + 3)..]);
        "|}".AsSpan().CopyTo(destination[(Id.Length + Source.Length + 3)..]);
        charsWritten = length;
        return true;
    }
}

/// <summary>
/// Creates typed diagnostic markers for <see cref="TestCode"/>.
/// </summary>
public static class DiagnosticMarkers
{
    public static DiagnosticInterpolation InterpolateDiagnostic(
        string source,
        DiagnosticDescriptor rule)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rule);

        return new DiagnosticInterpolation(source, rule.Id, expectation: null);
    }

    public static DiagnosticInterpolation InterpolateDiagnostic(
        string source,
        DiagnosticExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expectation);

        return new DiagnosticInterpolation(source, expectation.Id, expectation);
    }
}
