using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ConsumerShared;
using SourceGeneration.Helpers;
using SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetStandard;
using SourceGeneration.Extensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AutoImplementedProperties.SourceGenerator;

/// <summary>
/// A source generator creating constructors for types annotated with <see cref="AutoConstructorAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AutoImplementedPropertyGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Info> constructorInfo = context.SyntaxProvider
            .ForAutoImplementedAttribute(static (context, _) => GetInfo(context))
            .Where(static info => info.HasPropertiesToImplement);

        // Generate the constructors
        context.RegisterSourceOutput(constructorInfo, static (context, item) =>
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
            public string TypeDisplayName { get; set; }
            public string Name { get; set; }
        }
        public record struct OverloadedProperty
        {
            public Property Property { get; set; }
            public string InterfaceNameForExplicitImpl { get; set; }
        }
        public bool HasPropertiesToImplement => PropertiesToImplement.Any()
            || OverloadedPropertiesToImplement.Any();
        public required EquatableArray<Property> PropertiesToImplement { get; init; }
        public required EquatableArray<OverloadedProperty> OverloadedPropertiesToImplement { get; init; }
        public required HierarchyInfo Hierarchy { get; init; }
    }

    private static readonly SymbolDisplayFormat FullyQualifiedWithNullability =
        new SymbolDisplayFormat(
            globalNamespaceStyle:
                SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle:
                SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions:
                SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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
                continue;

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
            using var enumerator = group.AsEnumerable().GetEnumerator();
            Debug.Assert(enumerator.MoveNext());

            Info.Property CreatePropInfo(IPropertySymbol p)
            {
                return new Info.Property
                {
                    Name = p.Name,
                    TypeDisplayName = p.Type.ToDisplayString(FullyQualifiedWithNullability),
                };
            }

            Info.OverloadedProperty CreateOverloadedPropInfo(IPropertySymbol p)
            {
                var info = CreatePropInfo(p);
                var info2 = new Info.OverloadedProperty
                {
                    Property = info,
                    InterfaceNameForExplicitImpl = p.ContainingType
                        .ToDisplayString(FullyQualifiedWithNullability),
                };
                return info2;
            }

            var first = enumerator.Current!;
            if (enumerator.MoveNext() == false)
            {
                var info = CreatePropInfo(first);
                propertiesToImplement.Add(info);
            }
            else
            {

                {
                    var info = CreateOverloadedPropInfo(first);
                    overloadedPropertiesToImplement.Add(info);
                }
                do
                {
                    var current = enumerator.Current!;
                    var info = CreateOverloadedPropInfo(current);
                    overloadedPropertiesToImplement.Add(info);
                }
                while (enumerator.MoveNext());
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
            TypeSyntax type = IdentifierName(p.TypeDisplayName);
            SyntaxToken name = Identifier(p.Name);
            var propertyDeclaration = PropertyDeclaration(type, name)
                .WithAccessorList(AccessorList(List(new[]
                {
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
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
                IdentifierName(p.InterfaceNameForExplicitImpl));
            var propertyDeclaration = CreateFromInfo(p.Property)
                .WithExplicitInterfaceSpecifier(explicitInterfaceSpecifier);
            propertyDeclarations.Add(propertyDeclaration);
        }

        var members = propertyDeclarations.ToArray();
        var result = info.Hierarchy.GetSyntax(members);
        return result;
    }
}
