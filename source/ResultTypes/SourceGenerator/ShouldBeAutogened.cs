using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ResultTypes.Shared;
using SourceGeneration.Helpers;

namespace ResultTypes.SourceGenerator;

internal static class ShouldBeAutogened
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
            predicate: (node, _) => node is MethodDeclarationSyntax,
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

    public static IncrementalValueProvider<T?> First<T>(this IncrementalValuesProvider<T> provider)
        where T : struct
    {
        return provider
            .Collect()
            .Select((x, _) => x.Length == 0 ? (T?) null : x[0]);
    }

    public readonly record struct ConstructorArgContext
    {
        public required SemanticModel SemanticModel { get; init; }
        public required CompilationUnitSyntax CompilationUnitSyntax { get; init; }
        public required INamedTypeSymbol ConstructorArgument { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static IncrementalValuesProvider<U> ForTypeConstructorArgOfAttributeOnAssembly<T, U>(
        this SyntaxValueProvider syntaxProvider,
        Func<ConstructorArgContext, U?> transform)
        where U : struct
    {
        var fullName = typeof(T).FullName!;
        return syntaxProvider
            .ForAttributeWithMetadataName(
                fullName,
                predicate: (node, _) => node is CompilationUnitSyntax,
                (context, ct) =>
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
                        return default;
                    }
                    var p = attribute.ConstructorArguments[0];
                    if (p.Value is not { } val)
                    {
                        return default;
                    }
                    if (val is not INamedTypeSymbol symbol)
                    {
                        return default;
                    }

                    return transform(new()
                    {
                        CancellationToken = ct,
                        ConstructorArgument = symbol,
                        SemanticModel = context.SemanticModel,
                        CompilationUnitSyntax = (CompilationUnitSyntax) context.TargetNode,
                    });
                })
            .Where(x => x != null)
            .Select((x, _) => x!.Value);
    }

    public static IncrementalValueProvider<TypeSyntaxReference?> ForTypeParamOfAttributeWithName<T>(
        this SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider
            .ForTypeConstructorArgOfAttributeOnAssembly<T, TypeSyntaxReference>(x =>
            {
                return TypeSyntaxReference.From(x.ConstructorArgument);
            })
            .First();
    }
}
