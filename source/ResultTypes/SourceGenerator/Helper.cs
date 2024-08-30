using System;
using System.Diagnostics;
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
                // TODO: Warning.
                return Result.Failure(ResultKind.ConstantNotInt);
            }
            constValue = i;
        }

        var paramType = p.SemanticModel.GetTypeInfo(p.Argument.Expression, p.CancellationToken).Type;
        if (paramType is null)
        {
            // TODO: Warning.
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
    public required ITypeSymbol? Type { get; init; }
    public bool IsWithoutExplicitResult => true;
    public ImmutableArrayBuilder<int> Constants { get; init; } = ImmutableArrayBuilder<int>.Rent();
    public ImmutableArrayBuilder<IncompleteProperty> Properties = ImmutableArrayBuilder<IncompleteProperty>.Rent();

    public void Dispose()
    {
        Constants.Dispose();
        Properties.Dispose();
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

internal readonly record struct IncompleteProperty
{
    public required ITypeSymbol Type { get; init; }
    public required string Name { get; init; }
}


internal readonly record struct ResultSetUsage(ITypeSymbol Type, int? ConstValue);

internal static class AddPropertyHelper
{
    private static int FindPropertyIndex(
        ReadOnlySpan<IncompleteProperty> properties,
        string name)
    {
        for (int index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            if (property.Name == name)
            {
                return index;
            }
        }
        return -1;
    }

    public enum ResultKind
    {
        Ok,
        TypeConflict,
    }

    public readonly record struct Result
    {
        public ResultKind ResultKind { get; init; }
        public ITypeSymbol? ExistingType { get; init; }

        public static Result Ok() => new()
        {
            ResultKind = ResultKind.Ok,
        };

        public static Result TypeConflict(ITypeSymbol existingType) => new()
        {
            ResultKind = ResultKind.TypeConflict,
            ExistingType = existingType,
        };
    }

    public static Result Add(
        ref ImmutableArrayBuilder<IncompleteProperty> builder,
        IncompleteProperty p)
    {
        var index = FindPropertyIndex(builder.WrittenSpan, p.Name);
        if (index == -1)
        {
            builder.Add(p);
            return Result.Ok();
        }

        var existing = builder.WrittenSpan[index];
        if (existing.Type.Equals(p.Type, SymbolEqualityComparer.Default))
        {
            return Result.TypeConflict(existing.Type);
        }

        return Result.Ok();
    }
}

internal static class Helper
{

    public readonly record struct HandleNewParams
    {
        public required ObjectCreationExpressionSyntax NewTypeSyntax { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public static bool HandleNew(
        HandleNewParams p,
        ref ImmutableArrayBuilder<IncompleteProperty> builder)
    {
        if (p.NewTypeSyntax.Initializer is not { } initializer)
        {
            return false;
        }

        foreach (var prop in initializer.Expressions)
        {
            if (prop is not AssignmentExpressionSyntax assignment)
            {
                // TODO: warning.
                return false;
            }

            // Try to find the property
            var name = assignment.Left.ToString();
            var type = p.SemanticModel.GetTypeInfo(assignment.Left, p.CancellationToken).Type;
            if (type is null)
            {
                // TODO: warning.
                return false;
            }

            var result = AddPropertyHelper.Add(ref builder, new()
            {
                Name = name,
                Type = type,
            });

            if (result.ResultKind != AddPropertyHelper.ResultKind.Ok)
            {
                break;
            }
        }
        return true;
    }

    public static ref OverloadBuilder HandleConstant(
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
            return ref Unsafe.NullRef<OverloadBuilder>();
        }
        ref var overloadBuilder = ref AddForType(ref builder, result.Usage);
        return ref overloadBuilder;
    }


    public static ref OverloadBuilder AddForType(
        ref ResultSetsBuilder builder,
        ResultSetUsage p)
    {
        foreach (ref var r in builder.Values.WrittenSpan)
        {
            if (r.Type is null)
            {
                continue;
            }
            if (!r.Type.Equals(p.Type, SymbolEqualityComparer.Default))
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
            return ref r;
        }

        var b = new OverloadBuilder
        {
            Type = p.Type,
        };
        {
            if (p.ConstValue is { } i)
            {
                b.Constants.Add(i);
            }
        }
        builder.Values.Add(b);
        return ref builder.Values.WrittenSpan[^1];
    }
}
