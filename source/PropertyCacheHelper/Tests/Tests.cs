using System.Threading.Tasks;
using AutoImplementedProperties.Tests;
using PropertyCacheHelper.Shared;
using PropertyCacheHelper.SourceGenerator;
using VerifyXunit;
using Xunit;

namespace PropertyCacheHelper.Tests;

[UsesVerify]
public class Tests
{
    private readonly TestHelper<CachedPropertyInfoGenerator> _helper = new(
        TestHelper.GetAllMetadataReferences(typeof(CachePropertyInfoAttribute)));

    [Fact]
    public Task BasicTest()
    {
        return _helper.Verify("""
            using PropertyCacheHelper.Shared;

            [CachePropertyInfo]
            public sealed class Hello
            {
                public int Id { get; set; }
                public string? Name { get; set; }
            }
        """);
    }
}
