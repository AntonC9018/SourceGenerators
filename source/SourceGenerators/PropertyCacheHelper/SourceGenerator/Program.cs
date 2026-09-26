using System.Diagnostics;
using System.Linq;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PropertyCacheHelper.Shared;
using SourceGeneration.Extensions;
using SourceGeneration.Models;

namespace PropertyCacheHelper.SourceGenerator;

/// <summary>
/// A source generator creating properties for types annotated with <see cref="CachePropertyInfoAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CachedPropertyInfoGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Info> propertiesInfo = context.SyntaxProvider
            .ForCachedPropertyAttribute(static (context, _) => GetInfo(context))
            .Where(static info => info.Properties.Length > 0);

        context.RegisterSourceOutput(propertiesInfo, static (context, item) =>
        {
            var textWriter = new IndentedTextWriter();
            GenerateCachedPropertyInfos(item, textWriter);
            context.AddSource(
                item.ParentTypeName + ".CachedPropertyInfo.g.cs",
                textWriter.ToString());
        });
    }

    internal record Info
    {
        public readonly record struct Property
        {
            public required TypeSyntaxReference Type { get; init; }
            public required string Name { get; init; }
            public required string? JsonName { get; init; }
        }

        public required Accessibility Accessibility { get; init; }
        public required EquatableArray<Property> Properties { get; init; }
        public required TypeSyntaxReference ParentType { get; init; }
        public required string Namespace { get; init; }
        public required string ParentTypeName { get; init; }
    }

    private static Info GetInfo(ShouldBeAutogened.TypedGeneratorContext context)
    {
        // TODO: Maybe make getters for derived types to return the values in the base props class.
        var properties = context.TargetSymbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .ToArray();

        bool IsMarked(IPropertySymbol p)
        {
            return p.TryGetAttributeWithFullyQualifiedMetadataName(
                typeof(CachePropertyInfoAttribute).FullName!,
                out _);
        }
        bool hasMarkedProperties = properties.Any(IsMarked);

        var jsonPropertyAttribute = context.SemanticModel.Compilation
            .GetTypeByMetadataName("System.Text.Json.Serialization.JsonPropertyNameAttribute");


        using var propertiesBuilder = ImmutableArrayBuilder<Info.Property>.Rent();
        foreach (var p in properties)
        {
            if (p.IsStatic)
            {
                continue;
            }

            if (p.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (p.GetMethod is null)
            {
                continue;
            }

            if (hasMarkedProperties)
            {
                if (!IsMarked(p))
                {
                    continue;
                }
            }

            string? GetJsonName()
            {
                foreach (var attr in p.GetAttributes())
                {
                    if (attr.AttributeClass is not {} c)
                    {
                        continue;
                    }
                    if (!c.Equals(jsonPropertyAttribute, SymbolEqualityComparer.Default))
                    {
                        continue;
                    }
                    if (attr.TryGetConstructorArgument<string>(0, out var arg))
                    {
                        return arg;
                    }
                }
                return null;
            }

            propertiesBuilder.Add(new()
            {
                Type = TypeSyntaxReference.From(p.Type),
                Name = p.Name,
                JsonName = GetJsonName(),
            });
        }

        return new Info
        {
            Properties = propertiesBuilder.ToImmutable(),
            ParentType = TypeSyntaxReference.From(context.TargetSymbol),
            Namespace = context.TargetSymbol.ContainingNamespace.ToDisplayString(NamespaceDisplayFormat),
            ParentTypeName = context.TargetSymbol.Name,
            Accessibility = context.TargetSymbol.DeclaredAccessibility,
        };
    }

    private static readonly SymbolDisplayFormat NamespaceDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private static void GenerateCachedPropertyInfos(Info info, IndentedTextWriter w)
    {
        var t = typeof(CachedPropertyInfo<,>);
        var cachedPropertyTypeName = t.FullName![.. t.FullName.IndexOf('`')];
        Debug.Assert(cachedPropertyTypeName != "");

        w.WriteFileStart(nullableEnable: true);
        w.WriteLineIf(info.Namespace != "", $"namespace {info.Namespace};");

        var accessibilityString = SyntaxFacts.GetText(info.Accessibility);
        w.WriteLine($"{accessibilityString} static class {info.ParentTypeName}Props");

        using (w.WriteBlock())
        {
            foreach (var p in info.Properties)
            {
                w.Write($"public static readonly global::{cachedPropertyTypeName}<{info.ParentType.FullyQualifiedName}, {p.Type.FullyQualifiedName}> {p.Name} =");
                w.Write($" new(x => x.{p.Name}");
                w.WriteIf(p.JsonName != null, $", jsonPropertyName: \"{p.JsonName}\"");
                w.Write(");");
                w.WriteLine();
            }
        }
    }
}
