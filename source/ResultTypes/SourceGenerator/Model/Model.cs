using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed record Model
{
    public required HierarchyInfo ResultHierarchy { get; init; }

    // If the payload type is the same in all method calls,
    public required HierarchyInfo? SharedPayloadHierarchy { get; init; }
    public required Accessibility ResultAccessibility { get; init; }
    public required OneForEachOverloadSet<Overloads> OverloadsSets { get; init; }

    // We need the imports, because we can't know where
    // the referenced generated result types come from.
    public required ImmutableArray<string> Imports { get; init; }

    public readonly record struct Overloads
    {
        public required ImmutableArray<Method> Methods { get; init; }
        public required ImmutableArray<PassAlongResult> PassAlongResults { get; init; }
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
        public TypeName Type;
    }

    public sealed record TagType
    {
        public TypeName Type;
        public required TypeSyntaxReference? SupersetReference { get; set; }
        public required bool IsEnum { get; init; }
    }
}

