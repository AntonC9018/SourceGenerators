using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace ResultTypes.Shared;

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[Conditional(Constants.ConditionString)]
public sealed class GenerateResultTypeAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Conditional(Constants.ConditionString)]
public sealed class TagAttribute : Attribute
{
}

/// <summary>
/// Must have a <c>Create(value)</c> static method that creates a value,
/// adjusted to the global error set.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
[Conditional(Constants.ConditionString)]
public sealed class ResultBaseAttribute(Type t) : Attribute
{
    public Type Type { get; set; } = t;
}

/// <summary>
/// Must have an Ok and a GenericFailure members.
/// These will be used when Ok and Failure is called without arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
[Conditional(Constants.ConditionString)]
public sealed class WellKnownResultType(Type t) : Attribute
{
    public Type Type { get; set; } = t;
}


[AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
[Conditional(Constants.ConditionString)]
public class SubsetAttribute(Type t) : Attribute
{
    public Type Type { get; set; } = t;
}

public sealed class SubsetAttribute<T>() : SubsetAttribute(typeof(T))
{
}
