using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using ResultTypes.Shared;
using SourceGeneration.Models;

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

internal readonly record struct ResultBaseConfig(
    TypeSyntaxReference ResultBase,
    TypeSyntaxReference ResultSet)
{
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
            Models = p.Model.OverloadsSets,
            SinglePayloadIndex = default,
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

        foreach (var overloadInfo in allOverloadsContext)
        {
            SetupSinglePayloadIndex(overloadInfo);
        }

        var resultTypeInfo = p.Model.ResultHierarchy.Hierarchy[^1];

        w.WriteFileStart(nullableEnable: true);

        foreach (var import in p.Model.Imports)
        {
            w.WriteLine($"using {import};");
        }

        WriteResult();
        WritePayload();


        using var tagsContext = WriteTagsContext.Create(new()
        {
            Config = p.Config,
            Model = p.Model,
            AllOverloadsContext = allOverloadsContext,
        });
        tagsContext.WriteTagsType(new()
        {
            Config = p.Config,
            Model = p.Model,
            Writer = w,
            AllOverloadsContext = allOverloadsContext,
        });

        void WriteResult()
        {
            using var s1 = w.StartHierarchy(p.Model.ResultHierarchy, p.Model.ResultAccessibility);

            w.WriteLine($"public required {resultTypeInfo.Name}Tag Tag {{ get; init; }}");
            w.WriteLine($"public global::System.Exception? Exception {{ get; init; }}");

            foreach (var overloadsInfo in allOverloadsContext)
            {
                WriteConstructorFunctions(overloadsInfo);
            }

            foreach (var overloadsInfo in allOverloadsContext)
            {
                if (overloadsInfo.SinglePayloadIndex == -1)
                {
                }
            }
        }

        void WritePayload()
        {
            using var c = StartPayload();

            WriteAllPayloadFields();
        }

        HierarchyCleanup StartPayload()
        {
            if (p.Model.SharedPayloadHierarchy is { } payloadHierarchy)
            {
                return w.StartHierarchy(payloadHierarchy);
            }
            else
            {
                var payloadTypeInfo = resultTypeInfo with
                {
                    Name = resultTypeInfo.Name + "Payload",
                };
                return w.StartHierarchy(
                    p.Model.ResultHierarchy,
                    lastAccessibility: p.Model.ResultAccessibility,
                    lastReplacement: payloadTypeInfo);
            }
        }

        static string? GetPayloadFieldName(in Model.Method m)
        {
            if (m.Payload is not { })
            {
                return null;
            }
            return m.Payload.Type.ShortName;
        }

        void SetupSinglePayloadIndex(OverloadsInfo info)
        {
            int SinglePayloadIndex()
            {
                var methods = info.Model.Methods;
                int i = 0;
                int index = -1;
                while (i < methods.Length)
                {
                    var payload = methods[i].Payload;
                    if (payload is not null)
                    {
                        index = i;
                        i++;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
                for (; i < methods.Length; i++)
                {
                    var payload = methods[i].Payload;
                    if (payload is not null)
                    {
                        return -1;
                    }
                }
                return index;
            }

            info.SinglePayloadIndex = SinglePayloadIndex();
        }

        void WriteAllPayloadFields()
        {
            bool allHaveSinglePayload = allOverloadsContext.SinglePayloadIndex
                .Reduce(static (a, x) => a && x != -1, initialState: true);

            if (!allHaveSinglePayload)
            {
                // HashSet pool?
                var writtenPayloads = new HashSet<string>();

                foreach (var overloadsInfo in allOverloadsContext)
                {
                    if (overloadsInfo.HasSinglePayload)
                    {
                        continue;
                    }

                    WritePayloadFields(overloadsInfo, writtenPayloads);
                }
            }
        }

        void WritePayloadFields(
            OverloadsInfo info,
            HashSet<string> alreadyWrittenPayloads)
        {
            var methods = info.Model.Methods;

            for (int i = 0; i < methods.Length; i++)
            {
                if (GetPayloadFieldName(methods[i]) is not { } payloadName)
                {
                    continue;
                }
                if (!alreadyWrittenPayloads.Add(payloadName))
                {
                    continue;
                }
                var type = methods[i].Payload!.Type.QualifiedName!;

                w.WriteLine($"public {type} {payloadName};");
            }
        }

        void WriteConstructorFunctions(
            OverloadsInfo info)
        {
            bool includeExceptionParam = info.IncludeExceptionParameter;
            var overloads = info.Model;

            for (int i = 0; i < overloads.Methods.Length; i++)
            {
                var m = overloads.Methods[i];
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
                            w.Write($"global::{typeof(Debug).FullName!}.Assert(tag is ");
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

                    if (m.Payload is not null)
                    {
                        if (info.HasSinglePayload)
                        {
                            w.WriteLine("Payload = payload,");
                        }
                        else
                        {
                            w.WriteLine("Payload = new()");
                            using var b2 = w.WriteBlock(",");

                            var payloadName = GetPayloadFieldName(m);
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

internal enum OverloadTag
{
    Ok,
    Failure,
    Count,

    _Start = Ok,
    _End = Failure,
}

internal struct OneForEachOverloadSet<T>
{
    public required T Ok;
    public required T Failure;

    [UnscopedRef]
    public ref T Ref(OverloadTag overloadTag)
    {
        switch (overloadTag)
        {
            case OverloadTag.Ok:
                return ref Ok;
            case OverloadTag.Failure:
                return ref Failure;
            default:
                throw new ArgumentOutOfRangeException(nameof(overloadTag));
        }
    }

    public TState Reduce<TState>(Func<TState, T, TState> f, TState initialState)
    {
        var s = initialState;
        for (var i = OverloadTag._Start; i <= OverloadTag._End; i++)
        {
            s = f(s, Ref(i));
        }
        return s;
    }
}

internal struct OverloadsInfo
{
    private readonly AllOverloadsContext All;
    private readonly OverloadTag Tag;

    public OverloadsInfo(AllOverloadsContext all, OverloadTag tag)
    {
        All = all;
        Tag = tag;
    }

    public bool IncludeExceptionParameter
    {
        get
        {
            return Tag == OverloadTag.Failure;
        }
    }

    public Model.Overloads Model => All.Models.Ref(Tag);
    public string DefaultPrefix => All.DefaultPrefixes.Ref(Tag);
    public bool HasSinglePayload => SinglePayloadIndex != -1;
    public ref int SinglePayloadIndex => ref All.SinglePayloadIndex.Ref(Tag);
    public string DefaultTag => All.DefaultTags.Ref(Tag);
}

internal sealed class AllOverloadsContext
{
    public required OneForEachOverloadSet<Model.Overloads> Models;
    public required OneForEachOverloadSet<int> SinglePayloadIndex;
    public required OneForEachOverloadSet<string> DefaultPrefixes;
    public required OneForEachOverloadSet<string> DefaultTags;

    public OverloadsInfo For(OverloadTag tag)
    {
        return new(this, tag);
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly AllOverloadsContext _context;
        private OverloadTag _current;

        public Enumerator(AllOverloadsContext context)
        {
            _context = context;
            _current = OverloadTag._Start - 1;
        }

        public OverloadsInfo Current => _context.For(_current);

        public bool MoveNext()
        {
            _current++;
            return _current <= OverloadTag._End;
        }
    }
}
