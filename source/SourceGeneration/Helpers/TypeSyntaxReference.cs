using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoConstructor.SourceGenerator;

public record struct TypeSyntaxReference(string FullyQualifiedName)
{
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

    public static TypeSyntaxReference From(ITypeSymbol type)
    {
        return new(type.ToDisplayString(FullyQualifiedWithNullability));
    }

    public static implicit operator string(TypeSyntaxReference d) => d.FullyQualifiedName;
    public readonly TypeSyntax AsSyntax() => SyntaxFactory.ParseTypeName(FullyQualifiedName);
}
