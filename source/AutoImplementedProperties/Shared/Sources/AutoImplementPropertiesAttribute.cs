using System;
using System.Diagnostics;
using SourceGeneration.Shared;

namespace AutoImplementedProperties.Attributes;

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class,
    Inherited = true)]
[Conditional(Constants.ConditionString)]
public sealed class AutoImplementPropertiesAttribute : Attribute
{
}
