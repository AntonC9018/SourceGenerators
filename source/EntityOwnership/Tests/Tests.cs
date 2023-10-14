using System.Collections.Generic;
using System.Threading.Tasks;
using AutoImplementedProperties.Tests;
using EntityOwnership.SourceGenerator;
using VerifyXunit;
using Xunit;

namespace EntityOwnership.Tests;

[UsesVerify]
public class Tests
{
    private readonly TestHelper<EntityOwnershipSourceGenerator> _helper = new(
        TestHelper.GetAllMetadataReferences(typeof(IOwner)));

    [Fact]
    public Task BasicTest()
    {
        return _helper.Verify("""
            using EntityOwnership;

            public sealed class Root : IOwner
            {
                public int Id { get; set; }
            }
            public sealed class Child1 : IOwnedBy<Root>
            {
                public string Id { get; set; } = null!;
                public int RootId { get; set; }
                public Root Root { get; set; } = null!;
            }
        """);
    }
}
