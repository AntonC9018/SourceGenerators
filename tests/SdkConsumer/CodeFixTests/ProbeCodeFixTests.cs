using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace Probe;

public sealed class ProbeCodeFixTests
{
    [Fact]
    public async Task RenamesClass()
    {
        var source = TestCode.Create(
            $"class {InterpolateDiagnostic("BadName", ProbeAnalyzer.Rule)} {{ }}");

        await CodeFixTestBuilder
            .For<ProbeAnalyzer, ProbeCodeFix, DefaultVerifier>()
            .WithSource(source)
            .WithFixedCode("class GoodName\n{ }")
            .RunAsync();
    }
}
