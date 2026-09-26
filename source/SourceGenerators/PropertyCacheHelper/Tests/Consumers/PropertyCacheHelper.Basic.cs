#:package Anton.PropertyCacheHelper.SourceGenerator
#:property TargetFramework=net11.0
#:property PublishAot=false

using PropertyCacheHelper.Shared;

var property = HelloProps.Id.PropertyInfo;

if (property.Name != nameof(Hello.Id))
{
    throw new InvalidOperationException("Generated cached property info is incorrect.");
}

[CachePropertyInfo]
public sealed class Hello
{
    public int Id { get; set; }
}
