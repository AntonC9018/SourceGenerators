using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace CachedPropertyInfo.Shared;

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class,
    Inherited = true)]
[Conditional(Constants.ConditionString)]
public sealed class CachedPropertyInfoAttribute : Attribute
{
}
