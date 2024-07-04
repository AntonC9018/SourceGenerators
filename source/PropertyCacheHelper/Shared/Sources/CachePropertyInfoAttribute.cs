using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace PropertyCacheHelper.Shared;

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class,
    Inherited = true)]
[Conditional(Constants.ConditionString)]
public sealed class CachePropertyInfoAttribute : Attribute
{
}
