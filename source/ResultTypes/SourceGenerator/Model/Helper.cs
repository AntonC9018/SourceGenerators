using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Helpers;

namespace ResultTypes.SourceGenerator;

internal struct OverloadBuilder() : IDisposable
{
    public TypeThatMayBeAssociatedWithResultType Tag { get; set; }
    public TypeThatMayBeAssociatedWithResultType Payload { get; set; }
    public ImmutableArrayBuilder<int> Constants = ImmutableArrayBuilder<int>.Rent();

    public void Dispose()
    {
        Constants.Dispose();
    }
}

internal struct ResultSetsBuilder() : IDisposable
{
    public ImmutableArrayBuilder<OverloadBuilder> Values = ImmutableArrayBuilder<OverloadBuilder>.Rent();
    public ImmutableArrayBuilder<ITypeSymbol> PassedAlongResults = ImmutableArrayBuilder<ITypeSymbol>.Rent();

    public void Dispose()
    {
        Values.Dispose();
    }
}

[Flags]
internal enum ArgKinds
{
    None,
    Payload = 1 << 0,
    Const = 1 << 1,
    EnumTag = 1 << 2,
    ResultTag = 1 << 5,
    ResultPayload = 1 << 7,
    Result = 1 << 6,
    Exception = 1 << 3,

    AllTags = EnumTag | ResultTag,
    AllPayloads = Payload | ResultPayload,
}

internal static class ArgKindsHelper
{
    public static bool HasAllOf(this ArgKinds kinds, ArgKinds required)
    {
        return (kinds & required) == required;
    }

    public static bool HasEitherOf(this ArgKinds kinds, ArgKinds required)
    {
        return (kinds & required) != 0;
    }

    public static bool HasNeitherOf(this ArgKinds kinds, ArgKinds required)
    {
        return (kinds & required) == 0;
    }
}

internal enum FindArgKindFailure
{
    None,
    ConstantNotInt,
    NoArgumentType,
    NoMatch,
}

public readonly record struct TypeThatMayBeAssociatedWithResultType
{
    public required ITypeSymbol? Type { get; init; }
    public required ITypeSymbol? AssociatedResultType { get; init; }
}


internal static class Helper
{

    public readonly record struct FindArgKindParams
    {
        public required ArgumentSyntax Argument { get; init; }
        public required ArgKinds PossibleKinds { get; init; }
        public required INamedTypeSymbol ExceptionSymbol { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public readonly record struct ArgInfo
    {
        public ArgKinds Kind { get; init; }
        public ITypeSymbol? Type { get; init; }
        public ITypeSymbol? AssociatedResultType { get; init; }
        public int? ConstValue { get; init; }
    }

    public readonly record struct FindArgKindResult
    {
        public FindArgKindFailure Failure { get; init; }
        public ArgInfo ArgInfo { get; init; }
    }

    public static FindArgKindResult MatchArg(FindArgKindParams p)
    {
        (INamedTypeSymbol ResultType, INamedTypeSymbol? Type)? TryExtractResultTypeOfTagOrPayload(bool tag)
        {
            if (p.Argument.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return null;
            }

            if (tag)
            {
                if (memberAccess.Name.Identifier.Text != "Tag")
                {
                    return null;
                }
            }
            // Payload
            else
            {
                if (!memberAccess.Name.Identifier.Text.EndsWith("Payload"))
                {
                    return null;
                }
            }

            var maybeResultType = p.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (maybeResultType is not INamedTypeSymbol resultType)
            {
                return null;
            }

            var valueType = p.SemanticModel.GetTypeInfo(p.Argument.Expression).Type as INamedTypeSymbol;
            if (valueType?.TypeKind == TypeKind.Error)
            {
                valueType = null;
            }

            return (resultType, valueType);
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.ResultTag))
        {
            if (TryExtractResultTypeOfTagOrPayload(tag: true) is { } resultType1)
            {
                return new()
                {
                    ArgInfo = new()
                    {
                        Kind = ArgKinds.ResultTag,
                        Type = resultType1.Type,
                        AssociatedResultType = resultType1.ResultType,
                    },
                };
            }
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.ResultPayload))
        {
            if (TryExtractResultTypeOfTagOrPayload(tag: false) is { } resultType1)
            {
                return new()
                {
                    ArgInfo = new()
                    {
                        Kind = ArgKinds.ResultPayload,
                        Type = resultType1.Type,
                        AssociatedResultType = resultType1.ResultType,
                    },
                };
            }
        }

        var type = p.SemanticModel.GetTypeInfo(p.Argument.Expression).Type;
        if (type is null)
        {
            return new()
            {
                Failure = FindArgKindFailure.NoArgumentType,
            };
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.Const))
        {
            var constant = p.SemanticModel.GetConstantValue(p.Argument.Expression, p.CancellationToken);
            if (constant.HasValue)
            {
                if (constant.Value is not int i)
                {
                    return new()
                    {
                        Failure = FindArgKindFailure.ConstantNotInt,
                        ArgInfo = new()
                        {
                            Type = type,
                        },
                    };
                }
                return new()
                {
                    ArgInfo = new()
                    {
                        Kind = ArgKinds.Const,
                        Type = type,
                        ConstValue = i,
                    },
                };
            }
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.Exception))
        {
            bool IsException(INamedTypeSymbol exceptionType, ITypeSymbol typeSymbol)
            {
                while (true)
                {
                    if (typeSymbol.BaseType is not { } baseType)
                    {
                        break;
                    }

                    if (!baseType.Equals(exceptionType, SymbolEqualityComparer.Default))
                    {
                        typeSymbol = baseType;
                        continue;
                    }

                    return true;
                }
                return false;
            }
            if (IsException(p.ExceptionSymbol, type))
            {
                return new()
                {
                    ArgInfo = new()
                    {
                        Kind = ArgKinds.Exception,
                        Type = type,
                    },
                };
            }
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.Payload))
        {
            bool isCertainlyPayload = p.Argument.Expression is ObjectCreationExpressionSyntax;
            if (isCertainlyPayload)
            {
                return new()
                {
                    ArgInfo = new()
                    {
                        Kind = ArgKinds.Payload,
                        Type = type,
                    },
                };
            }
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.EnumTag) && type.TypeKind == TypeKind.Enum)
        {
            return new()
            {
                ArgInfo = new()
                {
                    Kind = ArgKinds.EnumTag,
                    Type = type,
                },
            };
        }

        if (p.PossibleKinds.HasAllOf(ArgKinds.Payload | ArgKinds.Result))
        {
            // We got ourselves a tie, need to do more convention-based rules.
            ArgKinds DetermineKind()
            {
                if (type.Name.EndsWith("Result"))
                {
                    return ArgKinds.Result;
                }
                if (type.Name.EndsWith("Payload"))
                {
                    return ArgKinds.Payload;
                }

                // If it's generated, it's probably a result.
                if (type.TypeKind == TypeKind.Error)
                {
                    return ArgKinds.Result;
                }

                return ArgKinds.Payload;
            }

            return new()
            {
                ArgInfo = new()
                {
                    Kind = DetermineKind(),
                    Type = type,
                },
            };
        }

        var payloadOrResult = p.PossibleKinds & (ArgKinds.Payload | ArgKinds.Result);
        if (payloadOrResult != 0)
        {
            return new()
            {
                ArgInfo = new()
                {
                    Kind = payloadOrResult,
                    Type = type,
                },
            };
        }

        return new()
        {
            Failure = FindArgKindFailure.NoMatch,
            ArgInfo = new()
            {
                Type = type,
            },
        };
    }

    public readonly record struct AddOrUpdateOverloadParams
    {
        public required TypeThatMayBeAssociatedWithResultType Payload { get; init; }
        public required TypeThatMayBeAssociatedWithResultType Tag { get; init; }
        public required int? ConstValue { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static void AddResultOverload(
        ITypeSymbol resultType,
        ref ResultSetsBuilder builder)
    {
        ref var resultBuilders = ref builder.PassedAlongResults;
        var results = resultBuilders.WrittenSpan;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].Equals(resultType, SymbolEqualityComparer.Default))
            {
                return;
            }
        }

        resultBuilders.Add(resultType);
        return;
    }

    public static void AddOrUpdateOverload(
        AddOrUpdateOverloadParams p,
        ref ResultSetsBuilder builder)
    {
        if (!UpdateOverload(ref builder))
        {
            builder.Values.Add(new()
            {
                Tag = p.Tag,
                Payload = p.Payload,
            });

            if (p.ConstValue is { } i)
            {
                builder.Values.WrittenSpan[^1].Constants.Add(i);
            }
        }

        bool UpdateOverload(ref ResultSetsBuilder resultSetBuilder)
        {
            var builders = resultSetBuilder.Values.WrittenSpan;
            foreach (ref var b in builders)
            {
                if (!IsSame(p.Payload, b.Payload))
                {
                    continue;
                }
                if (!IsSame(p.Tag, b.Tag))
                {
                    continue;
                }

                b.Payload = TakeNonNulls(b.Payload, p.Payload);
                b.Tag = TakeNonNulls(b.Tag, p.Tag);

                UpdateConstants(ref b.Constants, p.ConstValue);
                return true;
            }
            return false;
        }

        static void UpdateConstants(
            ref ImmutableArrayBuilder<int> constants,
            int? maybeConstValue)
        {
            if (constants.Count == 0)
            {
                return;
            }
            if (maybeConstValue is { } constValue)
            {
                foreach (var it in constants.WrittenSpan)
                {
                    if (it == constValue)
                    {
                        return;
                    }
                }
                constants.Add(constValue);
            }
            else
            {
                constants.Clear();
            }
        }

        static bool IsSame(
            TypeThatMayBeAssociatedWithResultType a,
            TypeThatMayBeAssociatedWithResultType b)
        {
            if (a.Type is null && b.Type is null)
            {
                return SymbolEqualityComparer.Default.Equals(a.AssociatedResultType, b.AssociatedResultType);
            }

            static bool ShouldCompareResults1(
                TypeThatMayBeAssociatedWithResultType a,
                TypeThatMayBeAssociatedWithResultType b)
            {
                if (a.Type is null
                    && b.Type is not null
                    && a.AssociatedResultType is not null)
                {
                    return true;
                }
                return false;
            }

            if (ShouldCompareResults1(a, b) || ShouldCompareResults1(b, a))
            {
                return SymbolEqualityComparer.Default.Equals(a.AssociatedResultType, b.AssociatedResultType);
            }

            return SymbolEqualityComparer.Default.Equals(a.Type, b.Type);
        }

        static TypeThatMayBeAssociatedWithResultType TakeNonNulls(
            TypeThatMayBeAssociatedWithResultType a,
            TypeThatMayBeAssociatedWithResultType b)
        {
            return new()
            {
                Type = a.Type ?? b.Type,
                AssociatedResultType = a.AssociatedResultType ?? b.AssociatedResultType,
            };
        }

    }
}
