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

    public static IncrementalValueProvider<string> ForTypeParamOfAttributeWithName<T>(
        this SyntaxValueProvider syntaxProvider)
    {
        var fullName = typeof(T).FullName!;
        return syntaxProvider
            .ForAttributeWithMetadataName(
                fullName,
                predicate: (node, _) => node is CompilationUnitSyntax,
                (context, _) =>
                {
                    var type = context.SemanticModel.Compilation.GetTypeByMetadataName(fullName);
                    var attribute = context.Attributes.First(x =>
                    {
                        if (x.AttributeClass is null)
                        {
                            return false;
                        }
                        return x.AttributeClass.Equals(type, SymbolEqualityComparer.Default);
                    });
                    if (attribute.ConstructorArguments.Length == 0)
                    {
                        return null;
                    }
                    var p = attribute.ConstructorArguments[0];
                    if (p.Value is not { } val)
                    {
                        return null;
                    }
                    if (val is not INamedTypeSymbol symbol)
                    {
                        return null;
                    }
                    return symbol.ToDisplayString();
                })
            .Where(x => x is not null)
            // Is this really the right way to do this?
            .Collect()
            .Select((x, _) => x.FirstOrDefault())!;
    }

    public static IncrementalValueProvider<string> ForResultBaseAttribute(
        this SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            typeof(ResultBaseAttribute).FullName!,
            predicate: (node, _) => node is CompilationUnitSyntax,
            (context, _) =>
            {
                var attribute = context.Attributes.First(typeof(ResultBaseAttribute).FullName!);
                return attribute.ConstructorArguments.FirstOrDefault().Value as string;
            }).Where(x => x is not null);
    }

    public static IncrementalValuesProvider<T> ForWellKnownResultAttribute<T>(
        this SyntaxValueProvider syntaxProvider,
        Func<TypedGeneratorContextGlobal, CancellationToken, T> valueFactory)
    {
        return syntaxProvider.ForAttributeWithMetadataName(
            typeof(WellKnownResultAttribute).FullName!,
            predicate: (node, _) => node is CompilationUnitSyntax,
            (context, cancellationToken) =>
            {
                var attribute = context.Attributes.First(typeof(WellKnownResultAttribute).FullName!);
                var typedContext = new TypedGeneratorContextGlobal(
                    attribute,
                    context.SemanticModel);
                return valueFactory(typedContext, cancellationToken);
            });
    }
}
