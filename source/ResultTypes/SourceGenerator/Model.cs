using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed record Model
{
    public required HierarchyInfo ResultHierarchy { get; init; }
    public required Accessibility ResultAccessibility { get; init; }
    public required OneForEachOverloadSet<Overloads> OverloadsSets { get; init; }

    // We need the imports, because we can't know where
    // the referenced generated result types come from.
    public required ImmutableArray<string> Imports { get; init; }

    public readonly record struct Overloads
    {
        public required ImmutableArray<MethodModel> Methods { get; init; }
        public required ImmutableArray<TypeSyntaxReference> PassAlongResults { get; init; }
    }

    public readonly record struct MethodModel
    {
        public required PayloadType? Payload { get; init; }
        public required TagType? Tag { get; init; }
        public required ImmutableArray<string> AcceptedTagValues { get; init; }
    }

    public readonly record struct TypeInfoPotentiallyFromResultType
    {
        public required TypeSyntaxReference? Type { get; init; }

        // This is used for generating field and type names.
        public required string ShortName { get; init; }

        // This is needed when the result type might not exist in the source code yet.
        public required string? AssociatedResultTypeName { get; init; }
        public required string? ResultTypeQualifyingPrefix { get; init; }
    }

    public sealed record PayloadType
    {
        public TypeInfoPotentiallyFromResultType Type;
    }

    public sealed record TagType
    {
        public TypeInfoPotentiallyFromResultType Type;
        public required TypeSyntaxReference? SupersetReference { get; set; }
        public required bool IsEnum { get; init; }
    }
}

