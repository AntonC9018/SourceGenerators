using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
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
                            state.ResultSets.Values.Add(new()
                            {
                                PayloadType = null,
                                TagType = null,
                            });
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

        var subsetAttribute = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(SubsetAttribute).FullName!)!;
        var genericSubsetAttribute = context.SemanticModel.Compilation
            .GetTypeByMetadataName(typeof(SubsetAttribute<>).FullName!)!;

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

        return new()
        {
            ResultHierarchy = GetResultHierarchy(),
            OkOverloads = ConvertToModel(okState),
            FailureMethods = ConvertToModel(failureState),
        };
    }

    private readonly record struct TagInfo(
        HashSet<string> Values,
        bool IsEnum,
        string? BaseSet);

    private static void GenerateCachedPropertyInfos(ConfigAndModel p, IndentedTextWriter w)
    {
        w.WriteFileStart(nullableEnable: true);
        using var s1 = w.StartHierarchy(p.Model.ResultHierarchy);

        var resultTypeInfo = p.Model.ResultHierarchy.Hierarchy[^1];
        var tagTypeInfo = resultTypeInfo with
        {
            Name = resultTypeInfo.Name + "Tag",
        };
        w.WriteLine($"public required {resultTypeInfo.Name}Tag Tag {{ get; init; }}");
        w.WriteLine($"public Exception? Exception {{ get; init; }}");

        void WritePayloadFieldName(string defaultPrefix, Model.MethodModel m)
        {
            if (m.PayloadType is { } payloadType)
            {
                w.Write(payloadType);
                return;
            }

            if (m.Tag is not { })
            {
                w.Write($"{defaultPrefix}Payload");
                return;
            }

            // Allows all values
            if (m.AcceptedTagValues.IsEmpty)
            {
                w.Write($"{m.Tag.ShortName}Payload");
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
            foreach (var m in overloads.Methods)
            {
                w.Write($"public static {resultTypeInfo.Name} {defaultPrefix}(");
                {
                    var list = w.List();

                    if (m.Tag is { } tag)
                    {
                        w.Write($"{tag.Type} tag");
                    }

                    if (m.PayloadType is { } payloadType)
                    {
                        w.Write($"{payloadType} payload");
                    }

                    if (!includeExceptionParam)
                    {
                        list.Write("Exception? exception = null");
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
                            w.Write("Debug.Assert(tag is ");
                            var list = w.List(separator: " or ");
                            foreach (var tagName in m.AcceptedTagValues)
                            {
                                list.Write($"{tagType}.{tagName}");
                            }
                        }
                    }
                    else
                    {
                        w.WriteLine($"var tag = {p.Config.WellKnownTypes}.{defaultPrefix};");
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

            tagTypeInfo.WriteAsTypeDeclaration(w);

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
        AddForOverloads(p.Model.OkOverloads, isOk: true);
        AddForOverloads(p.Model.FailureMethods, isOk: false);

        void AddForOverloads(Model.Overloads overloads, bool isOk)
        {
            string genericTag = isOk ? "Ok" : "GenericFailure";

            void AddDefault()
            {
                var key = p.Config.WellKnownTypes.FullyQualifiedName;
                if (!tags.TryGetValue(key, out var v))
                {
                    v = new(Values: new(), IsEnum: true, BaseSet: null);
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
                        BaseSet: tagType.SupersetReference?.FullyQualifiedName);
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

        w.WriteLine("public ResultBase Value { get; }");
        w.WriteLine("public bool IsNone => Value.IsNone;");

        WriteConstructors();
        WriteTryCreateWithCheck();
        WriteResultSets();
        WriteDeclares();
        WriteAsTags();
        WriteIsOk();

        // Constructors
        void WriteConstructors()
        {
            w.Write($"private {tagTypeInfo.Name}(ResultBase tag) => Value = tag;");

            foreach (var t in tags)
            {
                w.Write($"public {tagTypeInfo.Name}({t.Key} tag)");
                {
                    using var b = w.WriteBlock();
                    if (t.Value.Values.Count > 0)
                    {
                        w.Write($"Debug.Assert(tag is ");
                        var list = w.List(separator: " or ");
                        foreach (var tagName in t.Value.Values)
                        {
                            list.Write($"{t.Key}.{tagName}");
                        }
                        w.WriteLine(");");
                    }

                    if (t.Value.IsEnum)
                    {
                        w.WriteLine($"Value = ResultBase.Convert<{t.Key}>(tag);");
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
            w.WriteLine($"public static {tagTypeInfo.Name} TryCreateWithCheck(ResultBase value)");
            {
                using var b = w.WriteBlock();
                {
                    w.WriteLine("var ret = new(value);");
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
                        w.WriteLine($"if ((int) r != 0)");
                    }
                    else
                    {
                        w.WriteLine("if (!r.IsNone)");
                    }

                    using var b2 = w.WriteBlock();
                    w.WriteLine("return ret;");
                }
                w.WriteLine("return new(ResultBase.None);");
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
                        w.WriteLine($".. {t.Value}.ResultSets");
                        return;
                    }
                    w.Write($"ResultBase.ResultSetOf<t.Key>(");
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
                        w.WriteLine($"ResultBase.DeclareSubset<{t.Key}, {baseSet}>();");
                    }
                }
            }
        }

        // As<Tag>
        void WriteAsTags()
        {
            foreach (var t in tags)
            {
                w.WriteLine($"public readonly {t.Key} As{t.Key}()");
                {
                    using var b = w.WriteBlock();
                    if (t.Value.IsEnum)
                    {
                        w.WriteLine($"return ResultBase.As<{t.Key}>(Value);");
                    }
                    else
                    {
                        w.WriteLine($"return {t.Key}.TryCreateWithCheck(Value);");
                    }
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

            foreach (var m in p.Model.OkOverloads.Methods)
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

            w.WriteLine($"public bool IsOk");
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
                        w.WriteLine("if ((int) r != 0)");
                        using var b3 = w.WriteBlock();

                        if (ok.Values.Count == 0)
                        {
                            w.WriteLine("return true;");
                        }

                        foreach (var v in ok.Values)
                        {
                            w.WriteLine($"if (r == {ok.Tag}.{v}");
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

            w.WriteLine("return false");
        }

    }
}

