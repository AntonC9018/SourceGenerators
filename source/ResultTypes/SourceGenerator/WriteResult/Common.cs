using System;
using System.Buffers;
using System.Diagnostics;
using SourceGeneration.Helpers;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal readonly record struct CommonInfoGatherContext
{
    public required Config Config { get; init; }
    public Model Model => AllOverloadsContext.Model;
    public required AllOverloadsContext AllOverloadsContext { get; init; }
    public TypeInfo ResultTypeInfo => Model.ResultHierarchy.Hierarchy[^1];
}

internal readonly record struct CommonContext
{
    public required IndentedTextWriter Writer { get; init; }
    public required Config Config { get; init; }
    public Model Model => AllOverloadsContext.Model;
    public required AllOverloadsContext AllOverloadsContext { get; init; }

    public TypeInfo ResultTypeInfo => Model.ResultHierarchy.Hierarchy[^1];
}

public static class WriteHelper
{
    public static readonly string AssertFunc = $"global::{typeof(Debug).FullName!}.Assert";
}
