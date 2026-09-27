using Microsoft.CodeAnalysis;
using SourceGeneration.Helpers;

namespace Probe;

[Generator]
public sealed class ProbeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pool = new ObjectPool<string>(() => ProbeNames.GoodName, 1);
        var generatedValue = pool.Allocate();

        context.RegisterPostInitializationOutput(output =>
            output.AddSource(
                "Probe.g.cs",
                $"namespace Probe; public static class Generated {{ public const string Value = \"{generatedValue}\"; }}"));
    }
}
