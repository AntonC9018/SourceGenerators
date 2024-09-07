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

        IncrementalValueProvider<string> resultBaseConfig = context.SyntaxProvider
            .ForTypeParamOfAttributeWithName<ResultBaseAttribute>();

        IncrementalValueProvider<string> wellKnownTypesConfig = context.SyntaxProvider
            .ForWellKnownResultAttribute(static (c, _) => c.Attribute.ConstructorArguments.FirstOrDefault().Value as string);

        IncrementalValueProvider<(string ResultBase, string WellKnownTypes)> config = resultBaseConfig
            .Combine(wellKnownTypesConfig);

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

                    switch (args)
                    {
                        case []:
                        {
                            state.HasDefault = true;
                            break;
                        }
                        case [{ } x]:
                        {
                            var b = Helper.HandleConstant(new()
                            {
                                Argument = x,
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                            }, ref state.ResultSets);

                            if (b is { })
                            {
                                break;
                            }

                            break;
                        }
                        case [{ } x, { Expression: { } newTypeSyntax }]:
                        {
                            var b = Helper.HandleConstant(new()
                            {
                                Argument = x,
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                            }, ref state.ResultSets);
                            if (b is not { })
                            {
                                break;
                            }

                            Helper.HandleArgumentType(new()
                            {
                                CancellationToken = cancellationToken,
                                SemanticModel = context.SemanticModel,
                                NewTypeSyntax = newTypeSyntax,
                            }, ref state.ResultSets);

                            break;
                        }
                    }
                }
            }
        }
        finally
        {
            failureState.Dispose();
            okState.Dispose();
        }

        // It's not clear what to do with a default failure case?
        // Maybe it should just create like a SomeFailure member?

        INamespaceOrTypeSymbol containingOrNamespaceType = context.TargetSymbol.ContainingType;
        if (returnType.Name != "Result")
        {
            containingOrNamespaceType = (INamespaceOrTypeSymbol) containingOrNamespaceType.ContainingType
                ?? containingOrNamespaceType.ContainingNamespace;
        }

        using var good = ImmutableArrayBuilder<Model.MethodModel>.Rent();
        foreach (var x in okState.ResultSets.Values.WrittenSpan)
        {
            using var acceptedTagValues = ImmutableArrayBuilder<string>.Rent();

            if (x.Constants.Count > 0)
            {
                var members = x.TagType!.GetMembers().OfType<IFieldSymbol>().ToArray();

                foreach (var y in x.Constants.WrittenSpan)
                {
                    // get members in x.TagType with const value y
                    var member = members.FirstOrDefault(m => (int) m.ConstantValue! == y);
                    if (member is null)
                    {
                        // TODO: Issue warning
                        continue;
                    }

                    acceptedTagValues.Add(member.Name);
                }
            }

            good.Add(new()
            {
                PayloadType = x.PayloadType is null ? null : TypeSyntaxReference.From(x.PayloadType),
                TagType = x.TagType is null ? null : TypeSyntaxReference.From(x.TagType),
                AcceptedTagValues = acceptedTagValues.ToImmutable(),
            });
        }

        return new()
        {
            Hierarchy = HierarchyInfo.From(containingOrNamespaceType),
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

