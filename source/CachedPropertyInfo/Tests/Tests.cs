using System.Threading.Tasks;
using AutoImplementedProperties.Tests;
using CachedPropertyInfo.Shared;
using CachedPropertyInfo.SourceGenerator;
using VerifyXunit;
using Xunit;

namespace CachedPropertyInfo.Tests;

[UsesVerify]
public class Tests
{
    private readonly TestHelper<CachedPropertyInfoGenerator> _helper = new(
        TestHelper.GetAllMetadataReferences(typeof(CachedPropertyInfoAttribute)));

    [Fact]
    public Task BasicTest()
    {
        return _helper.Verify("""
            using CachedPropertyInfo.Shared;

            [CachedPropertyInfo]
            public sealed class Hello
            {
                public int Id { get; set; }
                public string? Name { get; set; }
            }
        """);
    }
}
