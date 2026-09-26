#:package Anton.AutoConstructor.SourceGenerator
#:property TargetFramework=net11.0
#:property PublishAot=false

using AutoConstructor.Attributes;

var dependency = new Dependency();
var service = new Service(dependency);

if (!ReferenceEquals(dependency, service.Dependency))
{
    throw new InvalidOperationException("Generated constructor did not assign the dependency.");
}

public sealed class Dependency;

[AutoConstructor]
public sealed partial class Service
{
    public readonly Dependency Dependency;
}
