#:package Anton.EntityOwnership.SourceGenerator
#:property TargetFramework=net11.0
#:property PublishAot=false
#:property RootNamespace=

using EntityOwnership;
using System.Linq;

var children = new[]
{
    new Child { Id = "child", RootId = 1, Root = new Root { Id = 1 } }
}.AsQueryable();

var result = children.RootOwnerFilter(1).Single();

if (result.RootId != 1)
{
    throw new InvalidOperationException("Generated ownership filter did not run correctly.");
}

public sealed class Root : IOwner
{
    public int Id { get; set; }
}

public sealed class Child : IOwnedBy<Root>
{
    public string Id { get; set; } = "";
    public int RootId { get; set; }
    public Root Root { get; set; } = null!;
}
