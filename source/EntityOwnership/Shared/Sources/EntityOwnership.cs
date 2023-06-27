using System;
using System.Linq;
using ConsumerShared;

namespace EntityOwnership;

/// <summary>
/// </summary>
[AlwaysAutoImplemented]
public interface IOwnedBy<T>
{
}

/// <summary>
/// Indicates the root of the ownership hierarchy.
/// </summary>
[AlwaysAutoImplemented]
public interface IOwner
{
}

public sealed class WrongIdTypeException : Exception
{
    public Type Expected { get; }
    public Type Actual { get; }

    public WrongIdTypeException(Type expected, Type actual)
    {
        Expected = expected;
        Actual = actual;
    }

    public override string Message => $"Expected id type {Expected.Name} but got {Actual.Name}.";
}
