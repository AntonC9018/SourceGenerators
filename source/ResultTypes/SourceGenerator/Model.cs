using System.Collections.Immutable;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed record Model
{
    public required HierarchyInfo ResultHierarchy { get; init; }
    public required Overloads OkOverloads { get; init; }
    public required Overloads FailureMethods { get; init; }

    public readonly record struct Overloads
    {
        public required ImmutableArray<MethodModel> Methods { get; init; }
    }

    public readonly record struct MethodModel
    {
        public required TypeSyntaxReference? PayloadType { get; init; }
        public required TagType? Tag { get; init; }
        public required ImmutableArray<string> AcceptedTagValues { get; init; }
    }

    public sealed record TagType
    {
        public required TypeSyntaxReference Type { get; set; }
        public required string ShortName { get; set; }
        public required TypeSyntaxReference? SupersetReference { get; set; }
        public required bool IsEnum { get; init; }
    }
}

