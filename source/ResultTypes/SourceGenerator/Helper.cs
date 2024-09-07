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

internal static class Helper
{
    public static int? HandleConstant(
        GetErrorValueHelper.Params p,
        ref ResultSetsBuilder builder)
    {
        var result = GetErrorValueHelper.Get(new()
        {
            Argument = p.Argument,
            CancellationToken = p.CancellationToken,
            SemanticModel = p.SemanticModel,
        });
        if (result.ResultKind != GetErrorValueHelper.ResultKind.Ok)
        {
            return null;
        }
        var overloadBuilder = AddForType(ref builder, result.Usage);
        return overloadBuilder;
    }


    public static int AddForType(
        ref ResultSetsBuilder builder,
        ResultSetUsage p)
    {
        var builders = builder.Values.WrittenSpan;
        for (int index = 0; index < builders.Length; index++)
        {
            var r = builders[index];
            if (r.TagType is null)
            {
                continue;
            }

            if (!r.TagType.Equals(p.Type, SymbolEqualityComparer.Default))
            {
                continue;
            }

            if (r.Constants.Count == 0)
            {
                continue;
            }

            if (p.ConstValue is { } i)
            {
                r.Constants.Add(i);
            }
            else
            {
                r.Constants.Clear();
            }

            return index;
        }

        var b = new OverloadBuilder
        {
            TagType = p.Type,
        };
        {
            if (p.ConstValue is { } i)
            {
                b.Constants.Add(i);
            }
        }
        builder.Values.Add(b);
        return builders.Length - 1;
    }

    public readonly record struct HandleArgumentTypeParams
    {
        public required ExpressionSyntax NewTypeSyntax { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static void HandleArgumentType(
        HandleArgumentTypeParams p,
        ref ResultSetsBuilder builder)
    {
        var type = p.SemanticModel.GetTypeInfo(p.NewTypeSyntax).Type;
        bool HasCorrespondingOverload(ref ResultSetsBuilder b)
        {
            var builders = b.Values.WrittenSpan;
            for (int i = 0; i < builders.Length; i++)
            {
                if (builders[i].PayloadType is not { } payloadType)
                {
                    continue;
                }
                if (builders[i].TagType is not null)
                {
                    continue;
                }
                if (!payloadType.Equals(type, SymbolEqualityComparer.Default))
                {
                    continue;
                }
                if (builders[i].Constants.Count == 0)
                {
                    return true;
                }
            }
            return false;
        }

        if (!HasCorrespondingOverload(ref builder))
        {
            builder.Values.Add(new()
            {
                PayloadType = type,
            });
        }
    }
}
