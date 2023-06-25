using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace AutoConstructor.Attributes;

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
[Conditional(Constants.ConditionString)]
public sealed class AutoConstructorAttribute : Attribute
{
}
