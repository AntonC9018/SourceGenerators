using Microsoft.CodeAnalysis;

namespace EntityOwnership.SourceGenerator;

public static class Diagnostics
{
    public static readonly DiagnosticDescriptor NoOwnerTypeInGraph = new(
        id: "EOWN001",
        title: "No owner type in graph",
        messageFormat: "The owner type {0} of {1} has not been found in the type graph. You need to make all owners explicitly implement IOwner. Also, all entities must be in the same project (for now).",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeHasItselfAsOwner = new DiagnosticDescriptor(
        id: "EOWN002",
        title: "A type has itself as owner",
        messageFormat: "The entity type {0} declared itself as the owner. This is not allowed.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeWasPartOfCycle = new DiagnosticDescriptor(
        id: "EOWN003",
        title: "A type was part of an ownership cycle",
        messageFormat: "A cycle was found in the ownership graph. The following types form a cycle: {0}.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
