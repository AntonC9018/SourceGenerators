using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SourceGeneration.Testing;

public static class CodeFixTestBuilder
{
    public static CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier> For<
        TAnalyzer,
        TCodeFix,
        TVerifier>()
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
        where TVerifier : IVerifier, new()
    {
        return new CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier>();
    }
}

public sealed class CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
    where TVerifier : IVerifier, new()
{
    private readonly List<DiagnosticResult> additionalDiagnostics = [];
    private TestCode? source;
    private string? fixedCode;

    internal CodeFixTestBuilder()
    {
    }

    public CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier> WithSource(TestCode value)
    {
        ArgumentNullException.ThrowIfNull(value);

        source = value;
        return this;
    }

    public CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier> WithFixedCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        fixedCode = value;
        return this;
    }

    public CodeFixTestBuilder<TAnalyzer, TCodeFix, TVerifier> ExpectDiagnostic(
        DiagnosticResult expected)
    {
        additionalDiagnostics.Add(expected);
        return this;
    }

    public async Task RunAsync()
    {
        if (source is null)
        {
            throw new InvalidOperationException("Configure source code before running the code fix test.");
        }

        if (fixedCode is null)
        {
            throw new InvalidOperationException("Configure fixed code before running the code fix test.");
        }

        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, TVerifier>
        {
            TestCode = source.Source,
            FixedCode = fixedCode,
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
