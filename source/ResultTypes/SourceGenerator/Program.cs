using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        IncrementalValuesProvider<Model> propertiesInfo = context.SyntaxProvider
            .ForGenerateResultTypesAttribute(static (context, ct) => CreateModel(context, ct))
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
                item.Model.ResultHierarchy.FullyQualifiedMetadataName + ".g.cs",
                textWriter.ToString());
        });
    }

    private struct State() : IDisposable
    {
        public ResultSetsBuilder ResultSets = new ResultSetsBuilder();

        public void Dispose()
        {
            ResultSets.Dispose();
        }
    }

    private struct States() : IDisposable
    {
        public State Ok = new();
        public State Failure = new();

        public void Dispose()
        {
            Ok.Dispose();
            Failure.Dispose();
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

        var taskType = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(Task<>).FullName!)!;
        if (returnType.OriginalDefinition.Equals(taskType, SymbolEqualityComparer.Default))
        {
            if (!context.TargetSymbol.IsAsync)
            {
                return null;
            }

            returnType = (INamedTypeSymbol) returnType.TypeArguments[0];
        }

        var subsetAttribute = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(SubsetAttribute).FullName!)!;
        var genericSubsetAttribute = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(SubsetAttribute<>).FullName!)!;
        var exceptionType = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(Exception).FullName!)!;

        {
            var states = new States();
            try
            {
                ProcessReturnSyntaxes(ref states);

                return new()
                {
                    ResultAccessibility = returnType.Kind == SymbolKind.ErrorType
                        ? Accessibility.Public
                        : returnType.DeclaredAccessibility,
                    ResultHierarchy = GetResultHierarchy(),
                    OverloadsSets = new()
                    {
                        Ok = ConvertToModel(states.Ok),
                        Failure = ConvertToModel(states.Failure),
                    },
                };
            }
            finally
            {
                states.Dispose();
            }
        }

        Helper.FindArgKindResult MatchArg(ArgumentSyntax argument, ArgKinds kinds)
        {
            return Helper.MatchArg(new()
            {
                Argument = argument,
                CancellationToken = cancellationToken,
                ExceptionSymbol = exceptionType,
                PossibleKinds = kinds,
                SemanticModel = context.SemanticModel,
            });
        }

        void ProcessReturnSyntaxes(ref States states)
        {
            // Find return all syntax
            var returnSyntaxes = context.TargetSymbol
                .DeclaringSyntaxReferences
                .SelectMany(r => r.GetSyntax().DescendantNodes().OfType<ReturnStatementSyntax>());

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
                if (memberAccess.Expression is not IdentifierNameSyntax returnTypeIdentifier)
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
                        Process(ref states.Failure, exceptionIsPayload: false);
                        break;
                    }

                    case "Ok":
                    {
                        Process(ref states.Ok, exceptionIsPayload: true);
                        break;
                    }
                }

                void Process(ref State state, bool exceptionIsPayload)
                {
                    var args = invocation.ArgumentList.Arguments;
                    var maxCount = exceptionIsPayload ? 2 : 3;
                    if (args.Count > maxCount)
                    {
                        // TODO: Issue warning in an analyzer.
                        return;
                    }

                    void AddDefault(ref State state)
                    {
                        state.ResultSets.Values.Add(new()
                        {
                            PayloadType = null,
                            TagType = null,
                        });
                    }

                    switch (args)
                    {
                        case []:
                        {
                            AddDefault(ref state);
                            break;
                        }
                        case [{ } x]:
                        {
                            ArgKinds kinds = ArgKinds.Const | ArgKinds.Tag | ArgKinds.Payload;
                            if (!exceptionIsPayload)
                            {
                                kinds |= ArgKinds.Exception;
                            }

                            var result = MatchArg(x, kinds);
                            if (result.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            bool isPayload = result.ArgInfo.Kind == ArgKinds.Payload;
                            bool isException = result.ArgInfo.Kind == ArgKinds.Exception;
                            bool isTag = !isPayload && !isException;

                            Helper.AddOrUpdateOverload(new()
                            {
                                CancellationToken = cancellationToken,
                                ConstValue = isTag ? result.ArgInfo.ConstValue : null,
                                PayloadType = isPayload ? result.ArgInfo.Type : null,
                                SemanticModel = context.SemanticModel,
                                TagType = isTag ? result.ArgInfo.Type : null,
                            }, ref state.ResultSets);

                            break;
                        }
                        case [{ } x, { } x1]:
                        {
                            ArgKinds kinds = ArgKinds.Const | ArgKinds.TagOrPayload;

                            var result1 = MatchArg(x, kinds);
                            if (result1.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            ArgKinds nextKinds = ArgKinds.Payload;
                            if (!exceptionIsPayload)
                            {
                                nextKinds |= ArgKinds.Exception;
                            }

                            var result2 = MatchArg(x1, nextKinds);
                            if (result2.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            ITypeSymbol? tagType;
                            ITypeSymbol? payloadType;

                            bool firstMustBeTag = result1.ArgInfo.Kind is ArgKinds.Const or ArgKinds.Tag;
                            bool isSecondException = result2.ArgInfo.Kind == ArgKinds.Exception;
                            bool firstMustBePayload = result1.ArgInfo.Kind == ArgKinds.Payload;

                            // tag, exception
                            if (firstMustBeTag)
                            {
                                tagType = result1.ArgInfo.Type;
                                payloadType = isSecondException ? null : result2.ArgInfo.Type;
                            }
                            // payload, exception
                            else if (firstMustBePayload)
                            {
                                if (!isSecondException)
                                {
                                    // TODO: warning
                                    break;
                                }
                                tagType = null;
                                payloadType = result1.ArgInfo.Type;
                            }
                            // tag, payload
                            else
                            {
                                tagType = result1.ArgInfo.Type;
                                payloadType = result2.ArgInfo.Type;
                            }

                            Helper.AddOrUpdateOverload(new()
                            {
                                CancellationToken = cancellationToken,
                                ConstValue = tagType != null ? result1.ArgInfo.ConstValue : null,
                                PayloadType = payloadType,
                                SemanticModel = context.SemanticModel,
                                TagType = tagType,
                            }, ref state.ResultSets);

                            break;
                        }
                        // tag, payload, exception
                        case [{ } x, { } x1, { } x2]:
                        {
                            if (exceptionIsPayload)
                            {
                                // TODO: Warning
                                break;
                            }

                            var result = MatchArg(x, ArgKinds.Const | ArgKinds.Tag);
                            if (result.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            var result1 = MatchArg(x1, ArgKinds.Payload);
                            if (result1.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            var result2 = MatchArg(x2, ArgKinds.Exception);
                            if (result2.Failure != FindArgKindFailure.None)
                            {
                                break;
                            }

                            Helper.AddOrUpdateOverload(new()
                            {
                                CancellationToken = cancellationToken,
                                ConstValue = result.ArgInfo.ConstValue,
                                PayloadType = result1.ArgInfo.Type,
                                SemanticModel = context.SemanticModel,
                                TagType = result.ArgInfo.Type,
                            }, ref state.ResultSets);
                            break;
                        }
                    }
                }
            }
        }

        // If a type is already defined, we must respect its positioning.
        HierarchyInfo GetResultHierarchy()
        {
            if (!returnType.DeclaringSyntaxReferences.IsEmpty)
            {
                return HierarchyInfo.From(returnType);
            }

            // It's not clear what to do with a default failure case?
            // Maybe it should just create like a SomeFailure member?
            INamespaceOrTypeSymbol containerForHierarchy = context.TargetSymbol.ContainingType;
            if (returnType.Name != "Result")
            {
                containerForHierarchy = (INamespaceOrTypeSymbol) containerForHierarchy.ContainingType
                    ?? containerForHierarchy.ContainingNamespace;
            }

            var defaultTypeInfo = new TypeInfo(returnType.Name, TypeKind.Struct, IsRecord: true);
            return HierarchyInfo.FromContainer(
                containerForHierarchy,
                defaultTypeInfo);
        }

        INamedTypeSymbol? GetSubsetType(ITypeSymbol tag)
        {
            var attributes = tag.GetAttributes();
            foreach (var attribute in attributes)
            {
                var c = attribute.AttributeClass;
                if (c is null)
                {
                    continue;
                }

                if (c.Equals(subsetAttribute, SymbolEqualityComparer.Default))
                {
                    return (INamedTypeSymbol) attribute.ConstructorArguments[0].Value!;
                }

                if (c.IsGenericType && c.OriginalDefinition.Equals(genericSubsetAttribute, SymbolEqualityComparer.Default))
                {
                    // get generic param
                    return (INamedTypeSymbol) c.TypeArguments[0];
                }
            }
            return null;
        }

        Model.Overloads ConvertToModel(State state)
        {
            using var builder = ImmutableArrayBuilder<Model.MethodModel>.Rent();
            foreach (var x in state.ResultSets.Values.WrittenSpan)
            {
                ImmutableArray<string> GetConstants()
                {
                    if (x.Constants.Count == 0)
                    {
                        return [];
                    }
                    var tagType = x.TagType!;
                    if (tagType.TypeKind != TypeKind.Enum)
                    {
                        return [];
                    }

                    using var acceptedTagValues = ImmutableArrayBuilder<string>.Rent();
                    var members = tagType.GetMembers().OfType<IFieldSymbol>().ToArray();

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
                    return acceptedTagValues.ToImmutable();
                }

                Model.TagType? GetTag()
                {
                    if (x.TagType is not { } tag)
                    {
                        return null;
                    }

                    TypeSyntaxReference? subsetTypeRef = null;
                    if (tag.TypeKind == TypeKind.Enum)
                    {
                        var subsetType = GetSubsetType(tag);
                        if (subsetType != null)
                        {
                            subsetTypeRef = TypeSyntaxReference.From(subsetType);
                        }
                    }

                    return new()
                    {
                        Type = TypeSyntaxReference.From(tag),
                        ShortName = tag.Name,
                        SupersetReference = subsetTypeRef,
                        IsEnum = tag.TypeKind == TypeKind.Enum,
                    };
                }

                builder.Add(new()
                {
                    PayloadShortName = x.PayloadType?.Name,
                    Tag = GetTag(),
                    PayloadType = x.PayloadType is null ? null : TypeSyntaxReference.From(x.PayloadType),
                    AcceptedTagValues = GetConstants(),
                });
            }
            return new()
            {
                Methods = builder.ToImmutable(),
            };
        }
    }

    private readonly record struct TagInfo(
        HashSet<string> Values,
        bool IsEnum,
        string? BaseSet,
        string ShortName);

    private static void GenerateCachedPropertyInfos(ConfigAndModel p, IndentedTextWriter w)
    {
        w.WriteFileStart(nullableEnable: true);
        using var s1 = w.StartHierarchy(p.Model.ResultHierarchy, p.Model.ResultAccessibility);

        var resultTypeInfo = p.Model.ResultHierarchy.Hierarchy[^1];
        var tagTypeInfo = resultTypeInfo with
        {
            Name = resultTypeInfo.Name + "Tag",
        };
        w.WriteLine($"public required {resultTypeInfo.Name}Tag Tag {{ get; init; }}");
        w.WriteLine($"public global::System.Exception? Exception {{ get; init; }}");

        static void ComputePayloadNames(OverloadsInfo p)
        {
            var methods = p.Model.Methods;
            if (methods.Length == 0)
            {
                return;
            }

            var payloadNames = p.PayloadNames;

            bool IsMoreThanOnePayload()
            {
                bool isOnePayload = false;
                foreach (var m in methods)
                {
                    if (m.PayloadType is not { })
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

            bool isMoreThanOnePayload = IsMoreThanOnePayload();
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.PayloadType is not { })
                {
                    continue;
                }

                if (!isMoreThanOnePayload)
                {
                    payloadNames[i] = $"{p.DefaultPrefix}Payload";
                    continue;
                }

                if (m.Tag is not { })
                {
                    payloadNames[i] = $"{m.PayloadShortName}Payload";
                    continue;
                }

                // Allows all values
                if (m.AcceptedTagValues.IsEmpty)
                {
                    payloadNames[i] = $"{m.Tag.ShortName}Payload";
                    return;
                }

                payloadNames[i] = $"{m.AcceptedTagValues[0]}Payload";
            }
        }

        void WritePayloadFields(
            OverloadsInfo info,
            HashSet<string> alreadyWrittenPayloads)
        {
            var methods = info.Model.Methods;
            var payloadNames = info.PayloadNames;

            for (int i = 0; i < info.PayloadNames.Length; i++)
            {
                if (payloadNames[i] is not { } payloadName)
                {
                    continue;
                }
                if (!alreadyWrittenPayloads.Add(payloadName))
                {
                    continue;
                }
                var type = methods[i].PayloadType!;

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
                        list.Write($"{tag.Type} tag");
                    }

                    if (m.PayloadType is { } payloadType)
                    {
                        list.Write($"{payloadType} payload");
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
                            var tagType = tag.Type.FullyQualifiedName;
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

                    if (m.PayloadType is not null)
                    {
                        w.WriteLine($"{info.PayloadNames[i]} = payload,");
                    }

                    if (includeExceptionParam)
                    {
                        w.WriteLine("Exception = exception,");
                    }
                }
                w.WriteLine();
            }
        }

        using var allOverloadsContext = new AllOverloadsContext
        {
            Models = p.Model.OverloadsSets,
            SharedArray = new(p.Model),
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
            ComputePayloadNames(overloadInfo);
        }

        {
            var writtenPayloads = new HashSet<string>();
            foreach (var overloadsInfo in allOverloadsContext)
            {
                // HashSet pool?
                WritePayloadFields(overloadsInfo, writtenPayloads);
            }
        }

        foreach (var overloadsInfo in allOverloadsContext)
        {
            WriteConstructorFunctions(overloadsInfo);
        }

        {
            // closes the block of the result type.
            var block = new IndentedTextWriter.Block(w);
            block.Dispose();

            w.Write(SyntaxFacts.GetText(p.Model.ResultAccessibility));
            w.Write(" ");
            tagTypeInfo.WriteAsTypeDeclaration(w);
            w.WriteLine();

            // open a block for the tag type
            // we don't need to close it, the disposal
            // on the hierarchy will close it.
            // It is hacky, but that's what we have to do with the current abstractions.
            _ = w.WriteBlock();
        }

        // Constructor, for each of the supported types.
        // Checks for the supported tags of those types (asserts).
        // Using ResultBase.Create<T>(value) make try convert methods (with assertions too).
        // ResultSetsOf() for this type
        // Declare() that declares all the base sets this depends on
        //    - if enum, ResultBase.Declare<T>()
        //    - if tag, T.Declare()
        // explicit casts that assert that the value is not 0 ?
        // IsOk and IsFailure ?
        Dictionary<string, TagInfo> tags = new();
        AddForOverloads(p.Model.OverloadsSets.Ok, isOk: true);
        AddForOverloads(p.Model.OverloadsSets.Failure, isOk: false);

        void AddForOverloads(Model.Overloads overloads, bool isOk)
        {
            string genericTag = isOk ? "Ok" : "GenericFailure";

            void AddDefault()
            {
                var key = p.Config.WellKnownTypes.FullyQualifiedName;
                if (!tags.TryGetValue(key, out var v))
                {
                    v = new(Values: new(), IsEnum: true, BaseSet: null, ShortName: "WellKnown");
                    tags.Add(key, v);
                }
                v.Values.Add(genericTag);
            }
            foreach (var m in overloads.Methods)
            {
                if (m.Tag is not { } tagType)
                {
                    AddDefault();
                    continue;
                }

                if (!tags.TryGetValue(tagType.Type, out var list))
                {
                    list = new(
                        m.AcceptedTagValues.ToHashSet(),
                        IsEnum: tagType.IsEnum,
                        BaseSet: tagType.SupersetReference?.FullyQualifiedName,
                        ShortName: tagType.ShortName);
                    tags.Add(tagType.Type, list);
                    continue;
                }

                if (m.AcceptedTagValues.IsEmpty)
                {
                    list.Values.Clear();
                    continue;
                }

                foreach (var x in m.AcceptedTagValues)
                {
                    list.Values.Add(x);
                }
            }
        }

        w.WriteLine($"public {p.Config.ResultBase} Value {{ get; }}");
        w.WriteLine("public readonly bool IsNone => Value.IsNone;");

        WriteConstructors();
        WriteTryCreateWithCheck();
        WriteResultSets();
        WriteDeclares();
        WriteAsTags();
        WriteIsOk();

        // Constructors
        void WriteConstructors()
        {
            w.WriteLine($"private {tagTypeInfo.Name}({p.Config.ResultBase} tag) => Value = tag;");

            foreach (var t in tags)
            {
                w.WriteLine($"public {tagTypeInfo.Name}({t.Key} tag)");
                {
                    using var b = w.WriteBlock();
                    if (t.Value.Values.Count > 0)
                    {
                        w.Write($"global::{typeof(Debug).FullName!}.Assert(tag is ");
                        var list = w.List(separator: " or ");
                        foreach (var tagName in t.Value.Values)
                        {
                            list.Write($"{t.Key}.{tagName}");
                        }
                        w.WriteLine(");");
                    }

                    if (t.Value.IsEnum)
                    {
                        w.WriteLine($"Value = {p.Config.ResultBase}.Create<{t.Key}>(tag);");
                    }
                    else
                    {
                        w.WriteLine($"Value = tag.Value;");
                    }
                }
                w.WriteLine();
            }

        }

        void WriteTryCreateWithCheck()
        {
            w.WriteLine($"public static {tagTypeInfo.Name} TryCreateWithCheck({p.Config.ResultBase} value)");
            {
                using var b = w.WriteBlock();
                {
                    w.WriteLine($"var ret = new {tagTypeInfo.Name}(value);");
                    w.WriteLine("if (value.IsNone)");
                    using var b1 = w.WriteBlock();
                    w.WriteLine("return ret;");
                }
                foreach (var t in tags)
                {
                    using var b1 = w.WriteBlock();
                    w.WriteLine($"var r = ret.As<{t.Key}>();");

                    if (t.Value.IsEnum)
                    {
                        w.WriteLine($"if (r != ({t.Key}) 0)");
                    }
                    else
                    {
                        w.WriteLine("if (!r.IsNone)");
                    }

                    using var b2 = w.WriteBlock();
                    w.WriteLine("return ret;");
                }
                w.WriteLine($"return new({p.Config.ResultBase}.None);");
            }
        }

        // Result sets
        void WriteResultSets()
        {
            w.WriteLine($"public static readonly global::{typeof(ImmutableArray<>).Namespace!}.ImmutableArray<{p.Config.ResultSet}> ResultSets = [");
            w.IncreaseIndent();
            foreach (var t in tags)
            {
                void WriteValue()
                {
                    if (!t.Value.IsEnum)
                    {
                        Debug.Assert(t.Value.Values.Count == 0);
                        // This could duplicate the result sets.
                        // We can't solve this easily.
                        // Having recursive dependencies is a nightmare in source generators.
                        // Another option is computing this at runtime (removing duplicates).
                        // This option is doable, the annoying thing is just that it's more work at runtime.
                        w.Write($".. {t.Key}.ResultSets");
                        return;
                    }
                    w.Write($"{p.Config.ResultBase}.ResultSetOf<{t.Key}>(");
                    if (t.Value.Values.Count != 0)
                    {
                        w.Write("[");
                        var list = w.List(separator: ", ");
                        foreach (var x in t.Value.Values)
                        {
                            list.Write($"{t.Key}.{x}");
                        }
                        w.Write("]");
                    }
                    w.Write(")");
                }
                WriteValue();
                w.WriteLine(",");
            }
            w.DecreaseIndent();
            w.WriteLine("];");
        }

        // Declares
        void WriteDeclares()
        {
            w.WriteLine("public static void Declare()");
            {
                using var b1 = w.WriteBlock();
                foreach (var t in tags)
                {
                    if (!t.Value.IsEnum)
                    {
                        w.WriteLine($"{t.Key}.Declare();");
                        continue;
                    }
                    if (t.Value.BaseSet is { } baseSet)
                    {
                        w.WriteLine($"{p.Config.ResultBase}.DeclareSubset<{t.Key}, {baseSet}>();");
                    }
                    else
                    {
                        w.WriteLine($"{p.Config.ResultBase}.Declare<{t.Key}>();");
                    }
                }
            }
        }

        // As<Tag>
        void WriteAsTags()
        {
            foreach (var t in tags)
            {
                // TODO: This will cause conflicts.
                var postfix = t.Value.ShortName;

                w.WriteLine($"public readonly {t.Key} As{postfix}()");
                {
                    using var b = w.WriteBlock();
                    if (t.Value.IsEnum)
                    {
                        w.WriteLine($"return Value.As<{t.Key}>();");
                    }
                    else
                    {
                        w.WriteLine($"return {t.Key}.TryCreateWithCheck(Value);");
                    }
                }
                w.WriteLine();
            }

            {
                w.WriteLine($"public readonly T As<T>() where T : struct");
                {
                    using var b = w.WriteBlock();
                    foreach (var t in tags)
                    {
                        w.WriteLine($"if (typeof(T) == typeof({t.Key}))");
                        using var b1 = w.WriteBlock();
                        w.WriteLine($"return (T) (object) As{t.Value.ShortName}();");
                    }
                    w.WriteLine("throw new global::System.InvalidOperationException($\"Type {typeof(T).FullName!} is not allowed here\");");
                }
                w.WriteLine();
            }
        }

        // IsOk
        void WriteIsOk()
        {
            List<(string Tag, List<string> Values, bool IsEnum)> oks = new();

            void TryAdd(string tag, ReadOnlySpan<string> values, bool isEnum)
            {
                foreach (var x in oks)
                {
                    if (x.Tag != tag)
                    {
                        continue;
                    }
                    if (x.Values.Count == 0)
                    {
                        continue;
                    }

                    bool Contains(string value)
                    {
                        foreach (string v in x.Values)
                        {
                            if (v == value)
                            {
                                return true;
                            }
                        }
                        return false;
                    }

                    foreach (var v in values)
                    {
                        if (Contains(v))
                        {
                            continue;
                        }

                        x.Values.Add(v);
                    }
                    return;
                }

                oks.Add((tag, [.. values], isEnum));
            }

            foreach (var m in p.Model.OverloadsSets.Ok.Methods)
            {
                if (m.Tag is not { } tag)
                {
                    TryAdd(p.Config.WellKnownTypes, ["Ok"], isEnum: true);
                    continue;
                }

                TryAdd(
                    tag.Type.FullyQualifiedName,
                    m.AcceptedTagValues.AsSpan(),
                    isEnum: tag.IsEnum);
            }

            w.WriteLine($"public readonly bool IsOk");
            using var b = w.WriteBlock();
            w.WriteLine("get");
            using var b1 = w.WriteBlock();

            foreach (var ok in oks)
            {
                using var b2 = w.WriteBlock();
                {
                    w.WriteLine($"var r = As<{ok.Tag}>();");

                    if (ok.IsEnum)
                    {
                        w.WriteLine($"if (r != ({ok.Tag}) 0)");
                        using var b3 = w.WriteBlock();

                        if (ok.Values.Count == 0)
                        {
                            w.WriteLine("return true;");
                        }

                        foreach (var v in ok.Values)
                        {
                            w.WriteLine($"if (r == {ok.Tag}.{v})");
                            using var b4 = w.WriteBlock();
                            w.WriteLine("return true;");
                        }
                    }
                    else
                    {
                        w.WriteLine("if (!r.IsNone)");
                        using var b3 = w.WriteBlock();
                        w.WriteLine("return r.IsOk;");
                    }
                }
            }

            w.WriteLine("return false;");
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
    public Span<string?> PayloadNames => All.SharedArray.Array(Tag);
    public string DefaultPrefix => All.DefaultPrefixes.Ref(Tag);
    public string DefaultTag => All.DefaultTags.Ref(Tag);
}

internal sealed class AllOverloadsContext : IDisposable
{
    public required OneForEachOverloadSet<Model.Overloads> Models;
    public required SharedArrayForEachOverloadSet<string?> SharedArray;
    public required OneForEachOverloadSet<string> DefaultPrefixes;
    public required OneForEachOverloadSet<string> DefaultTags;

    public OverloadsInfo For(OverloadTag tag)
    {
        return new(this, tag);
    }

    public void Dispose()
    {
        SharedArray.Dispose();
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

internal readonly struct SharedArrayForEachOverloadSet<T> : IDisposable
{
    private readonly T[] _underlyingMemory;
    private readonly Model _model;

    public SharedArrayForEachOverloadSet(Model model)
    {
        _model = model;
        var len = model.OverloadsSets.Reduce((a, x) => a + x.Methods.Length, 0);
        _underlyingMemory = ArrayPool<T>.Shared.Rent(len);
    }

    public ArraySegment<T> Array(OverloadTag tag)
    {
        var start = tag switch
        {
            OverloadTag.Ok => 0,
            OverloadTag.Failure => _model.OverloadsSets.Ok.Methods.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(tag)),
        };
        var len = tag switch
        {
            OverloadTag.Ok => _model.OverloadsSets.Ok.Methods.Length,
            OverloadTag.Failure => _model.OverloadsSets.Failure.Methods.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(tag)),
        };
        return new(_underlyingMemory, start, len);
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_underlyingMemory!);
    }
}
