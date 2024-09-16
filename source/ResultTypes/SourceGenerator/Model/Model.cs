using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed record Model
{
    public required HierarchyInfo ResultHierarchy { get; init; }
    public required Accessibility ResultAccessibility { get; init; }
    public required ResultPayload? SharedPayload { get; init; }
    public required OneForEachOverloadSet<OverloadSet> OverloadsSets;

    // We need the imports, because we can't know where
    // the referenced generated result types come from.
    public required ImmutableArray<string> Imports { get; init; }

    public readonly record struct OverloadSet
    {
        public required ImmutableArray<Method> Methods { get; init; }
        public required ImmutableArray<PassAlongResult> PassAlongResults { get; init; }
    }

    public sealed record ResultPayload
    {
        public required ImmutableArray<string> ExistingFields { get; init; }
        public required HierarchyInfo Hierarchy { get; init; }
    }

    public readonly record struct PassAlongResult
    {
        public required string? ExistingFieldNameInPayload { get; init; }
        public required TypeName ResultType { get; init; }
    }

    public readonly record struct Method
    {
        public required PayloadType? Payload { get; init; }
        public required TagType? Tag { get; init; }
        public required ImmutableArray<string> AcceptedTagValues { get; init; }
    }

    public readonly record struct TypeName
    {
        public required string QualifiedName { get; init; }

        // This is used for generating field and type names.
        public required string ShortName { get; init; }

        // This is needed when the result type might not exist in the source code yet.
        // public required string? AssociatedResultTypeName { get; init; }
        // public required string? ResultTypeQualifyingPrefix { get; init; }
    }

    public sealed record PayloadType
    {
        public required TypeName Type { get; init; }

        // This is detected just by name.
        // This being true indicates that a field for this type of payload must not be added.
        // And the fact that the whole payload should be assigned.
        public required bool IsWholePayload { get; init; }
    }

    public sealed record TagType
    {
        public required TypeName Type { get; init; }
        public required TypeSyntaxReference? SupersetReference { get; set; }
        public required bool IsEnum { get; init; }
    }
}

internal sealed record Config
{
    public required TypeSyntaxReference ResultBase { get; init; }
    public required TypeSyntaxReference WellKnownTypes { get; init; }
    public required TypeSyntaxReference ResultSet { get; init; }
}

internal readonly record struct ConfigAndModel
{
    public required Model Model { get; init; }
    public required Config Config { get; init; }
}
