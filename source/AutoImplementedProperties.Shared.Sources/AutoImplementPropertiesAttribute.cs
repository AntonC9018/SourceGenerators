using System;
using System.Diagnostics;

namespace AutoImplementedProperties.Attributes;

/// <summary>
/// </summary>
public static class Constants
{
    /// <summary>
    /// </summary>
    public const string ConditionString = "SOURCE_GENERATOR";
}

/// <summary>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class,
    Inherited = true)]
[Conditional(Constants.ConditionString)]
public sealed class AutoImplementPropertiesAttribute : Attribute
{
}