using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ResultTypes.SourceGenerator;

public static class ShouldBeAutogened
{
    public readonly struct TypedGeneratorContext
    {
        public readonly SemanticModel SemanticModel;
        public readonly IMethodSymbol TargetSymbol;

        public TypedGeneratorContext(
            IMethodSymbol targetSymbol,
            SemanticModel semanticModel)
        {
            TargetSymbol = targetSymbol;
            SemanticModel = semanticModel;
        }
    }

    public static IncrementalValuesProvider<T> ForGenerateResultTypesAttribute<T>(
        this SyntaxValueProvider syntaxProvider,
        Func<TypedGeneratorContext, CancellationToken, T> valueFactory)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            typeof(GenerateResultTypeAttribute).FullName!,
            predicate: (node, _) => node is CompilationUnitSyntax,
            (context, cancellationToken) =>
            {
                var s = (IMethodSymbol) context.TargetSymbol;
                return valueFactory(new(s, context.SemanticModel), cancellationToken);
            });
    }

    public readonly struct TypedGeneratorContextGlobal
    {
        public readonly SemanticModel SemanticModel;
        public readonly AttributeData Attribute;

        public TypedGeneratorContextGlobal(
            AttributeData attribute,
            SemanticModel semanticModel)
        {
            Attribute = attribute;
            SemanticModel = semanticModel;
        }
    }

    public static IncrementalValuesProvider<T> ForResultBaseAttribute<T>(
        this SyntaxValueProvider syntaxProvider,
        Func<TypedGeneratorContextGlobal, CancellationToken, T> valueFactory)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            typeof(ResultBaseAttribute).FullName!,
            predicate: (node, _) => node is CompilationUnitSyntax,
            (context, cancellationToken) =>
            {
                var attribute = context.Attributes.First(typeof(ResultBaseAttribute).FullName!);
                var typedContext = new TypedGeneratorContextGlobal(
                    attribute,
                    context.SemanticModel);
                return valueFactory(typedContext, cancellationToken);
            });
    }
}
