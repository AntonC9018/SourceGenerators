using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AutoConstructor.Attributes;
using ConsumerShared;
using SourceGeneration.Helpers;
using SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetStandard;
using SourceGeneration.Extensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AutoConstructor.SourceGenerator;

/// <summary>
/// A source generator creating constructors for types annotated with <see cref="AutoConstructorAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AutoImplementedPropertyGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var info = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                typeof(AutoConstructorAttribute).FullName!,
                static s => s is TypeDeclarationSyntax,
                static context => new GetInfo(context));
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
        public record struct Constructor
        {
            public required EquatableArray<Parameter> Parameters { get; init; }
        }
        public record struct Parameter
        {
            public required string FullyQualifiedTypeName { get; init; }
            public required bool IsNullable { get; init; }
        }
        public record struct Member
        {
            public required string Name { get; init; }
            public required string FullyQualifiedTypeName { get; init; }
        }

        public required EquatableArray<Member> MemberNamesToSet { get; init; }
        public required EquatableArray<Constructor> Constructors { get; init; }
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

    private static Info GetInfo(GeneratorAttributeSyntaxContext context)
    {
        var classSymbol = (INamedTypeSymbol) context.TargetSymbol;
        var hierarchyInfo = HierarchyInfo.From(classSymbol);

        var memberNamesToSet = ImmutableArray.CreateBuilder<Info.Member>();
        foreach (var member in classSymbol.GetMembers())
        {
            // Ignore parent symbols
            if (!member.ContainingType.Equals(classSymbol, SymbolEqualityComparer.Default))
                continue;

            if (member is IPropertySymbol propertySymbol)
            {
                {
                    bool case1 =


                }


            }
        }




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
