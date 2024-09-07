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
        public required TypeSyntaxReference? PayloadType { get; init; }
        public required TypeSyntaxReference? TagType { get; init; }
        public required ImmutableArray<string> AcceptedTagValues { get; init; }
    }
}

