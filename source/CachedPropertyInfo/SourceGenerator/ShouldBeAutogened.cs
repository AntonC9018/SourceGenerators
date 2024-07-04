using System;
using System.Threading;
using CachedPropertyInfo.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CachedPropertyInfo.SourceGenerator;

public static class ShouldBeAutogened
{
    public readonly struct TypedGeneratorContext
    {
        public readonly INamedTypeSymbol TargetSymbol;

        public TypedGeneratorContext(
            INamedTypeSymbol targetSymbol)
        {
            TargetSymbol = targetSymbol;
        }
    }

    public static IncrementalValuesProvider<T> ForAutoImplementedAttribute<T>(
        this SyntaxValueProvider syntaxProvider,
        Func<TypedGeneratorContext, CancellationToken, T> valueFactory,
        Func<SyntaxNode, CancellationToken, bool>? additionalSyntaxFilter = null)
    {
        Func<SyntaxNode, CancellationToken, bool> filter;
        if (additionalSyntaxFilter == null)
        {
            filter = (node, _) => node is TypeDeclarationSyntax;
        }
        else
        {
            filter = (node, token) => node is TypeDeclarationSyntax
                                      && additionalSyntaxFilter(node, token);
        }

        return syntaxProvider.ForAttributeWithMetadataName(
            typeof(CachedPropertyInfoAttribute).FullName
                ?? throw new InvalidOperationException("Attribute type without full name"),
            filter,
            (context, cancellationToken) =>
            {
                INamedTypeSymbol symbol = (INamedTypeSymbol) context.TargetSymbol;
                var typedContext = new TypedGeneratorContext(
                    symbol);
                return valueFactory(typedContext, cancellationToken);
            });
    }
}
