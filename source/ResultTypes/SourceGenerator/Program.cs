using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

/// <summary>
/// A source generator creating properties for types annotated with <see cref="CachePropertyInfoAttribute"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ResultTypesGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Model> propertiesInfo = context.SyntaxProvider
            .ForGenerateResultTypesAttribute(static (context, ct) => CreateModel(context, ct));

        IncrementalValuesProvider<string> config = context.SyntaxProvider
            .ForResultBaseAttribute(static (c, _) => c.Attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .Where(static s => s is not null)!;

        context.RegisterSourceOutput(propertiesInfo, static (context, item) =>
        {
            var textWriter = new IndentedTextWriter();
            GenerateCachedPropertyInfos(item, textWriter);
            context.AddSource(
                item.ParentTypeName + ".CachedPropertyInfo.g.cs",
                textWriter.ToString());
        });
    }

    private struct State() : IDisposable
    {
        public ResultSetsBuilder ResultSets = new ResultSetsBuilder();
        public bool HasDefault = false;

        public void Dispose()
        {
            ResultSets.Dispose();
        }
    }

    private static Model CreateModel(
        ShouldBeAutogened.TypedGeneratorContext context,
        CancellationToken cancellationToken)
    {
        var returnType = context.TargetSymbol.ReturnType;

        // Find return all syntax
        var returnSyntaxes = context.TargetSymbol
            .DeclaringSyntaxReferences
            .SelectMany(r => r.GetSyntax().DescendantNodes().OfType<ReturnStatementSyntax>());

        var failureState = new State();
        var okState = new State();
        try
        {
            foreach (var returnSyntax in returnSyntaxes)
            {
                if (returnSyntax.Expression is not InvocationExpressionSyntax invocation)
                {
                    continue;
                }
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }
                if (memberAccess.Expression is not MemberAccessExpressionSyntax returnTypeExpression)
                {
                    continue;
                }
                if (returnTypeExpression.Expression is not IdentifierNameSyntax returnTypeIdentifier)
                {
                    continue;
                }
                if (returnTypeIdentifier.Identifier.Text != returnType.Name)
                {
                    continue;
                }

                switch (memberAccess.Name.Identifier.Text)
                {
                    case "Failure":
                    {
                        Process(ref failureState);
                        break;
                    }

                    case "Ok":
                    {
                        Process(ref okState);
                        break;
                    }
                }

                void Process(ref State state)
                {
                    var args = invocation.ArgumentList.Arguments;
                    if (args.Count > 2)
                    {
                        // TODO: Issue warning in an analyzer.
                        return;
                    }

                    ref OverloadBuilder FindOrAddOverloadWithoutExplicitResult(ref ResultSetsBuilder builder)
                    {
                        foreach (ref var r in builder.Values.WrittenSpan)
                        {
                            if (r.IsWithoutExplicitResult)
                            {
                                return ref r;
                            }
                        }
                        builder.Values.Add(new()
                        {
                            Type = null,
                        });
                        return ref builder.Values.WrittenSpan[^1];
                    }

                    switch (args)
                    {
                        case []:
                        {
                            state.HasDefault = true;
                            break;
                        }
                        case [{ Expression: ObjectCreationExpressionSyntax newTypeSyntax }]:
                        {

                            ref var defaultOverload = ref FindOrAddOverloadWithoutExplicitResult(ref state.ResultSets);
                            Helper.HandleNew(new()
                            {
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                                NewTypeSyntax = newTypeSyntax,
                            }, ref defaultOverload.Properties);
                            break;
                        }
                        case [{ } x]:
                        {
                            Helper.HandleConstant(new()
                            {
                                Argument = x,
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                            }, ref state.ResultSets);
                            break;
                        }
                        case [{ } x, { Expression: ObjectCreationExpressionSyntax newTypeSyntax }]:
                        {
                            ref var overload = ref Helper.HandleConstant(new()
                            {
                                Argument = x,
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                            }, ref state.ResultSets);
                            if (Unsafe.IsNullRef(ref overload))
                            {
                                break;
                            }
                            Helper.HandleNew(new()
                            {
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                                NewTypeSyntax = newTypeSyntax,
                            }, ref overload.Properties);
                            break;
                        }
                    }
                }
            }

            using var goodBuilder = ImmutableArrayBuilder<Model.MethodModel>.Rent();
            foreach (var t in okState.ResultSets.Values.WrittenSpan)
            {
                using var properties = ImmutableArrayBuilder<Model.Property>.Rent();
                foreach (var p in t.Properties.WrittenSpan)
                {
                    properties.Add(new()
                    {
                        Name = p.Name,
                        Type = TypeSyntaxReference.From(p.Type),
                    });
                }

                using var results = ImmutableArrayBuilder<Model.ResultSet>.Rent();
                foreach (var r in t.)
                {
                    results.Add(new()
                    {
                        ProvidedConcreteValues = r.ProvidedConcreteValues.ToImmutable(),
                        Type = TypeSyntaxReference.From(r.Type),
                    });
                }

                goodBuilder.Add(new()
                {
                    Properties = properties.ToImmutable(),
                    ParamsModelType = null,
                    ResultSet =
                });
            }
        }
        finally
        {
            failureState.Dispose();
            okState.Dispose();
        }

        return new()
        {
            Hierarchy = HierarchyInfo.From(context.TargetSymbol.ContainingType),
            Ok = [],
        };
    }

    private static void GenerateCachedPropertyInfos(Info info, IndentedTextWriter w)
    {
        var t = typeof(CachedPropertyInfo<,>);
        var cachedPropertyTypeName = t.FullName![.. t.FullName.IndexOf('`')];
        Debug.Assert(cachedPropertyTypeName != "");

        w.WriteFileStart(nullableEnable: true);
        w.WriteLineIf(info.Namespace != "", $"namespace {info.Namespace};");

        var accessibilityString = SyntaxFacts.GetText(info.Accessibility);
        w.WriteLine($"{accessibilityString} static class {info.ParentTypeName}Props");

        using (w.WriteBlock())
        {
            foreach (var p in info.Properties)
            {
                w.Write($"public static readonly global::{cachedPropertyTypeName}<{info.ParentType.FullyQualifiedName}, {p.Type.FullyQualifiedName}> {p.Name} =");
                w.Write($" new(x => x.{p.Name}");
                w.WriteIf(p.JsonName != null, $", jsonPropertyName: \"{p.JsonName}\"");
                w.Write(");");
                w.WriteLine();
            }
        }
    }
}

