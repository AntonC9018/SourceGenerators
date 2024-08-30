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
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
[Conditional(Constants.ConditionString)]
public sealed class ResultBaseAttribute(Type t) : Attribute
{
    public Type Type { get; set; } = t;
}
