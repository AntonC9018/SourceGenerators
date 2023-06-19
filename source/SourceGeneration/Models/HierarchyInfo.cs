using SourceGeneration.Extensions;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle;

namespace SourceGeneration.Models;

/// <summary>
/// A model describing the hierarchy info for a specific type.
/// </summary>
/// <param name="FullyQualifiedMetadataName">The fully qualified metadata name for the current type.</param>
/// <param name="Namespace">Gets the namespace for the current type.</param>
/// <param name="Hierarchy">Gets the sequence of type definitions containing the current type.</param>
internal sealed partial record HierarchyInfo(string FullyQualifiedMetadataName, string Namespace, EquatableArray<TypeInfo> Hierarchy)
{
    /// <summary>
    /// Creates a new <see cref="HierarchyInfo"/> instance from a given <see cref="INamedTypeSymbol"/>.
    /// </summary>
    /// <param name="typeSymbol">The input <see cref="INamedTypeSymbol"/> instance to gather info for.</param>
    /// <returns>A <see cref="HierarchyInfo"/> instance describing <paramref name="typeSymbol"/>.</returns>
    public static HierarchyInfo From(INamedTypeSymbol typeSymbol)
    {
        using var hierarchy = ImmutableArrayBuilder<TypeInfo>.Rent();

        for (INamedTypeSymbol? parent = typeSymbol;
             parent is not null;
             parent = parent.ContainingType)
        {
            hierarchy.Add(new TypeInfo(
                parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                parent.TypeKind,
                parent.IsRecord));
        }

        return new(
            typeSymbol.GetFullyQualifiedMetadataName(),
            typeSymbol.ContainingNamespace.ToDisplayString(new(typeQualificationStyle: NameAndContainingTypesAndNamespaces)),
            hierarchy.ToImmutable());
    }

    /// <summary>
    /// Creates a <see cref="CompilationUnitSyntax"/> instance for the current hierarchy.
    /// </summary>
    /// <param name="memberDeclarations">The member declarations to add to the generated type.</param>
    /// <returns>A <see cref="CompilationUnitSyntax"/> instance for the current hierarchy.</returns>
    public CompilationUnitSyntax GetSyntax(
        MemberDeclarationSyntax[] memberDeclarations,
        bool nullableEnable = true)
    {
        // Create the partial type declaration with for the current hierarchy.
        // This code produces a type declaration as follows:
        //
        // partial <TYPE_KIND> <TYPE_NAME>
        // {
        //     <MEMBER_DECLARATIONS>
        // }
        TypeDeclarationSyntax typeDeclarationSyntax =
            Hierarchy[0].GetSyntax()
            .AddModifiers(Token(SyntaxKind.PartialKeyword))
            .AddMembers(memberDeclarations);

        // Add all parent types in ascending order, if any
        foreach (TypeInfo parentType in Hierarchy.AsSpan()[1..])
        {
            typeDeclarationSyntax =
                parentType.GetSyntax()
                .AddModifiers(Token(SyntaxKind.PartialKeyword))
                .AddMembers(typeDeclarationSyntax);
        }

        SyntaxTriviaList syntaxTriviaList = GeneratedFileHelper.GetTriviaList(nullableEnable);

        if (Namespace is "")
        {
            // If there is no namespace, attach the pragma directly to the declared type,
            // and skip the namespace declaration. This will produce code as follows:
            //
            // <SYNTAX_TRIVIA>
            // <TYPE_HIERARCHY>
            return
                CompilationUnit()
                .AddMembers(typeDeclarationSyntax.WithLeadingTrivia(syntaxTriviaList))
                .NormalizeWhitespace(eol: "\n");
        }

        // Create the compilation unit with disabled warnings, target namespace and generated type.
        // This will produce code as follows:
        //
        // <SYNTAX_TRIVIA>
        // namespace <NAMESPACE>;
        //
        // <TYPE_HIERARCHY>
        return
            CompilationUnit().AddMembers(
            FileScopedNamespaceDeclaration(IdentifierName(Namespace))
            .WithLeadingTrivia(syntaxTriviaList)
            .AddMembers(typeDeclarationSyntax))
            .NormalizeWhitespace(eol: "\n");
    }
}

public static class GeneratedFileHelper
{
    // Prepare the leading trivia for the generated compilation unit.
    // This will produce code as follows:
    /*
        // <auto-generated/>
        #pragma warning disable
        #nullable enable
    */
    public static SyntaxTriviaList GetTriviaList(bool nullableEnable)
    {
        SyntaxTriviaList syntaxTriviaList;
        var autogenComment = Comment("// <auto-generated/>");
        var warningDisable = PragmaWarningDirectiveTrivia(
            Token(SyntaxKind.DisableKeyword), !nullableEnable);
        var nullableEnableDirective = NullableDirectiveTrivia(
            Token(SyntaxKind.EnableKeyword), nullableEnable);
        syntaxTriviaList = TriviaList(
            autogenComment,
            Trivia(warningDisable),
            Trivia(nullableEnableDirective));
        return syntaxTriviaList;
    }
}
