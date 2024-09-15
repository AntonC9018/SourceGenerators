using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ResultTypes.Shared;
using SourceGeneration.Helpers;
using SourceGeneration.Models;
using TypeInfo = Microsoft.CodeAnalysis.TypeInfo;

namespace ResultTypes.SourceGenerator;

internal static class CreateModelHelper
{
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

    private struct AddOrUpdateOverloadParams
    {
        public Helper.ArgInfo? Tag { get; init; }
        public Helper.ArgInfo? Payload { get; init; }
    }

    public static Model? CreateModel(
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

                var imports = ImmutableArrayBuilder<string>.Rent();
                var root = context.TargetSyntax.SyntaxTree.GetRoot(cancellationToken);
                foreach (var usingStatement in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    imports.Add(usingStatement.NamespaceOrType.ToString());
                }

                return new()
                {
                    Imports = imports.ToImmutable(),

                    ResultAccessibility = returnType.Kind == SymbolKind.ErrorType
                        ? Accessibility.Public
                        : returnType.DeclaredAccessibility,

                    ResultHierarchy = GetResultHierarchy(
                        returnType,
                        context.TargetSymbol.ContainingType),

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

        void TryAddResultTypeOverload(ITypeSymbol resultType, ref ResultSetsBuilder b)
        {
            Helper.AddResultOverload(resultType, ref b);
        }

        bool AddOrUpdateOverload(AddOrUpdateOverloadParams p, ref ResultSetsBuilder b)
        {
            var payloadResultType = p.Payload?.AssociatedResultType;
            var tagResultType = p.Tag?.AssociatedResultType;

            if (payloadResultType != null && tagResultType != null)
            {
                if (!tagResultType.Equals(payloadResultType, SymbolEqualityComparer.Default))
                {
                    return false;
                }
            }

            var tagType = p.Tag?.Type;
            var constVal = p.Tag?.ConstValue;
            var payloadType = p.Payload?.Type;

            Helper.AddOrUpdateOverload(new()
            {
                CancellationToken = cancellationToken,
                SemanticModel = context.SemanticModel,
                ConstValue = constVal,
                Payload = new()
                {
                    Type = payloadType,
                    AssociatedResultType = payloadResultType,
                },
                Tag = new()
                {
                    Type = tagType,
                    AssociatedResultType = tagResultType,
                },
            }, ref b);

            return true;
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

                {
                    if (memberAccess.Expression is not IdentifierNameSyntax returnTypeIdentifier)
                    {
                        continue;
                    }
                    if (returnTypeIdentifier.Identifier.Text != returnType.Name)
                    {
                        continue;
                    }
                }

                switch (memberAccess.Name.Identifier.Text)
                {
                    case "Failure":
                    {
                        ProcessInvocationExpression(
                            ref states.Failure,
                            invocation,
                            exceptionIsPayload: false);
                        break;
                    }

                    case "Ok":
                    {
                        ProcessInvocationExpression(
                            ref states.Ok,
                            invocation,
                            exceptionIsPayload: true);
                        break;
                    }
                }
            }
        }

        void ProcessInvocationExpression(
            ref State state,
            InvocationExpressionSyntax invocation,
            bool exceptionIsPayload)
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
                    Payload = default,
                    Tag = default,
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
                    var kinds = ArgKinds.Const
                        | ArgKinds.AllTags
                        | ArgKinds.AllPayloads
                        | ArgKinds.Result;
                    if (!exceptionIsPayload)
                    {
                        kinds |= ArgKinds.Exception;
                    }

                    var result = MatchArg(x, kinds);
                    if (result.Failure != FindArgKindFailure.None)
                    {
                        break;
                    }

                    if (result.ArgInfo.Kind == ArgKinds.Result)
                    {
                        TryAddResultTypeOverload(result.ArgInfo.Type, ref state.ResultSets);
                    }
                    else
                    {
                        bool isPayload = result.ArgInfo.Kind.HasEitherOf(ArgKinds.AllPayloads);
                        bool isException = result.ArgInfo.Kind == ArgKinds.Exception;
                        bool isTag = !isPayload && !isException;

                        AddOrUpdateOverload(new()
                        {
                            Tag = isTag ? result.ArgInfo : null,
                            Payload = isPayload ? result.ArgInfo : null,
                        }, ref state.ResultSets);
                    }

                    break;
                }
                case [{ } x, { } x1]:
                {
                    var kinds = ArgKinds.Const | ArgKinds.AllTags;

                    var result1 = MatchArg(x, kinds);
                    if (result1.Failure != FindArgKindFailure.None)
                    {
                        break;
                    }

                    var nextKinds = ArgKinds.AllPayloads;
                    if (!exceptionIsPayload)
                    {
                        nextKinds |= ArgKinds.Exception;
                    }

                    var result2 = MatchArg(x1, nextKinds);
                    if (result2.Failure != FindArgKindFailure.None)
                    {
                        break;
                    }

                    Helper.ArgInfo? tag;
                    Helper.ArgInfo? payload;

                    bool firstMustBeTag = result1.ArgInfo.Kind.HasEitherOf(ArgKinds.AllTags | ArgKinds.Const);
                    bool isSecondException = result2.ArgInfo.Kind == ArgKinds.Exception;
                    bool firstMustBePayload = result1.ArgInfo.Kind.HasEitherOf(ArgKinds.AllPayloads);

                    // tag, exception
                    if (firstMustBeTag)
                    {
                        tag = result1.ArgInfo;
                        payload = isSecondException ? null : result2.ArgInfo;
                    }
                    // payload, exception
                    else if (firstMustBePayload)
                    {
                        if (!isSecondException)
                        {
                            // TODO: warning
                            break;
                        }
                        tag = null;
                        payload = result1.ArgInfo;
                    }
                    // tag, payload
                    else
                    {
                        tag = result1.ArgInfo;
                        payload = result2.ArgInfo;
                    }

                    AddOrUpdateOverload(new()
                    {
                        Tag = tag,
                        Payload = payload,
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

                    var result = MatchArg(x, ArgKinds.Const | ArgKinds.EnumTag);
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

                    AddOrUpdateOverload(new()
                    {
                        Tag = result.ArgInfo,
                        Payload = result1.ArgInfo,
                    }, ref state.ResultSets);
                    break;
                }
            }
        }

        // If a type is already defined, we must respect its positioning.
        static HierarchyInfo GetResultHierarchy(
            INamedTypeSymbol returnType,
            INamedTypeSymbol containingType)
        {
            if (!returnType.DeclaringSyntaxReferences.IsEmpty)
            {
                return HierarchyInfo.From(returnType);
            }

            // It's not clear what to do with a default failure case?
            // Maybe it should just create like a SomeFailure member?
            INamespaceOrTypeSymbol containerForHierarchy = containingType;
            if (returnType.Name != "Result")
            {
                containerForHierarchy = (INamespaceOrTypeSymbol) containerForHierarchy.ContainingSymbol;
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
            using var builder = ImmutableArrayBuilder<Model.Method>.Rent();
            foreach (var x in state.ResultSets.Values.WrittenSpan)
            {
                builder.Add(new()
                {
                    Tag = GetTag(x.Tag),
                    Payload = GetPayload(x.Payload),
                    AcceptedTagValues = GetConstants(x.Constants.WrittenSpan, x.Tag.Type),
                });
            }

            using var passAlongBuilder = ImmutableArrayBuilder<TypeSyntaxReference>.Rent();
            foreach (var x in state.ResultSets.PassedAlongResults.WrittenSpan)
            {
                var reference = TypeSyntaxReference.From(x);
                passAlongBuilder.Add(reference);
            }

            return new()
            {
                PassAlongResults = passAlongBuilder.ToImmutable(),
                Methods = builder.ToImmutable(),
            };
        }

        ImmutableArray<string> GetConstants(
            ReadOnlySpan<int> constants,
            ITypeSymbol? tagType)
        {
            if (constants.Length == 0)
            {
                return [];
            }
            Debug.Assert(tagType is not null);

            using var acceptedTagValues = ImmutableArrayBuilder<string>.Rent();
            var members = tagType!.GetMembers().OfType<IFieldSymbol>().ToArray();

            foreach (var y in constants)
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

        Model.TagType? GetTag(
            TypeThatMayBeAssociatedWithResultType tag)
        {
            if (tag == default)
            {
                return null;
            }

            TypeSyntaxReference? subsetTypeRef = null;
            if (tag.Type is { } tagType
                && tagType.TypeKind == TypeKind.Enum)
            {
                var subsetType = GetSubsetType(tagType);
                if (subsetType != null)
                {
                    subsetTypeRef = TypeSyntaxReference.From(subsetType);
                }
            }
            else
            {
                tagType = null;
            }

            return new()
            {
                Type = ConvertToTypeInfo(tag, "Tag"),
                IsEnum = tagType?.TypeKind == TypeKind.Enum,
                SupersetReference = subsetTypeRef,
            };
        }

        static Model.PayloadType? GetPayload(
            TypeThatMayBeAssociatedWithResultType payload)
        {
            if (payload == default)
            {
                return null;
            }
            return new()
            {
                Type = ConvertToTypeInfo(payload, "Payload"),
            };
        }

        static Model.TypeName ConvertToTypeInfo(
            TypeThatMayBeAssociatedWithResultType x,
            string defaultPostfix)
        {
            Debug.Assert(x != default);

            var shortName = x.Type?.Name ?? (x.AssociatedResultType!.Name + defaultPostfix);
            var resultTypeQualifyingPrefix = x.AssociatedResultType?.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            TypeSyntaxReference Type()
            {
                if (x.Type is { } type)
                {
                    return TypeSyntaxReference.From(type);
                }

                if (resultTypeQualifyingPrefix == "")
                {
                    return new(shortName);
                }

                return new(resultTypeQualifyingPrefix + "." + shortName);
            }

            return new()
            {
                QualifiedName = Type(),
                ShortName = shortName,
                AssociatedResultTypeName = x.AssociatedResultType?.Name,
                ResultTypeQualifyingPrefix = resultTypeQualifyingPrefix,
            };
        }
    }

}
