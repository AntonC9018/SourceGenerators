using System.Threading.Tasks;
using PropertyCacheHelper.Shared;
using PropertyCacheHelper.SourceGenerator;
using SourceGeneration.Testing;
using Xunit;

namespace PropertyCacheHelper.Tests;

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

    [Fact]
    public Task ExplicitPropertyTest()
    {
        return _helper.Verify("""
            using PropertyCacheHelper.Shared;

            [CachePropertyInfo]
            public sealed class Hello
            {
                [CachePropertyInfo]
                public int Id { get; set; }
                public string? Name { get; set; }
            }
        """);
    }


    [Fact]
    public Task InheritancePropertyTest()
    {
        return _helper.Verify("""
            using PropertyCacheHelper.Shared;

            [CachePropertyInfo]
            public abstract class Base
            {
                [CachePropertyInfo]
                public int Id { get; set; }
                public string? Name { get; set; }
            }

            [CachePropertyInfo]
            public sealed class Derived : Base
            {
                public int IdDerived { get; set; }
            }

            [CachePropertyInfo]
            public sealed class OtherDerived : Base
            {
                [CachePropertyInfo]
                public int IdDerived { get; set; }
                public string? Ignored { get; set; }
            }

            public sealed class NotGeneratedDerived : Base
            {
                [CachePropertyInfo]
                public int IdDerived { get; set; }
                public string? Ignored { get; set; }
            }
        """);
    }
}
