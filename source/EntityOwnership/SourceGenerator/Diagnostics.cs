namespace EntityOwnership.SourceGenerator;

public static class Diagnostics
{
#if false
    public static readonly DiagnosticDescriptor NoOwnerTypeInGraph = new(
        id: "ENTOWN001",
        title: "No owner type in graph",
        messageFormat: "The owner type {0} of {1} has not been found in the type graph. You need to make all owners explicitly implement IOwner.",
        category: "EntityOwnership",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The owner types have to be explicitly annotated.");
#endif
}
