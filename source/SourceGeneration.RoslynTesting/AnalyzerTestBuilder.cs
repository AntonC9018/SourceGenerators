using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SourceGeneration.Testing;

public static class AnalyzerTestBuilder
{
    public static AnalyzerTestBuilder<TAnalyzer, TVerifier> For<TAnalyzer, TVerifier>()
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TVerifier : IVerifier, new()
    {
        return new AnalyzerTestBuilder<TAnalyzer, TVerifier>();
    }
}

public sealed class AnalyzerTestBuilder<TAnalyzer, TVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TVerifier : IVerifier, new()
{
    private readonly List<DiagnosticResult> additionalDiagnostics = [];
    private TestCode? source;

    internal AnalyzerTestBuilder()
    {
    }

    public AnalyzerTestBuilder<TAnalyzer, TVerifier> WithSource(TestCode value)
    {
        ArgumentNullException.ThrowIfNull(value);

        source = value;
        return this;
    }

    public AnalyzerTestBuilder<TAnalyzer, TVerifier> ExpectDiagnostic(DiagnosticResult expected)
    {
        additionalDiagnostics.Add(expected);
        return this;
    }

    public async Task RunAsync()
    {
        if (source is null)
        {
            throw new InvalidOperationException("Configure source code before running the analyzer test.");
        }

        var test = new CSharpAnalyzerTest<TAnalyzer, TVerifier>
        {
            TestCode = source.Source,
        };

        foreach (var expected in source.ExpectedDiagnostics)
        {
            test.ExpectedDiagnostics.Add(expected);
        }

        foreach (var expected in additionalDiagnostics)
        {
            test.ExpectedDiagnostics.Add(expected);
        }

        await test.RunAsync();
    }
}
