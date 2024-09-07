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
using ResultTypes.Shared;
using SourceGeneration.Models;
using TypeInfo = SourceGeneration.Models.TypeInfo;

namespace ResultTypes.SourceGenerator;

internal sealed record Config
{
    public required TypeSyntaxReference ResultBase { get; init; }
    public required TypeSyntaxReference WellKnownTypes { get; init; }
    public required TypeSyntaxReference ResultSet { get; init; }
}

internal readonly record struct ConfigAndModel
{
    public required Model Model { get; init; }
    public required Config Config { get; init; }
}

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
            .ForGenerateResultTypesAttribute(static (context, ct) => CreateModel(context, ct))
            .Where(x => x != null)!;

        IncrementalValueProvider<Config> config =
            context.SyntaxProvider.ForTypeConstructorArgOfAttributeOnAssembly<
                    ResultBaseAttribute,
                    (TypeSyntaxReference ResultBase, TypeSyntaxReference ResultSet)>(x =>
                {
                    TypeSyntaxReference ResultSet()
                    {
                        var resultSetOfMethod = x.ConstructorArgument
                            .GetMembers()
                            .OfType<IMethodSymbol>()
                            .FirstOrDefault(m => m.Name == "ResultSetOf");
                        if (resultSetOfMethod == null)
                        {
                            // TODO: A warning is warranted as well.
                            return new("ResultSet");
                        }

                        var returnType = resultSetOfMethod.ReturnType;
                        return new(returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }

                    return (TypeSyntaxReference.From(x.ConstructorArgument), ResultSet());
                })
                .First()
                .Combine(context.SyntaxProvider.ForTypeParamOfAttributeWithName<WellKnownResultType>())
                .Select((x, _) => new Config
                {
                    ResultBase = x.Left.ResultBase,
                    ResultSet = x.Left.ResultSet,
                    WellKnownTypes = x.Right,
                });

        var final = propertiesInfo
            .Combine(config)
            .Select((x, _) => new ConfigAndModel
            {
                Config = x.Right,
                Model = x.Left,
            });

        context.RegisterSourceOutput(final, static (context, item) =>
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

    private static Model? CreateModel(
        ShouldBeAutogened.TypedGeneratorContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol.ReturnType is not INamedTypeSymbol returnType)
        {
            return null;
        }

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
        INamespaceOrTypeSymbol containerForHierarchy = context.TargetSymbol.ContainingType;
        if (returnType.Name != "Result")
        {
            containerForHierarchy = (INamespaceOrTypeSymbol) containerForHierarchy.ContainingType
                ?? containerForHierarchy.ContainingNamespace;
        }

        // If a type is already defined, we must respect its positioning.
        HierarchyInfo GetResultHierarchy()
        {
            if (!returnType.DeclaringSyntaxReferences.IsEmpty)
            {
                return HierarchyInfo.From(returnType);
            }

            var defaultTypeInfo = new TypeInfo(returnType.Name, TypeKind.Struct, IsRecord: true);
            return HierarchyInfo.FromContainer(
                containerForHierarchy,
                defaultTypeInfo);
        }

        Model.Overloads ConvertToModel(State state)
        {
            using var builder = ImmutableArrayBuilder<Model.MethodModel>.Rent();
            foreach (var x in state.ResultSets.Values.WrittenSpan)
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

                builder.Add(new()
                {
                    TagTypeShortName = x.TagType?.Name,
                    PayloadType = x.PayloadType is null ? null : TypeSyntaxReference.From(x.PayloadType),
                    TagType = x.TagType is null ? null : TypeSyntaxReference.From(x.TagType),
                    AcceptedTagValues = acceptedTagValues.ToImmutable(),
                });
            }
            return new()
            {
                Methods = builder.ToImmutable(),
                HasMethodWithNoArgs = state.HasDefault,
            };
        }

        return new()
        {
            ResultHierarchy = GetResultHierarchy(),
            OkOverloads = ConvertToModel(okState),
            FailureMethods = ConvertToModel(failureState),
        };
    }

    private static void GenerateCachedPropertyInfos(ConfigAndModel p, IndentedTextWriter w)
    {
        w.WriteFileStart(nullableEnable: true);
        using var s1 = w.StartHierarchy(p.Model.ResultHierarchy);

        var ownShortName = p.Model.ResultHierarchy.Hierarchy[^1].Name;
        w.WriteLine($"public required {ownShortName}Tag Tag {{ get; init; }}");
        w.WriteLine($"public Exception? Exception {{ get; init; }}");

        void WritePayloadFieldName(string defaultPrefix, Model.MethodModel m)
        {
            if (m.PayloadType is { } payloadType)
            {
                w.Write(payloadType);
                return;
            }

            if (m.TagType is not { })
            {
                w.Write($"{defaultPrefix}Payload");
                return;
            }

            // Allows all values
            if (m.AcceptedTagValues.IsEmpty)
            {
                w.Write($"{m.TagTypeShortName!}Payload");
                return;
            }

            w.Write($"{m.AcceptedTagValues[0]}Payload");
        }

        void WriteOverloadFields(string defaultPrefix, Model.Overloads overloads)
        {
            var methods = overloads.Methods;
            if (methods.Length > 0)
            {
                bool IsMoreThanOnePayload()
                {
                    bool isOnePayload = false;
                    foreach (var m in methods)
                    {
                        if (m.PayloadType is not { } payloadType)
                        {
                            continue;
                        }
                        if (isOnePayload)
                        {
                            return true;
                        }
                        isOnePayload = true;
                    }
                    return false;
                }

                if (IsMoreThanOnePayload())
                {
                    foreach (var m in methods)
                    {
                        if (m.PayloadType is not { } payloadType)
                        {
                            continue;
                        }

                        void WriteField()
                        {
                            w.Write($"public {payloadType} ");
                            WritePayloadFieldName(defaultPrefix, m);
                        }

                        WriteField();
                        w.WriteLine(";");
                    }
                }
                else
                {
                    foreach (var m in methods)
                    {
                        if (m.PayloadType is not { } payloadType)
                        {
                            continue;
                        }
                        w.WriteLine($"public {payloadType} {defaultPrefix}Payload;");
                        break;
                    }
                }

            }
        }

        void WriteConstructorFunctions(
            string defaultPrefix,
            bool includeExceptionParam,
            Model.Overloads overloads)
        {
            if (overloads.HasMethodWithNoArgs)
            {
                w.WriteLine($"public static {ownShortName} {defaultPrefix}()");
                using var b = w.WriteBlock();
                w.WriteLine($"return new()");
                using var b1 = w.WriteBlock();
                w.WriteLine($"Tag = new({p.Config.WellKnownTypes.FullyQualifiedName}.Ok),");
            }
            w.WriteLine();

            foreach (var m in overloads.Methods)
            {
                w.Write($"public static {ownShortName} {defaultPrefix}(");
                var list = w.List();

                if (m.TagType is { } tag)
                {
                    w.Write($"{tag.FullyQualifiedName} tag");
                }

                if (m.PayloadType is { } payloadType)
                {
                    w.Write($"{payloadType.FullyQualifiedName} payload");
                }

                if (!includeExceptionParam)
                {
                    list.Write("Exception? exception = null");
                }

                w.WriteLine(")");
                {
                    using var b = w.WriteBlock();

                    TagAsserts();
                    void TagAsserts()
                    {
                        if (m.AcceptedTagValues.Length > 1)
                        {
                            var tagType = m.TagType!.Value.FullyQualifiedName;
                            w.Write("Debug.Assert(tag is ");
                            var list = w.List(separator: " or ");
                            foreach (var tagName in m.AcceptedTagValues)
                            {
                                list.Write($"{tagType}.{tagName}");
                            }
                        }
                    }

                    w.WriteLine("return new()");
                    using var b1 = w.WriteBlock();

                    w.WriteLine($"Tag = new(tag),");

                    if (m.PayloadType is not null)
                    {
                        WritePayloadFieldName(defaultPrefix: defaultPrefix, m);
                        w.WriteLine($" = payload,");
                    }

                    if (!includeExceptionParam)
                    {
                        w.WriteLine("Exception = exception,");
                    }
                }
                w.WriteLine();
            }
        }

        WriteOverloadFields("Ok", p.Model.OkOverloads);
        WriteOverloadFields("Failure", p.Model.FailureMethods);
        WriteConstructorFunctions("Ok", includeExceptionParam: false, p.Model.OkOverloads);
        WriteConstructorFunctions("Failure", includeExceptionParam: true, p.Model.FailureMethods);

        {

            // closes the block of the result type.
            var block = new IndentedTextWriter.Block(w);
            block.Dispose();

            // open a block for the tag type
            // we don't need to close it, the disposal
            // on the hierarchy will close it.
            // It is hacky, but that's what we have to do with the current abstractions.

            var last = p.Model.ResultHierarchy.Hierarchy[^1];
            last.WriteAsTypeDeclaration(w); // the same name + Tag
            w.WriteLine("Tag");
            _ = w.WriteBlock();

            // Constructor, for each of the supported types.
            // Checks for the supported tags of those types (asserts).
            // Using ResultBase.Create<T>(value) make try convert methods (with assertions too).
            // ResultSetsOf() for this type
            // Declare() that declares all the base sets this depends on
            //    - if enum, ResultBase.Declare<T>()
            //    - if tag, T.Declare()
            // explicit casts that assert that the value is not 0
            // IsOk and IsFailure ?
        }


    }
}

