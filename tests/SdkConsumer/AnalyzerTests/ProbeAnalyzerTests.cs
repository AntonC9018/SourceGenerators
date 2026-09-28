using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace Probe;

public sealed class ProbeAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic()
    {
        var caseNumber = 1;
        var source = TestCode.Create(
            $"// Case {caseNumber}\nclass {InterpolateDiagnostic("BadName", ProbeAnalyzer.Rule)} {{ }}");

        await AnalyzerTestBuilder
            .For<ProbeAnalyzer, DefaultVerifier>()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task ChecksExplicitSeverity()
    {
        var expected = new DiagnosticExpectation(
            ProbeAnalyzer.Rule.Id,
            DiagnosticSeverity.Warning);
        var source = TestCode.Create(
            $"class {InterpolateDiagnostic("BadName", expected)} {{ }}");

        await AnalyzerTestBuilder
            .For<ProbeAnalyzer, DefaultVerifier>()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task AcceptsPlainSource()
    {
        var source = TestCode.Create("class GoodName { }");

        await AnalyzerTestBuilder
            .For<ProbeAnalyzer, DefaultVerifier>()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task CombinesRuleAndSeverityMarkers()
    {
        var expected = new DiagnosticExpectation(
            ProbeAnalyzer.Rule.Id,
            DiagnosticSeverity.Warning);
        var source = TestCode.Create($$"""
            namespace A { class {{InterpolateDiagnostic("BadName", ProbeAnalyzer.Rule)}} { } }
            namespace B { class {{InterpolateDiagnostic("BadName", expected)}} { } }
            """);

        await AnalyzerTestBuilder
            .For<ProbeAnalyzer, DefaultVerifier>()
            .WithSource(source)
            .RunAsync();
    }

    [Fact]
    public async Task PassesThroughSeparateDiagnosticExpectation()
    {
        var source = TestCode.Create("class BadName { }");
        var expected = new DiagnosticResult(
            ProbeAnalyzer.Rule.Id,
            DiagnosticSeverity.Warning).WithSpan(1, 7, 1, 14);

        await AnalyzerTestBuilder
            .For<ProbeAnalyzer, DefaultVerifier>()
            .WithSource(source)
            .ExpectDiagnostic(expected)
            .RunAsync();
    }
}
