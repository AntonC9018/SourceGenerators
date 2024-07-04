using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneration.Extensions;

public static class AnalyzerConfigOptionsExtensions
{
    public static string? GetMSBuildProperty(this AnalyzerConfigOptions options, string name)
    {
        if (options.TryGetValue($"build_property.{name}", out var value))
        {
            return value;
        }

        return null;
    }

    public static string? GetRootNamespace(this AnalyzerConfigOptions options)
    {
        if (options.TryGetValue("build_property.RootNamespace", out var value))
        {
            return value;
        }

        return null;
    }
}
