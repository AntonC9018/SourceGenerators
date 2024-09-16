using System.Diagnostics;
using System.Linq;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using ResultTypes.Shared;
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
        IncrementalValuesProvider<Model> model = context.SyntaxProvider
            .ForGenerateResultTypesAttribute(static (context, ct) => CreateModelHelper.CreateModel(context, ct))
            .Where(x => x != null)!;

        IncrementalValueProvider<Config> config =
            context.SyntaxProvider.ForTypeConstructorArgOfAttributeOnAssembly<
                    ResultBaseAttribute,
                    ResultBaseConfig>(x =>
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
                            return new("global::ResultTypes.ResultSet");
                        }

                        var returnType = resultSetOfMethod.ReturnType;
                        return new(returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }

                    return new(
                        ResultBase: TypeSyntaxReference.From(x.ConstructorArgument),
                        ResultSet: ResultSet());
                })
                .First(new ResultBaseConfig(
                    ResultBase: new("global::ResultTypes.ResultBase"),
                    ResultSet: new("global::ResultTypes.ResultSet")))
                .Combine(
                    context.SyntaxProvider.ForTypeParamOfAttributeWithName<WellKnownResultAttribute>(
                        defaultValue: new TypeSyntaxReference("global::ResultTypes.WellKnownResult")))
                .Select((x, _) =>
                {
                    return new Config
                    {
                        ResultBase = x.Left.ResultBase,
                        ResultSet = x.Left.ResultSet,
                        WellKnownTypes = x.Right,
                    };
                });

        var final = model
            .Combine(config)
            .Select((x, _) => new ConfigAndModel
            {
                Config = x.Right,
                Model = x.Left,
            });

        context.RegisterSourceOutput(final, static (context, item) =>
        {
            var textWriter = new IndentedTextWriter();
            Generate(item, textWriter);
            context.AddSource(
                item.Model.ResultHierarchy.FullyQualifiedMetadataName + ".g.cs",
                textWriter.ToString());
        });
    }

    private static void Generate(ConfigAndModel p, IndentedTextWriter w)
    {
        var allOverloadsContext = new AllOverloadsContext
        {
            Model = p.Model,
            PayloadNames = new(p.Model, (model, tag) => model.OverloadsSets.Ref(tag).Methods.Length),
            ResultPayloadNames = new(p.Model, (model, tag) => model.OverloadsSets.Ref(tag).PassAlongResults.Length),
            DefaultPrefixes = new()
            {
                Ok = "Ok",
                Failure = "Failure",
            },
            DefaultTags = new()
            {
                Failure = "GenericFailure",
                Ok = "Ok",
            },
        };

        {
            var resultTypeNameMap = ResultTypeToFieldNameMap.Create(allOverloadsContext);
            foreach (var overloadInfo in allOverloadsContext)
            {
                PayloadHelper.SetupPayloadFieldNames(overloadInfo, resultTypeNameMap);
            }
        }

        using var tagsContext = WriteTagsContext.Create(new()
        {
            Config = p.Config,
            AllOverloadsContext = allOverloadsContext,
        });
        using var payloadContext = WritePayloadContext.Create(new()
        {
            Config = p.Config,
            AllOverloadsContext = allOverloadsContext,
        });

        var resultTypeInfo = p.Model.ResultHierarchy.Hierarchy[^1];

        w.WriteFileStart(nullableEnable: true);

        foreach (var import in p.Model.Imports)
        {
            w.WriteLine($"using {import};");
        }

        WriteResult();

        var commonContext = new CommonContext
        {
            Config = p.Config,
            Writer = w,
            AllOverloadsContext = allOverloadsContext,
        };
        tagsContext.WriteTagsType(commonContext);
        payloadContext.WritePayload(commonContext);

        void WriteResult()
        {
            using var s1 = w.StartHierarchy(p.Model.ResultHierarchy, p.Model.ResultAccessibility);

            w.WriteLine($"public required {resultTypeInfo.Name}Tag Tag {{ get; init; }}");
            w.WriteLine($"public global::System.Exception? Exception {{ get; init; }}");

            {
                w.Write($"public {payloadContext.PayloadTypeName} Payload");
                if (allOverloadsContext.IsPayloadNeverAssigned())
                {
                    w.Write(" => new()");
                }
                w.WriteLine(";");
            }

            foreach (var overloadsInfo in allOverloadsContext)
            {
                WriteConstructorFunctions(overloadsInfo);
            }
        }

        void WriteConstructorFunctions(
            OverloadSetInfoAccessor info)
        {
            bool includeExceptionParam = info.IncludeExceptionParameter;
            var model = info.Model;

            for (int index = 0; index < model.PassAlongResults.Length; index++)
            {
                var passAlongResult = model.PassAlongResults[index];
                w.WriteLine(
                    $"public static {resultTypeInfo.Name} {info.DefaultPrefix}({passAlongResult.ResultType.QualifiedName} otherResult)");
                using var b = w.WriteBlock();

                {
                    var shouldNegateIsOk = info.Tag == OverloadTag.Failure;
                    var negationText = shouldNegateIsOk ? "!" : "";

                    if (info.Tag == OverloadTag.Ok)
                    {
                        w.WriteLine(
                            $"{WriteHelper.AssertFunc}({negationText}otherResult.Tag.IsOk, \"Check IsOk before constructing the result\");");
                    }
                }

                {
                    w.WriteLine("return new()");
                    using var b1 = w.WriteBlock(endChar: ";");

                    w.WriteLine("Tag = new(otherResult.Tag),");
                    {
                        w.WriteLine("Payload = new()");
                        using var b2 = w.WriteBlock(endChar: ",");

                        var payloadFieldName = info.ResultPayloadNames[index];
                        w.WriteLine($"{payloadFieldName} = otherResult.Payload,");
                    }

                    w.WriteLine("Exception = otherResult.Exception,");
                }
            }

            for (int i = 0; i < model.Methods.Length; i++)
            {
                var m = model.Methods[i];
                w.Write($"public static {resultTypeInfo.Name} {info.DefaultPrefix}(");
                {
                    var list = w.List();

                    if (m.Tag is { } tag)
                    {
                        list.Write($"{tag.Type.QualifiedName} tag");
                    }

                    if (m.Payload is { } payloadType)
                    {
                        list.Write($"{payloadType.Type.QualifiedName} payload");
                    }

                    if (includeExceptionParam)
                    {
                        list.Write("global::System.Exception? exception = null");
                    }
                }
                w.WriteLine(")");

                {
                    using var b = w.WriteBlock();

                    if (m.Tag is { } tag)
                    {
                        if (!m.AcceptedTagValues.IsEmpty)
                        {
                            var tagType = tag.Type.QualifiedName;
                            w.Write($"{WriteHelper.AssertFunc}(tag is ");
                            var list = w.List(separator: " or ");
                            foreach (var tagName in m.AcceptedTagValues)
                            {
                                list.Write($"{tagType}.{tagName}");
                            }

                            w.WriteLine(");");
                        }
                    }
                    else
                    {
                        w.WriteLine($"var tag = {p.Config.WellKnownTypes}.{info.DefaultTag};");
                    }

                    w.WriteLine("return new()");
                    using var b1 = w.WriteBlock(endChar: ";");

                    w.WriteLine($"Tag = new(tag),");

                    if (m.Payload is { } payload)
                    {
                        if (payload.IsWholePayload)
                        {
                            w.WriteLine("Payload = payload,");
                        }
                        else
                        {
                            var payloadName = info.PayloadNames[i]!;
                            Debug.Assert(payloadName != null);

                            w.WriteLine("Payload = new()");
                            using var b2 = w.WriteBlock(",");

                            w.WriteLine($"{payloadName} = payload,");
                        }
                    }

                    if (includeExceptionParam)
                    {
                        w.WriteLine("Exception = exception,");
                    }
                }
                w.WriteLine();
            }
        }
    }
}

internal readonly record struct ResultBaseConfig(
    TypeSyntaxReference ResultBase,
    TypeSyntaxReference ResultSet)
{
}

