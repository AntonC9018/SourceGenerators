using System;

namespace ConsumerShared;

/// <summary>
/// Indicates that all properties of the interface will be auto-implemented,
/// even without the auto-implemented property source generator.
/// This implies that the source generator that processes the interface
/// will generate the corresponding code on its own.
/// This also implies that the AutoImplementedProperties source generator
/// will completely ignore it.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class AlwaysAutoImplemented : System.Attribute
{

}
