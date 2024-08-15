using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AutoImplementedProperties.Attributes;
using SourceGeneration.Helpers;
using SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Extensions;
using Utils.Shared;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AutoImplementedProperties.SourceGenerator;

/// <summary>
/// A source generator creating properties for types annotated with <see cref="AutoImplementPropertiesAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AutoImplementedPropertyGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Info> propertiesInfo = context.SyntaxProvider
            .ForAutoImplementedAttribute(static (context, _) => GetInfo(context))
            .Where(static info => info.HasPropertiesToImplement);

        context.RegisterSourceOutput(propertiesInfo, static (context, item) =>
        {
            var compilationUnit = GeneratePartialImplementingUnimplementedMembers(item);
            context.AddSource(
                item.Hierarchy.FullyQualifiedMetadataName + ".AutoProps.g.cs",
                compilationUnit.GetText(Encoding.UTF8));
        });
    }

    internal record Info
    {
        public record struct Property
        {
            public TypeSyntaxReference Type { get; set; }
            public string Name { get; set; }
        }
        public record struct OverloadedProperty
        {
            public Property Property { get; set; }
            public TypeSyntaxReference InterfaceForExplicitImpl { get; set; }
        }
        public bool HasPropertiesToImplement => PropertiesToImplement.Any()
            || OverloadedPropertiesToImplement.Any();
        public required EquatableArray<Property> PropertiesToImplement { get; init; }
        public required EquatableArray<OverloadedProperty> OverloadedPropertiesToImplement { get; init; }
        public required HierarchyInfo Hierarchy { get; init; }
    }

    private static Info GetInfo(ShouldBeAutogened.TypedGeneratorContext context)
    {
        var alwaysAutoImplementedAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(
            typeof(AlwaysAutoImplemented).FullName!)!;
        var interfaceProperties = context.TargetSymbol
            .AllInterfaces
            .Where(i => i
                .GetAttributes()
                .All(a => a.AttributeClass is { } ac
                    && !ac.Equals(alwaysAutoImplementedAttribute, SymbolEqualityComparer.Default)))
            .SelectMany(i => i.GetMembers().OfType<IPropertySymbol>())
            .Where(p => p.GetMethod is not null && p.SetMethod is not null);

        var interfacePropsSet = new HashSet<IPropertySymbol>(
            interfaceProperties, SymbolEqualityComparer.Default);

        var allProperties = context.TargetSymbol
            .GetAllMembers()
            .OfType<IPropertySymbol>();

        foreach (var p in allProperties)
        {
            if (p.GetMethod is null || p.SetMethod is null)
            {
                continue;
            }

            if (p is { ExplicitInterfaceImplementations: [{ } explicitImpl] })
            {
                interfacePropsSet.Remove(explicitImpl);
                continue;
            }

            interfacePropsSet.RemoveWhere(
                remainingProp => p.Name == remainingProp.Name);
        }

        var propsByName = interfacePropsSet
            .GroupBy(p => p.Name);

        using var propertiesToImplement = ImmutableArrayBuilder<Info.Property>.Rent();
        using var overloadedPropertiesToImplement = ImmutableArrayBuilder<Info.OverloadedProperty>.Rent();

        foreach (var group in propsByName)
        {
            IPropertySymbol firstProperty;
            bool shouldGenerateOverloads = false;
            {
                using var enumerator = group.GetEnumerator();

                // ReSharper disable once RedundantAssignment
                bool notEmpty = enumerator.MoveNext();
                Debug.Assert(notEmpty);

                firstProperty = enumerator.Current!;
                var firstType = firstProperty.Type;

                while (enumerator.MoveNext())
                {
                    if (enumerator.Current!.Type.Equals(firstType, SymbolEqualityComparer.IncludeNullability))
                    {
                        continue;
                    }

                    shouldGenerateOverloads = true;
                    break;
                }
            }

            static Info.Property CreatePropInfo(IPropertySymbol p)
            {
                var info = new Info.Property
                {
                    Type = TypeSyntaxReference.From(p.Type),
                    Name = p.Name,
                };
                return info;
            }

            static Info.OverloadedProperty CreateOverloadedPropInfo(IPropertySymbol p)
            {
                var info = CreatePropInfo(p);
                var info2 = new Info.OverloadedProperty
                {
                    Property = info,
                    InterfaceForExplicitImpl = TypeSyntaxReference.From(p.ContainingType),
                };
                return info2;
            }

            if (shouldGenerateOverloads)
            {
                foreach (var p in group)
                {
                    var info = CreateOverloadedPropInfo(p);
                    overloadedPropertiesToImplement.Add(info);
                }
            }
            else
            {
                var info = CreatePropInfo(firstProperty);
                propertiesToImplement.Add(info);
            }
        }

        var hierarchyInfo = HierarchyInfo.From(context.TargetSymbol);

        return new Info
        {
            PropertiesToImplement = propertiesToImplement.ToImmutable(),
            OverloadedPropertiesToImplement = overloadedPropertiesToImplement.ToImmutable(),
            Hierarchy = hierarchyInfo,
        };
    }

    private static CompilationUnitSyntax GeneratePartialImplementingUnimplementedMembers(Info info)
    {
        using var propertyDeclarations = ImmutableArrayBuilder<MemberDeclarationSyntax>.Rent();

        PropertyDeclarationSyntax CreateFromInfo(Info.Property p)
        {
            TypeSyntax type = IdentifierName(p.Type);
            SyntaxToken name = Identifier(p.Name);
            var propertyDeclaration = PropertyDeclaration(type, name)
                .WithAccessorList(AccessorList(List(new[]
                {
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                })));
            return propertyDeclaration;
        }

        foreach (var p in info.PropertiesToImplement)
        {
            var propertyDeclaration = CreateFromInfo(p)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
            propertyDeclarations.Add(propertyDeclaration);
        }

        foreach (var p in info.OverloadedPropertiesToImplement)
        {
            var explicitInterfaceSpecifier = ExplicitInterfaceSpecifier(
                p.InterfaceForExplicitImpl.AsNameSyntax());
            var propertyDeclaration = CreateFromInfo(p.Property)
                .WithExplicitInterfaceSpecifier(explicitInterfaceSpecifier);
            propertyDeclarations.Add(propertyDeclaration);
        }

        var members = propertyDeclarations.ToArray();
        var result = info.Hierarchy.GetSyntax(members);
        return result;
    }
}
