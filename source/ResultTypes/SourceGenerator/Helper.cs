using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Helpers;

namespace ResultTypes.SourceGenerator;

internal static class GetErrorValueHelper
{
    public enum ResultKind
    {
        Ok,
        ConstantNotInt,
        NoArgumentType,
    }

    public readonly record struct Result
    {
        public ResultKind ResultKind { get; init; }
        public ResultSetUsage Usage { get; init; }

        public static Result Failure(ResultKind failure)
        {
            Debug.Assert(failure != ResultKind.Ok);
            return new()
            {
                ResultKind = failure,
            };
        }

        public static Result Ok(ResultSetUsage usage)
        {
            return new()
            {
                ResultKind = ResultKind.Ok,
                Usage = usage,
            };
        }
    }

    public readonly record struct Params
    {
        public required ArgumentSyntax Argument { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static Result Get(Params p)
    {
        var constant = p.SemanticModel.GetConstantValue(p.Argument.Expression, p.CancellationToken);
        int? constValue = null;
        if (constant.HasValue)
        {
            if (constant.Value is not int i)
            {
                return Result.Failure(ResultKind.ConstantNotInt);
            }
            constValue = i;
        }

        var paramType = p.SemanticModel.GetTypeInfo(p.Argument.Expression, p.CancellationToken).Type;
        if (paramType is null)
        {
            return Result.Failure(ResultKind.NoArgumentType);
        }

        return Result.Ok(new()
        {
            Type = paramType,
            ConstValue = constValue,
        });
    }
}

internal struct OverloadBuilder() : IDisposable
{
    public ITypeSymbol? TagType { get; set; }
    public ITypeSymbol? PayloadType { get; set; }
    public ImmutableArrayBuilder<int> Constants { get; init; } = ImmutableArrayBuilder<int>.Rent();

    public void Dispose()
    {
        Constants.Dispose();
    }
}

internal struct ResultSetsBuilder() : IDisposable
{
    public ImmutableArrayBuilder<OverloadBuilder> Values = ImmutableArrayBuilder<OverloadBuilder>.Rent();

    public void Dispose()
    {
        Values.Dispose();
    }
}


internal readonly record struct ResultSetUsage(ITypeSymbol Type, int? ConstValue);

[Flags]
internal enum ArgKinds
{
    None,
    Payload = 1 << 0,
    Const = 1 << 1,
    Tag = 1 << 2,
    TagOrPayload = Tag | Payload,
    Exception = 1 << 3,
}

internal enum FindArgKindFailure
{
    None,
    ConstantNotInt,
    NoArgumentType,
    NoMatch,
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
        public ITypeSymbol Type { get; init; }
        public int? ConstValue { get; init; }
    }

    public readonly record struct FindArgKindResult
    {
        public FindArgKindFailure Failure { get; init; }
        public ArgInfo ArgInfo { get; init; }
    }

    public static FindArgKindResult MatchArg(in FindArgKindParams p)
    {
        var type = p.SemanticModel.GetTypeInfo(p.Argument.Expression).Type;
        if (type is null)
        {
            return new()
            {
                Failure = FindArgKindFailure.NoArgumentType,
            };
        }

        if ((p.PossibleKinds & ArgKinds.Const) != 0)
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

        if ((p.PossibleKinds & ArgKinds.Exception) != 0)
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

        if ((p.PossibleKinds & ArgKinds.Payload) != 0)
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

        if ((p.PossibleKinds & ArgKinds.Tag) != 0)
        {
            bool includePayload = type.TypeKind != TypeKind.Enum && (p.PossibleKinds & ArgKinds.Tag) != 0;
            var kind = ArgKinds.Tag;
            if (includePayload)
            {
                kind |= ArgKinds.Payload;
            }

            return new()
            {
                ArgInfo = new()
                {
                    Kind = kind,
                    Type = type,
                },
            };
        }

        if ((p.PossibleKinds & ArgKinds.Payload) != 0)
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
        public required ITypeSymbol? PayloadType { get; init; }
        public required ITypeSymbol? TagType { get; init; }
        public required int? ConstValue { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static void AddOrUpdateOverload(
        AddOrUpdateOverloadParams p,
        ref ResultSetsBuilder builder)
    {
        bool UpdateOverload(ref ResultSetsBuilder b)
        {
            var builders = b.Values.WrittenSpan;
            for (int i = 0; i < builders.Length; i++)
            {
                if (p.PayloadType is { } requiredPayload)
                {
                    if (builders[i].PayloadType is not { } payloadType)
                    {
                        continue;
                    }
                    if (!payloadType.Equals(requiredPayload, SymbolEqualityComparer.Default))
                    {
                        continue;
                    }
                }
                if (p.TagType is { } requiredTag)
                {
                    if (builders[i].TagType is not { } tagType)
                    {
                        continue;
                    }
                    if (!tagType.Equals(requiredTag, SymbolEqualityComparer.Default))
                    {
                        continue;
                    }
                    if (builders[i].Constants.Count == 0)
                    {
                        return true;
                    }
                    if (p.ConstValue is { } constValue)
                    {
                        foreach (var it in builders[i].Constants.WrittenSpan)
                        {
                            if (it == constValue)
                            {
                                return true;
                            }
                        }
                        builders[i].Constants.Add(constValue);
                        return true;
                    }
                    else
                    {
                        builders[i].Constants.Clear();
                        return true;
                    }
                }
            }
            return false;
        }

        if (!UpdateOverload(ref builder))
        {
            builder.Values.Add(new()
            {
                PayloadType = p.PayloadType,
                TagType = p.TagType,
            });

            if (p.ConstValue is { } i)
            {
                builder.Values.WrittenSpan[^1].Constants.Add(i);
            }
        }
    }
}
