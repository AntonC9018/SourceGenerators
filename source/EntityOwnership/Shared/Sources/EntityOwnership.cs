using ConsumerShared;

namespace EntityOwnership;

/// <summary>
/// </summary>
[AlwaysAutoImplemented]
public interface IOwned<T>
{
}

/// <summary>
/// Indicates the root of the ownership hierarchy.
/// </summary>
[AlwaysAutoImplemented]
public interface IOwner
{
}
