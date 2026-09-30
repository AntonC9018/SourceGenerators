using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Probe;

public sealed class ExplicitPackageTests
{
    [Fact]
    public void IncludesSharedProjectAndExplicitPackages()
    {
        var identifier = SyntaxFactory.IdentifierName(ProbeNames.GoodName);
        Assert.Equal("GoodName", identifier.Identifier.ValueText);
    }
}
