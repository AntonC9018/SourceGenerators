using System.Diagnostics;
using System.Linq;
using CachedPropertyInfo.Shared;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using SourceGeneration.Extensions;
using SourceGeneration.Models;

namespace CachedPropertyInfo.SourceGenerator;

/// <summary>
/// A source generator creating properties for types annotated with <see cref="CachedPropertyInfoAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CachedPropertyInfoGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Info> propertiesInfo = context.SyntaxProvider
            .ForAutoImplementedAttribute(static (context, _) => GetInfo(context))
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
        }

        public required EquatableArray<Property> Properties { get; init; }
        public required TypeSyntaxReference ParentType { get; init; }
        public required string Namespace { get; init; }
        public required string ParentTypeName { get; init; }
    }

    private static Info GetInfo(ShouldBeAutogened.TypedGeneratorContext context)
    {
        var properties = context.TargetSymbol
            .GetAllMembers()
            .OfType<IPropertySymbol>();
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

            propertiesBuilder.Add(new()
            {
                Type = TypeSyntaxReference.From(p.Type),
                Name = p.Name,
            });
        }

        return new Info
        {
            Properties = propertiesBuilder.ToImmutable(),
            ParentType = TypeSyntaxReference.From(context.TargetSymbol),
            Namespace = context.TargetSymbol.ContainingNamespace.ToDisplayString(NamespaceDisplayFormat),
            ParentTypeName = context.TargetSymbol.Name,
        };
    }

    private static readonly SymbolDisplayFormat NamespaceDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private static void GenerateCachedPropertyInfos(Info info, IndentedTextWriter w)
    {
        var t = typeof(Shared.CachedPropertyInfo<,>);
        var cachedPropertyTypeName = t.FullName![.. t.FullName.IndexOf('`')];
        Debug.Assert(cachedPropertyTypeName != "");

        w.WriteFileStart(nullableEnable: true);
        w.WriteLineIf(info.Namespace != "", $"namespace {info.Namespace};");
        w.WriteLine($"public static class {info.ParentTypeName}Props");
        using (w.WriteBlock())
        {
            foreach (var p in info.Properties)
            {
                w.WriteLine($"public static readonly global::{cachedPropertyTypeName}<{info.ParentType.FullyQualifiedName}, {p.Type.FullyQualifiedName}> {p.Name} = new(x => x.{p.Name});");
            }
        }
    }
}
