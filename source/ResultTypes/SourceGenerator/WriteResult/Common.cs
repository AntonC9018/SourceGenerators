using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal readonly record struct CommonInfoGatherContext
{
    public required Config Config { get; init; }
    public required Model Model { get; init; }
    public required AllOverloadsContext AllOverloadsContext { get; init; }
}

internal readonly record struct CommonContext
{
    public required IndentedTextWriter Writer { get; init; }
    public required Config Config { get; init; }
    public required Model Model { get; init; }
    public required AllOverloadsContext AllOverloadsContext { get; init; }

    public TypeInfo ResultTypeInfo => Model.ResultHierarchy.Hierarchy[^1];
}

