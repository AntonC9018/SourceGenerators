using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace PropertyCacheHelper.Shared;

/// <summary>
/// Apply to property to say that the PropertyInfo for it should be cached in a static field.
/// Must also apply to the class the property is in.
/// If only applied to the class, a static PropertyInfo field is generated for each property.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Property,
    Inherited = true)]
[Conditional(Constants.ConditionString)]
public sealed class CachePropertyInfoAttribute : Attribute
{
}
