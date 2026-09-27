using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Probe;

public sealed class ProbeAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<ProbeAnalyzer, DefaultVerifier>
        {
            TestCode = "class {|PROBE001:BadName|} { }",
        };

        await test.RunAsync();
    }
}
