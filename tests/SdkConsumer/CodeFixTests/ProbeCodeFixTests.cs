using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Probe;

public sealed class ProbeCodeFixTests
{
    [Fact]
    public async Task RenamesClass()
    {
        var test = new CSharpCodeFixTest<ProbeAnalyzer, ProbeCodeFix, DefaultVerifier>
        {
            TestCode = "class {|PROBE001:BadName|} { }",
            FixedCode = "class GoodName\n{ }",
        };

        await test.RunAsync();
    }
}
