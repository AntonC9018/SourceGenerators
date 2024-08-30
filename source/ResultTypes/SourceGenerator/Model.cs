using System.Collections.Immutable;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed record Model
{
    public required HierarchyInfo Hierarchy { get; init; }
    public required string GeneratedTypeName { get; init; }
    public required ImmutableArray<MethodModel> OkMethods { get; init; }
    public required ImmutableArray<MethodModel> FailureMethods { get; init; }

    public readonly record struct MethodModel
    {
        public required ResultSet ResultSet { get; init; }
        public required TypeSyntaxReference? ParamsModelType { get; init; }
        public required ImmutableArray<Property> Properties { get; init; }
    }

    public readonly record struct Property
    {
        public required TypeSyntaxReference Type { get; init; }
        public required string Name { get; init; }
    }

    public readonly record struct ResultSet
    {
        public required TypeSyntaxReference Type { get; init; }
        public required ImmutableArray<string> ProvidedConcreteValues { get; init; }
    }
}

