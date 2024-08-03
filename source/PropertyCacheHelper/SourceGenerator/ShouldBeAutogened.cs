using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PropertyCacheHelper.Shared;

namespace PropertyCacheHelper.SourceGenerator;

public static class ShouldBeAutogened
{
    public readonly struct TypedGeneratorContext
    {
        public readonly SemanticModel SemanticModel;
        public readonly INamedTypeSymbol TargetSymbol;

        public TypedGeneratorContext(
            INamedTypeSymbol targetSymbol,
            SemanticModel semanticModel)
        {
            TargetSymbol = targetSymbol;
            SemanticModel = semanticModel;
        }
    }

    public static IncrementalValuesProvider<T> ForCachedPropertyAttribute<T>(
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
            typeof(CachePropertyInfoAttribute).FullName!,
            filter,
            (context, cancellationToken) =>
            {
                INamedTypeSymbol symbol = (INamedTypeSymbol) context.TargetSymbol;
                var typedContext = new TypedGeneratorContext(
                    symbol,
                    context.SemanticModel);
                return valueFactory(typedContext, cancellationToken);
            });
    }
}
