using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Testing;

namespace SourceGeneration.Testing;

/// <summary>
/// Source code and the diagnostics declared by its interpolated markers.
/// </summary>
public sealed class TestCode
{
    private readonly DiagnosticResult[] expectedDiagnostics;

    private TestCode(string source, DiagnosticResult[] expectedDiagnostics)
    {
        Source = source;
        this.expectedDiagnostics = expectedDiagnostics;
    }

    public string Source { get; }

    internal IReadOnlyList<DiagnosticResult> ExpectedDiagnostics => expectedDiagnostics;

    public static TestCode Create(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new TestCode(source, []);
    }

    public static TestCode Create(TestCodeHandler handler)
    {
        return handler.Build();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Source;
    }

    [InterpolatedStringHandler]
    public ref struct TestCodeHandler
    {
        private DefaultInterpolatedStringHandler builder;
        private readonly List<DiagnosticResult> expectedDiagnostics;

        public TestCodeHandler(int literalLength, int formattedCount)
        {
            builder = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
            expectedDiagnostics = new List<DiagnosticResult>(formattedCount);
        }

        public void AppendLiteral(string value)
        {
            builder.AppendLiteral(value);
        }

        public void AppendFormatted(DiagnosticInterpolation marker)
        {
            if (marker.Expectation is null)
            {
                builder.AppendFormatted(marker);
                return;
            }

            var location = expectedDiagnostics.Count;
            builder.AppendLiteral("{|#");
            builder.AppendFormatted(location);
            builder.AppendLiteral(":");
            builder.AppendLiteral(marker.Source);
            builder.AppendLiteral("|}");

            var expected = new DiagnosticResult(
                marker.Expectation.Id,
                marker.Expectation.Severity);
            expectedDiagnostics.Add(expected.WithLocation(location));
        }

        public void AppendFormatted(DiagnosticInterpolation marker, int alignment)
        {
            throw new FormatException("Diagnostic markers do not support alignment.");
        }

        public void AppendFormatted(DiagnosticInterpolation marker, string? format)
        {
            throw new FormatException("Diagnostic markers do not support format strings.");
        }

        public void AppendFormatted(
            DiagnosticInterpolation marker,
            int alignment,
            string? format)
        {
            throw new FormatException("Diagnostic markers do not support alignment or format strings.");
        }

        public void AppendFormatted<T>(T value)
        {
            builder.AppendFormatted(value);
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            builder.AppendFormatted(value, format);
        }

        public void AppendFormatted<T>(T value, int alignment)
        {
            builder.AppendFormatted(value, alignment);
        }

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            builder.AppendFormatted(value, alignment, format);
        }

        public void AppendFormatted(ReadOnlySpan<char> value)
        {
            builder.AppendFormatted(value);
        }

        internal TestCode Build()
        {
            var source = builder.ToStringAndClear();
            return new TestCode(source, expectedDiagnostics.ToArray());
        }
    }
}
