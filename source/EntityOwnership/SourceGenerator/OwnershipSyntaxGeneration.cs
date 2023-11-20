using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using AutoConstructor.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Models;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static EntityOwnership.SourceGenerator.SyntaxFactoryHelper;

namespace EntityOwnership.SourceGenerator;

internal static class OwnershipSyntaxHelper
{
    public static MemberAccessExpressionSyntax? GetOwnerIdExpression(
        this GraphNode graphNode, ExpressionSyntax parent)
    {
        if (graphNode.OwnerIdProperty is not null)
        {
            // e.OwnerId
            return PropertyAccess(parent, graphNode.OwnerIdProperty);
        }
        if (graphNode.OwnerNavigation is { } ownerNavigation)
        {
            // e.Owner.Id
            var ownerIdProperty = graphNode.OwnerNode!.OwnerIdProperty;
            if (ownerIdProperty is null)
                return null;
            var navigationAccess = PropertyAccess(
                parent,
                ownerNavigation);
            return PropertyAccess(
                navigationAccess,
                ownerIdProperty);
        }
        return null;
    }

    public static CompilationUnitSyntax GenerateExtensionMethodClasses(
        Graph graph, NameSyntax generatedNamespace)
    {
        foreach (var graphNode in graph.Nodes)
            graphNode.SyntaxCache = NodeSyntaxCache.Create(graphNode);

        var cache = SyntaxGenerationCache.Instance.Value;
        var members = new List<MemberDeclarationSyntax>();

        AddDirectOwnerFilterMethods(graph, cache, members);
        AddRootOwnerFilterMethods(graph, cache, members);
        var overloadClass = ContainerClass(StaticSyntaxCache.OverloadsClassIdentifier, members);
        members.Clear();

        AddHelperMethods(graph, cache, members);
        var helperClass = ContainerClass(StaticSyntaxCache.HelperClassIdentifier, members);
        members.Clear();

        AddGenericMethods(graph, cache, members);
        var genericMethodsClass = ContainerClass(StaticSyntaxCache.GenericMethodsClassIdentifier, members);
        members.Clear();

        var usings = new List<UsingDirectiveSyntax>();
        usings.Add(UsingDirective(IdentifierName("System.Linq.Expressions")));
        usings.Add(UsingDirective(IdentifierName("System.Linq")));
        usings.Add(UsingDirective(IdentifierName("System")));
        var usingList = List(usings);

        var @namespace = NamespaceDeclaration(generatedNamespace)
            .WithLeadingTrivia(GeneratedFileHelper.GetTriviaList(nullableEnable: true))
            .WithUsings(usingList);

        members.Add(overloadClass);
        members.Add(genericMethodsClass);
        members.Add(helperClass);
        @namespace = @namespace.WithMembers(List(members));

        var compilationUnit = CompilationUnit()
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@namespace));

        // Reformat
        compilationUnit = compilationUnit.NormalizeWhitespace(eol: "\n");

        return compilationUnit;
    }

    private static void AddDirectOwnerFilterMethods(
        Graph graph,
        SyntaxGenerationCache cache,
        List<MemberDeclarationSyntax> outResult)
    {
        foreach (var graphNode in graph.Nodes)
        {
            if (graphNode is not
                {
                    OwnerNode.SyntaxCache.IdParameter: { } ownerIdParameter,
                    SyntaxCache: { } syntaxCache,
                })
            {
                continue;
            }

            /*

             IQueryable<Entity> DirectOwnerFilter(this IQueryable<Entity> query, ID ownerId)
             {
                return query.Where(e => e.OwnerId == ownerId);
             }

            */

            var parameter = syntaxCache.LambdaParameter;
            ExpressionSyntax? lhsExpression = graphNode.GetOwnerIdExpression(
                IdentifierName(parameter.Identifier));
            if (lhsExpression is null)
                continue;

            // e => e.OwnerId == ownerId
            var lambda = EqualsCheckLambda(
                parameter, lhsExpression, IdentifierName(ownerIdParameter.Identifier));

            // query.Where(e => e.OwnerId == ownerId)
            var whereCall = WhereInvocation(
                IdentifierName(syntaxCache.QueryParameter.Identifier), lambda);

            // return ...
            var returnStatement = ReturnStatement(whereCall);

            // IQueryable<Entity> DirectOwnerFilter(this IQueryable<Entity> query, ID ownerId)
            using var parameters = cache.Parameters.Borrow();
            parameters.Add(syntaxCache.QueryParameter);
            parameters.Add(ownerIdParameter);
            var method = FluentExtensionMethod(StaticSyntaxCache.DirectOwnerFilterIdentifier, parameters)
                .WithBody(Block(returnStatement));

            outResult.Add(method);
            syntaxCache.DirectOwnerMethod = method;
        }
    }

    private static void AddRootOwnerFilterMethods(
        Graph graph,
        SyntaxGenerationCache cache,
        List<MemberDeclarationSyntax> outResult)
    {
        foreach (var graphNode in graph.Nodes)
        {
            if (graphNode.Cycle is not null)
                continue;
            if (graphNode.SyntaxCache is not { } syntaxCache)
                continue;

            using var parameters = cache.Parameters.Borrow();
            parameters.Add(syntaxCache.QueryParameter);

            // If the node is itself the root, meaning it has no owner,
            // we have to check its own id.
            MethodDeclarationSyntax? method;
            if (graphNode.OwnerNode is not { } ownerNode)
                method = GetMethod_SelfIsRoot(graphNode, syntaxCache, parameters);
            else
                method = GetMethod_RegularOwnerIdChecks(graphNode, ownerNode, syntaxCache, parameters);

            if (method is null)
                continue;

            outResult.Add(method);
            syntaxCache.RootOwnerMethod = method;
        }

        MethodDeclarationSyntax? GetMethod_SelfIsRoot(GraphNode graphNode,
            NodeSyntaxCache syntaxCache, BorrowableList<ParameterSyntax> parameters)
        {
            if (graphNode.IdProperty is not { } idProperty)
                return null;
            if (syntaxCache.IdParameter is not { } idParameter)
                return null;

            parameters.Add(idParameter);
            var method = FluentExtensionMethod(
                StaticSyntaxCache.RootOwnerFilterIdentifier, parameters);

            // e => e.Id == ownerId
            var parameter = syntaxCache.LambdaParameter;
            var lambda = EqualsCheckLambda(
                parameter,
                PropertyAccess(IdentifierName(parameter.Identifier), idProperty),
                IdentifierName(idParameter.Identifier));
            // query.Where(e => e.Id == ownerId)
            var whereInvocation = WhereInvocation(
                IdentifierName(syntaxCache.QueryParameter.Identifier), lambda);
            var returnStatement = ReturnStatement(whereInvocation);
            method = method.WithBody(Block(returnStatement));
            return method;
        }

        MethodDeclarationSyntax? GetMethod_RegularOwnerIdChecks(
            GraphNode graphNode,
            GraphNode ownerNode,
            NodeSyntaxCache syntaxCache,
            BorrowableList<ParameterSyntax> parameters)
        {
            if (graphNode is not
                {
                    OwnerNavigation: { } ownerNavigation,
                    RootOwnerNode: { SyntaxCache.IdParameter: { } rootOwnerIdParameter } rootOwnerNode,
                })
            {
                return null;
            }

            parameters.Add(rootOwnerIdParameter);
            var method = FluentExtensionMethod(
                StaticSyntaxCache.RootOwnerFilterIdentifier, parameters);

            if (ReferenceEquals(ownerNode, rootOwnerNode))
            {
                if (syntaxCache.DirectOwnerMethod is not { } directOwnerMethod)
                    return null;

                // Just call the root filter:
                // return DirectOwnerFilter(query, ownerId);
                cache.Arguments[0] = Argument(IdentifierName(syntaxCache.QueryParameter.Identifier));
                cache.Arguments[1] = Argument(IdentifierName(rootOwnerIdParameter.Identifier));
                var invocation = InvocationExpression(IdentifierName(directOwnerMethod.Identifier))
                    .WithArgumentList(ArgumentList(SeparatedList(cache.Arguments)));
                return method.WithBody(Block(ReturnStatement(invocation)));
            }

            var parentExpression = IdentifierName(syntaxCache.LambdaParameter.Identifier);
            var memberAccessChain = PropertyAccess(parentExpression, ownerNavigation);

            if (!CompleteMemberAccessChain())
                return null;

            // return query.Where(e => e.Owner...)
            var lambda = EqualsCheckLambda(
                syntaxCache.LambdaParameter,
                memberAccessChain,
                IdentifierName(rootOwnerIdParameter.Identifier));
            var returnStatement = ReturnStatement(
                WhereInvocation(
                    IdentifierName(syntaxCache.QueryParameter.Identifier),
                    lambda));
            method = method.WithBody(Block(returnStatement));
            return method;

            bool CompleteMemberAccessChain()
            {
                GraphNode node = ownerNode;
                GraphNode nodeOwner = ownerNode.OwnerNode!;

                while (true)
                {
                    if (nodeOwner.OwnerNode is not { } potentialRoot)
                        break;
                    if (node.OwnerNavigation is not { } ownerOwnerNavigation)
                        return false;

                    node = nodeOwner;
                    nodeOwner = potentialRoot;

                    memberAccessChain = PropertyAccess(memberAccessChain, ownerOwnerNavigation);
                }

                // nodeOwner is now the root node, so we can reach for the owner id instead of the navigation.
                memberAccessChain = node.GetOwnerIdExpression(memberAccessChain);

                return true;
            }
        }
    }

    // Now the switch
    // The generic method
    /*

    public static IQueryable<T> DirectOwnerFilter<T, TId>(this IQueryable<T> query, TId ownerId)
    {
        if (typeof(T) == typeof(Entity1))
        {
            var q = (IQueryable<Entity1>) query;
            var id = Coerce<TId, ID1>(ownerId);
            return EntityOwnershipOverloads.DirectOwnerFilter(q, id);
        }
        if ...

        throw new InvalidOperationException();
    }

    public static IQueryable<T> RootOwnerFilter<T, TId>(this IQueryable<T> query, TId ownerId)
    {
        if (typeof(T) == typeof(Entity1))
        {
            var q = (IQueryable<Entity1>) query;
            var id = Coerce<TId, ID1>(ownerId);
            return EntityOwnershipOverloads.RootOwnerFilter(q, id);
        }
        if ...

        throw new InvalidOperationException();
    }


    private static U Coerce<T, U>(T value)
    {
        if (value is not U u)
            throw new WrongIdTypeException(expected: typeof(U), actual: typeof(T));
        return u;
    }

    public static SomeOwnerFilter<T, TOwner, TId>(this IQueryable<T> query, TId ownerId)
    {
        if (typeof(T) == typeof(Entity1))
        {
            var q = (IQueryable<Entity1>) query;
            if (typeof(TOwner) == typeof(Entity1Owner1))
            {
                var id = Coerce<TId, ID1>(ownerId);
                return query.Where(e => e.Owner1.Id == id);
            }
            if ...

            throw new TypeNotOwnerException();
        }
        if ...

        throw new InvalidOperationException();
    }

    */
    private static void AddGenericMethods(
        Graph graph,
        SyntaxGenerationCache cache,
        List<MemberDeclarationSyntax> outResult)
    {
        var genericContext = GenericContext.Create();
        outResult.Add(CreateSwitchMethod(0));
        outResult.Add(CreateSwitchMethod(1));
        outResult.Add(CreateGetSomeOwnerFilterMethod());
        outResult.Add(StaticSyntaxCache.SomeOwnerFilterTMethod);
        outResult.Add(StaticSyntaxCache.CoerceMethod);
        AddGetOwnerIdAccesses();
        outResult.Add(CreateTrySetDirectOwnerIdMethod());

        foreach (var expressionKind in new[]
            {
                OwnerExpressionKind.Id,
                OwnerExpressionKind.Owner,
            })
        {
            var method = AddGetOwnerNavigationExpressionsMethods(expressionKind);
            outResult.Add(method);
        }

        // "Switch" here is an analogy.
        MethodDeclarationSyntax CreateSwitchMethod(int methodIndex)
        {
            SyntaxToken methodToCallIdentifier = methodIndex switch
            {
                0 => StaticSyntaxCache.DirectOwnerFilterIdentifier,
                1 => StaticSyntaxCache.RootOwnerFilterIdentifier,
                _ => throw new ArgumentOutOfRangeException(nameof(methodIndex))
            };
            var methodToCall = MethodAccess(
                StaticSyntaxCache.OverloadsClassIdentifier, methodToCallIdentifier);
            using var statements = cache.Statements.Borrow();

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                if (methodIndex switch
                    {
                        0 => syntaxCache.DirectOwnerMethod,
                        1 => syntaxCache.RootOwnerMethod,
                        _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                    } is null)
                {
                    continue;
                }

                var ownerNode = methodIndex switch
                {
                    0 => graphNode.OwnerNode,
                    1 => graphNode.RootOwnerNode,
                    _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                };
                if (ownerNode?.SyntaxCache?.IdType is not { } ownerIdType)
                    continue;

                var branch = genericContext.CreateBranch(
                    cache, methodToCall, syntaxCache, ownerIdType);
                statements.Add(branch);
            }

            statements.Add(ThrowInvalidOperationStatement);

            var methodName = methodIndex switch
            {
                0 => StaticSyntaxCache.DirectOwnerFilterTIdentifier,
                1 => StaticSyntaxCache.RootOwnerFilterTIdentifier,
                _ => throw new ArgumentOutOfRangeException(nameof(methodIndex))
            };

            var method = genericContext.CreateQueryMethod(cache, methodName)
                .TypeParams2();

            return method;
        }

        /*
        Expression<Func<T, bool>>? GetSomeOwnerFilter<T, TOwner, TId>(TId ownerId)
        {
            if (typeof(T) == typeof(Entity1))
            {
                if (typeof(TOwner) == typeof(Entity1Owner1))
                    return (e => e.Navigation.OwnerId == ownerId);
                if (typeof(TOwner) == typeof(Entity1Owner2))
                    return (e => e.Navigation.OwnerId == ownerId);
                ...
            }
        }
        */
        MethodDeclarationSyntax CreateGetSomeOwnerFilterMethod()
        {
            using var statements = cache.Statements.Borrow();

            // Expression<Func<TEntity, bool>>?
            var resultType = NullableType(
                UnqualifiedExpressionFunc(
                    cache,
                    genericContext.EntityType,
                    IdentifierName("bool")));

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                if (graphNode.Cycle is not null)
                    continue;

                using var statements2 = cache.Statements2.Borrow();
                var startExpression = IdentifierName(syntaxCache.LambdaParameter.Identifier);
                var typeofGenericOwner = TypeOfExpression(genericContext.OwnerType);
                foreach (var (ownerNode, idAccess, _) in GetIdNavigations(startExpression, graphNode))
                {
                    if (ownerNode.SyntaxCache is not { IdType: { } ownerIdType } ownerSyntaxCache)
                        continue;

                    // if (typeof(TOwner) == typeof(Entity1Owner1))
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        typeofGenericOwner,
                        TypeOfExpression(ownerSyntaxCache.EntityType));

                    // var id = Coerce<TId, int>(ownerId);
                    var id = genericContext.IdDeclaration(cache, ownerIdType);

                    // Expression<Func<Entity1, bool>> e = (T e) => e.Navigation.OwnerId == id;
                    var lambda = EqualsCheckLambda(
                        syntaxCache.LambdaParameter,
                        idAccess,
                        IdentifierName(id.Identifier));
                    var lambdaType = UnqualifiedExpressionFunc(
                        cache,
                        syntaxCache.EntityType,
                        IdentifierName("bool"));
                    var lambdaVariableIdentifier = Identifier("e");
                    var lambdaVariableDeclaration = SingleVariableDeclaration(
                        lambdaType,
                        VariableDeclarator(lambdaVariableIdentifier)
                            .WithInitializer(EqualsValueClause(lambda)));

                    // return (Expression<Func<T, bool>>?) (LambdaExpression) e;
                    var castedLambda = CastExpression(
                        resultType,
                        CastExpression(
                            NullableType(IdentifierName(nameof(LambdaExpression))),
                            IdentifierName(lambdaVariableIdentifier)));
                    var returnStatement = ReturnStatement(castedLambda);


                    using var statements3 = cache.Statements3.Borrow();
                    statements3.Add(LocalDeclarationStatement(id.Declaration));
                    statements3.Add(LocalDeclarationStatement(lambdaVariableDeclaration));
                    statements3.Add(returnStatement);

                    var ifStatement = IfStatement(typeCheck, Block(statements3));
                    statements2.Add(ifStatement);
                }

                {
                    statements2.Add(ReturnNull);

                    // if (typeof(TEntity) == typeof(Entity1))
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        TypeOfExpression(genericContext.EntityType),
                        TypeOfExpression(syntaxCache.EntityType));
                    var ifStatement = IfStatement(typeCheck, Block(statements2));

                    statements.Add(ifStatement);
                }
            }

            statements.Add(ReturnNull);
            var parameter = ParameterList(SingletonSeparatedList(genericContext.OwnerIdParameter));
            var method = MethodDeclaration(resultType, StaticSyntaxCache.GenericGetSomeOwnerFilterIdentifier)
                .WithParameterList(parameter)
                .WithBody(Block(statements))
                .WithModifiers(PublicStatic);
            method = genericContext
                .CreateMethod(cache, method)
                .TypeParams3();
            return method;
        }

        void AddGetOwnerIdAccesses()
        {
            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;

                var startExpression = IdentifierName(syntaxCache.LambdaParameter.Identifier);
                var idNavigations = GetIdNavigations(startExpression, graphNode);

                foreach (var idNavigation in idNavigations)
                {
                    if (idNavigation.OwnerNode.SyntaxCache is not { } ownerSyntaxCache)
                        continue;
                    if (ownerSyntaxCache.IdType is not { } ownerIdType)
                        continue;

                    // private static readonly Expression<Func<...>> name = x => x.Navigation.Id;
                    AddAccessThing(
                        ownerIdType,
                        idNavigation.IdAccess,
                        OwnerExpressionKind.Id);

                    if (idNavigation.OwnerAccess is not null)
                    {
                        // private static readonly Expression<Func<...>> name = x => x.Navigation;
                        AddAccessThing(
                            ownerSyntaxCache.EntityType,
                            idNavigation.OwnerAccess,
                            OwnerExpressionKind.Owner);
                    }

                    void AddAccessThing(
                        TypeSyntax outputTypeOfLambda,
                        ExpressionSyntax access,
                        OwnerExpressionKind expressionKind)
                    {
                        var accessName = syntaxCache.GetOwnerAccessName(ownerSyntaxCache, expressionKind);
                        var outputList = syntaxCache.GetOwnerAccessesList(expressionKind);
                        outputList.Add((idNavigation.OwnerNode, accessName));

                        var lambda = ParenthesizedLambdaExpression(access)
                            .WithParameterList(ParameterList(SingletonSeparatedList(syntaxCache.LambdaParameter)));
                        var expressionType = UnqualifiedExpressionFunc(
                            cache, syntaxCache.EntityType, outputTypeOfLambda);
                        var variable = VariableDeclarator(Identifier(accessName))
                            .WithInitializer(EqualsValueClause(lambda));
                        var idAccessMember = FieldDeclaration(SingleVariableDeclaration(expressionType, variable))
                            .WithModifiers(PrivateStaticReadonly);
                        outResult.Add(idAccessMember);
                    }
                }
            }
        }

        // bool TrySetDirectOwnerId(TEntity entity, TOwnerId ownerId) => entity.OwnerId = ownerId;
        MethodDeclarationSyntax CreateTrySetDirectOwnerIdMethod()
        {
            using var statements = cache.Statements.Borrow();
            var entityParameter = Parameter(Identifier("entity"))
                .WithType(genericContext.EntityType);

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode is not
                    {
                        SyntaxCache: { } syntaxCache,
                        OwnerNode.SyntaxCache:
                        {
                            IdType: { } ownerIdType,
                        } ownerSyntaxCache,
                    })
                {
                    continue;
                }

                // if (typeof(Entity) == typeof(Entity1))
                var entityTypeCheck = BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    TypeOfExpression(genericContext.EntityType),
                    TypeOfExpression(syntaxCache.EntityType));

                // if (typeof(Owner) != typeof(Owner1))
                //     return false;
                var ownerTypeCheck = BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    TypeOfExpression(genericContext.OwnerType),
                    TypeOfExpression(ownerSyntaxCache.EntityType));
                var ownerTypeCheckStatement = IfStatement(ownerTypeCheck, ReturnFalse);

                // var castedEntity = (Entity1)(object)entity;
                var castToConcreteEntity = CastExpression(
                    syntaxCache.EntityType,
                    CastExpression(
                        IdentifierName("object"),
                        IdentifierName(entityParameter.Identifier)));
                var castedEntityVariable = Identifier("castedEntity");
                var castedEntityVariableDeclaration = VariableDeclaration(
                    IdentifierName("var"),
                    SingletonSeparatedList(
                        VariableDeclarator(castedEntityVariable)
                            .WithInitializer(EqualsValueClause(castToConcreteEntity))));

                // x => x.Navigation.Id
                var idAccess = graphNode.GetOwnerIdExpression(
                    IdentifierName(castedEntityVariable));
                if (idAccess is null)
                    continue;

                // var id = Coerce<T, int>(ownerId);
                var coercedId = genericContext.IdDeclaration(cache, ownerIdType);

                // castedEntity.OwnerId = ownerId
                var idAssignment = AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    idAccess, IdentifierName(coercedId.Identifier));

                using var statements2 = cache.Statements2.Borrow();
                statements2.Add(ownerTypeCheckStatement);
                statements2.Add(LocalDeclarationStatement(coercedId.Declaration));
                statements2.Add(LocalDeclarationStatement(castedEntityVariableDeclaration));
                statements2.Add(ExpressionStatement(idAssignment));
                statements2.Add(ReturnTrue);

                var ifStatement = IfStatement(
                    entityTypeCheck, Block(statements2));
                statements.Add(ifStatement);
            }

            statements.Add(ReturnFalse);

            {
                using var typeParameters = cache.TypeParameters.Borrow();
                typeParameters.Add(genericContext.EntityTypeParameter);
                typeParameters.Add(genericContext.OwnerTypeParameter);
                typeParameters.Add(genericContext.OwnerIdTypeParameter);

                using var parameters = cache.Parameters.Borrow();
                parameters.Add(entityParameter
                    // Extension method
                    .WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))));
                parameters.Add(genericContext.OwnerIdParameter);

                var method = MethodDeclaration(
                    returnType: PredefinedType(Token(SyntaxKind.BoolKeyword)),
                    identifier: StaticSyntaxCache.TrySetOwnerIdIdentifier)

                    .WithTypeParameterList(TypeParameterList(SeparatedList(typeParameters)))
                    .WithModifiers(PublicStatic)
                    .WithParameterList(ParameterList(SeparatedList(parameters)))
                    .WithBody(Block(statements))

                    // where Entity : notnull
                    .WithConstraintClauses(SingletonList(
                        TypeParameterConstraintClause(
                            IdentifierName(genericContext.EntityTypeParameter.Identifier))
                        .WithConstraints(SingletonSeparatedList<TypeParameterConstraintSyntax>(
                            TypeConstraint(IdentifierName("notnull"))))));

                return method;
            }
        }

        // GetOwnerId(Type entity, Type owner) => (e => e.Navigation.OwnerId)
        MethodDeclarationSyntax AddGetOwnerNavigationExpressionsMethods(OwnerExpressionKind expressionKind)
        {
            using var statements = cache.Statements.Borrow();

            var methodReturnType = IdentifierName(nameof(Expression));

            ParameterSyntax TypeParam(string name)
            {
                return Parameter(Identifier(name))
                    .WithType(ParseTypeName(typeof(Type).FullName!));
            }

            var entityTypeParameter = TypeParam("entityType");
            var ownerTypeParameter = TypeParam("ownerType");

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;

                using var statements2 = cache.Statements2.Borrow();

                foreach (var (ownerNode, idAccessName) in syntaxCache.GetOwnerAccessesList(expressionKind))
                {
                    var ownerSyntaxCache = ownerNode.SyntaxCache!;
                    // if (typeof(TOwner) == typeof(Entity1Owner1))
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        IdentifierName(ownerTypeParameter.Identifier),
                        TypeOfExpression(ownerSyntaxCache.EntityType));
                    // return _CachedExpression;
                    var returnStatement = ReturnStatement(IdentifierName(idAccessName));
                    var ifStatement = IfStatement(typeCheck, returnStatement);
                    statements2.Add(ifStatement);
                }
                statements2.Add(ReturnNull);

                {
                    // if (typeof(T) == typeof(Entity1))
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        IdentifierName(entityTypeParameter.Identifier),
                        TypeOfExpression(syntaxCache.EntityType));
                    var block = Block(statements2);
                    var ifStatement = IfStatement(typeCheck, block);
                    statements.Add(ifStatement);
                }
            }

            statements.Add(ReturnNull);
            {
                using var typeParameters = cache.Parameters.Borrow();
                typeParameters.Add(entityTypeParameter);
                typeParameters.Add(ownerTypeParameter);

                var returnType = NullableType(methodReturnType);
                var method = MethodDeclaration(
                    returnType: returnType,
                    identifier: StaticSyntaxCache.GetGetOwnerExpressionIdentifier(expressionKind))

                    .WithParameterList(ParameterList(SeparatedList(typeParameters)))
                    .WithModifiers(PublicStatic)
                    .WithBody(Block(statements));

                return method;
            }
        }
    }

    private static GenericNameSyntax UnqualifiedExpressionFunc(
        SyntaxGenerationCache cache,
        TypeSyntax parameter,
        TypeSyntax returnType)
    {
        cache.TypeArguments[0] = parameter;
        cache.TypeArguments[1] = returnType;
        var funcType = GenericName(
            Identifier("Func"),
            TypeArgumentList(SeparatedList(cache.TypeArguments)));
        var expressionType = GenericName(
            Identifier("Expression"),
            TypeArgumentList(SingletonSeparatedList<TypeSyntax>(funcType)));
        return expressionType;
    }

    private record struct OwnerAccessExpressions(
        GraphNode OwnerNode,
        ExpressionSyntax IdAccess,
        ExpressionSyntax? OwnerAccess);

    // q --> q.Navigation.Id
    private static IEnumerable<OwnerAccessExpressions> GetIdNavigations(
        ExpressionSyntax start, GraphNode leaf)
    {
        if (leaf.IdProperty is { } leafIdProperty)
            yield return new(leaf, PropertyAccess(start, leafIdProperty), start);
        if (leaf.OwnerNode is not { } ownerNode)
            yield break;

        GraphNode node = leaf;
        var chainToNode = start;

        while (true)
        {
            if (ownerNode.OwnerNode is not { } nextOwnerNode)
                break;

            ExpressionSyntax? chainToParent = null;
            if (node.OwnerNavigation is { } ownerNavigation)
                chainToParent = PropertyAccess(chainToNode, ownerNavigation);

            ExpressionSyntax ownerIdAccess;
            if (node.OwnerIdProperty is { } ownerIProperty)
                ownerIdAccess = PropertyAccess(chainToNode, ownerIProperty);
            else if (chainToParent is not null)
                ownerIdAccess = PropertyAccess(chainToParent, ownerNode.IdProperty!);
            else
                yield break;

            yield return new(ownerNode, ownerIdAccess, chainToParent);

            if (chainToParent is null)
                yield break;

            node = ownerNode;
            ownerNode = nextOwnerNode;
            chainToNode = chainToParent;
        }

        if (node.GetOwnerIdExpression(chainToNode) is { } idAccess)
        {
            ExpressionSyntax? ownerNavigationExpression = null;
            if (node.OwnerNavigation is { } ownerNavigation)
                ownerNavigationExpression = PropertyAccess(chainToNode, ownerNavigation);
            yield return new(ownerNode, idAccess, ownerNavigationExpression);
        }
    }

    /*


    public static Type GetIdType(Type entityType)
    {
        if (type == typeof(Entity1))
            return typeof(ID1);
        if ...
        throw new InvalidOperationException();
    }

    public static bool SupportsDirectOwnerFilter(Type type)
    {
        if (type == typeof(Entity1))
            return true;
        if ...
        return false;
    }
    public static bool SupportsDirectOwnerFilter(Type type, Type idType)
    {
        return SupportsOwnerFilter(type) && GetIdType(type) == idType;
    }

    public static bool SupportsRootOwnerFilter(Type type)
    {
        if (type == typeof(Entity1))
            return true;
        if ...
        return false;
    }
    public static bool SupportsRootOwnerFilter(Type type, Type idType)
    {
        return SupportsRootFilter(type) && GetIdType(type) == idType;
    }

    */
    private static void AddHelperMethods(
        Graph graph,
        SyntaxGenerationCache cache,
        List<MemberDeclarationSyntax> outResult)
    {
        var typeType = ParseTypeName(typeof(Type).FullName!);
        var nullableTypeType = NullableType(typeType);
        var entityTypeParameter = Parameter(Identifier("entityType"))
            .WithType(typeType);
        var entityTypeParameterAsList = ParameterList(SingletonSeparatedList(entityTypeParameter));

        // Initialize type checks
        foreach (var graphNode in graph.Nodes)
        {
            if (graphNode.SyntaxCache is { } syntaxCache)
                syntaxCache.EntityTypeCheck = EntityTypeCheck(syntaxCache.EntityType);

            BinaryExpressionSyntax EntityTypeCheck(TypeSyntax otherType)
            {
                return BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    IdentifierName(entityTypeParameter.Identifier),
                    TypeOfExpression(otherType));
            }
        }

        outResult.Add(CreateGetTypeIdMethod());

        outResult.Add(CreateGetOwnerTypeMethod(0));
        outResult.Add(CreateGetOwnerTypeMethod(1));

        AddSupportsMethods(0);
        AddSupportsMethods(1);

        AddSupportsSomeOwnerFilterMethods();

        AddDependentTypes();

        // GetDirectOwnerType(Type) => Type?
        // GetRootOwnerType(Type) => Type?
        MethodDeclarationSyntax CreateGetOwnerTypeMethod(int methodIndex)
        {
            using var statements = cache.Statements.Borrow();
            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                var xOwner = methodIndex switch
                {
                    0 => graphNode.OwnerNode,
                    1 => graphNode.RootOwnerNode,
                    _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                };
                if (xOwner?.SyntaxCache is not { } ownerSyntaxCache)
                    continue;

                var typeCheck = syntaxCache.EntityTypeCheck!;
                var returnIdType = ReturnStatement(
                    TypeOfExpression(ownerSyntaxCache.EntityType));
                var ifStatement = IfStatement(typeCheck, returnIdType);
                statements.Add(ifStatement);
            }

            var finalStatement = ReturnNull;
            statements.Add(finalStatement);
            var method = MethodDeclaration(
                    returnType: nullableTypeType,
                    identifier: methodIndex switch
                    {
                        0 => Identifier("GetDirectOwnerType"),
                        1 => Identifier("GetRootOwnerType"),
                        _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                    })
                .WithModifiers(PublicStatic)
                .WithParameterList(entityTypeParameterAsList)
                .WithBody(Block(statements));
            return method;
        }

        MethodDeclarationSyntax CreateGetTypeIdMethod()
        {
            using var statements = cache.Statements.Borrow();
            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { IdType: { } idType } syntaxCache)
                    continue;
                var typeCheck = BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    IdentifierName("entityType"),
                    TypeOfExpression(syntaxCache.EntityType));
                var returnIdType = ReturnStatement(
                    TypeOfExpression(idType));
                var ifStatement = IfStatement(typeCheck, returnIdType);
                statements.Add(ifStatement);
            }
            statements.Add(ReturnNull);

            var getTypeIdMethod = MethodDeclaration(
                returnType: nullableTypeType,
                identifier: Identifier("GetIdType"))

                .WithModifiers(PublicStatic)
                .WithParameterList(entityTypeParameterAsList)
                .WithBody(Block(statements));

            return getTypeIdMethod;
        }

        void AddSupportsMethods(int methodIndex)
        {
            using var statements = cache.Statements.Borrow();
            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                var targetMethod = methodIndex switch
                {
                    0 => syntaxCache.DirectOwnerMethod,
                    1 => syntaxCache.RootOwnerMethod,
                    _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                };
                if (targetMethod is null)
                    continue;

                var typeCheck = syntaxCache.EntityTypeCheck!;
                var ifStatement = IfStatement(typeCheck, ReturnTrue);
                statements.Add(ifStatement);
            }

            statements.Add(ReturnFalse);
            var supportsMethod = MethodDeclaration(
                    returnType: PredefinedType(Token(SyntaxKind.BoolKeyword)),
                    identifier: methodIndex switch
                    {
                        0 => Identifier("SupportsDirectOwnerFilter"),
                        1 => Identifier("SupportsRootOwnerFilter"),
                        _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
                    })
                .WithModifiers(PublicStatic)
                .WithParameterList(entityTypeParameterAsList)
                .WithBody(Block(statements));

            outResult.Add(supportsMethod);

            var overload2 = methodIndex switch
            {
                0 => StaticSyntaxCache.SupportsDirectOwnerFilter2Method,
                1 => StaticSyntaxCache.SupportsRootOwnerFilter2Method,
                _ => throw new ArgumentOutOfRangeException(nameof(methodIndex)),
            };
            outResult.Add(overload2);
        }

        // SupportsSomeOwnerFilter(Type entity, Type ownerType) => bool
        void AddSupportsSomeOwnerFilterMethods()
        {
            var ownerTypeParameter = Parameter(Identifier("ownerType"))
                .WithType(typeType);

            using var statements = cache.Statements.Borrow();

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.Cycle is not null)
                    continue;
                using var statements2 = cache.Statements2.Borrow();
                var node = graphNode;
                do
                {
                    if (node.SyntaxCache is { } syntaxCache)
                    {
                        // if (ownerType == typeof(OwnerType))
                        //    return true;
                        var typeCheck = BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            IdentifierName(ownerTypeParameter.Identifier),
                            TypeOfExpression(syntaxCache.EntityType));
                        var ifStatement = IfStatement(typeCheck, ReturnTrue);

                        statements2.Add(ifStatement);
                    }
                    node = node.OwnerNode;
                }
                while (node is not null);

                statements2.Add(ReturnFalse);

                {
                    if (graphNode.SyntaxCache is not { } syntaxCache)
                        continue;
                    // if (entityType == typeof(EntityType))
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        IdentifierName(entityTypeParameter.Identifier),
                        TypeOfExpression(syntaxCache.EntityType));
                    var ifStatement = IfStatement(typeCheck, Block(statements2));
                    statements.Add(ifStatement);
                }
            }

            var methodIdentifier = Identifier("SupportsSomeOwnerFilter");
            {
                statements.Add(ReturnFalse);

                var method = MethodDeclaration(
                        identifier: methodIdentifier,
                        returnType: PredefinedType(Token(SyntaxKind.BoolKeyword)))

                    .WithBody(Block(statements))
                    .WithModifiers(PublicStatic);

                using (var p = cache.Parameters.Borrow())
                {
                    p.Add(entityTypeParameter);
                    p.Add(ownerTypeParameter);
                    method = method.WithParameterList(ParameterList(SeparatedList(p)));
                }

                outResult.Add(method);
            }

            // SupportsSomeOwnerFilter(Type entityType, Type ownerType, Type idType) -> bool
            outResult.Add(StaticSyntaxCache.SupportsSomeOwnerFilterMethod);
        }

        // ReadOnlyCollection<Type> GetDependentObjects<TOwnerType>()
        void AddDependentTypes()
        {
            var typeParameter = TypeParameter(Identifier("TOwnerType"));
            var readonlyCollectionType = ParseTypeName(
                typeof(ReadOnlyCollection<>).Namespace + ".ReadOnlyCollection<Type>");

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.Cycle is not null)
                    continue;
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;

                var dependentTypesArrayName = syntaxCache.GetDependentTypesArrayName();
                var dependentNodes = graphNode.GetAllDependentNodesDepthFirst();

                using var typeExpressions = ListHelper.Rent<ExpressionSyntax>();
                foreach (var node in dependentNodes)
                {
                    if (node.SyntaxCache is not { } childSyntaxCache)
                        continue;
                    typeExpressions.Add(
                        TypeOfExpression(childSyntaxCache.EntityType));
                }

                // new Type[] { typeof(EntityType), ... }
                var typeArraySyntax = ArrayCreationExpression(
                    ArrayType(typeType, SingletonList(ArrayRankSpecifier())),
                    InitializerExpression(
                        SyntaxKind.ArrayInitializerExpression,
                        SeparatedList(typeExpressions.Enumerable)));

                // System.Array.AsReadOnly(new Type[] { typeof(EntityType), ... })
                var asReadOnlyCast = InvocationExpression(
                    ParseName("System.Array.AsReadOnly"),
                    ArgumentList(SingletonSeparatedList(Argument(typeArraySyntax))));

                // private static readonly ... = AsReadOnly([...]);
                var variableDeclarator = VariableDeclarator(
                    Identifier(dependentTypesArrayName),
                    argumentList: null,
                    initializer: EqualsValueClause(asReadOnlyCast));

                var declaration = FieldDeclaration(
                        SingleVariableDeclaration(readonlyCollectionType, variableDeclarator))
                    .WithModifiers(PrivateStaticReadonly);
                outResult.Add(declaration);
            }

            using var statements = cache.Statements.Borrow();

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.Cycle is not null)
                    continue;
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;

                // if (typeof(TOwnerType) == typeof(EntityType))
                var typeCheck = BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    TypeOfExpression(IdentifierName(typeParameter.Identifier)),
                    TypeOfExpression(syntaxCache.EntityType));
                var returnArray = ReturnStatement(IdentifierName(syntaxCache.GetDependentTypesArrayName()));
                var ifStatement = IfStatement(typeCheck, returnArray);
                statements.Add(ifStatement);
            }

            statements.Add(ThrowInvalidOperationStatement);

            var method = MethodDeclaration(
                    identifier: StaticSyntaxCache.GetDependentTypesIdentifier,
                    returnType: readonlyCollectionType)
                .WithBody(Block(statements))
                .WithModifiers(PublicStatic)
                .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(typeParameter)));
            outResult.Add(method);
        }

    }
}

internal record GenericContext(
    TypeParameterSyntax EntityTypeParameter,
    TypeParameterSyntax OwnerTypeParameter,
    TypeParameterSyntax OwnerIdTypeParameter,
    TypeSyntax EntityType,
    TypeSyntax OwnerType,
    TypeSyntax OwnerIdType,
    ParameterSyntax QueryParameter,
    ParameterSyntax OwnerIdParameter)
{
    public static GenericContext Create()
    {
        var genericEntityTypeParameter = TypeParameter(Identifier("T"));
        var genericEntityType = ParseTypeName(genericEntityTypeParameter.Identifier.Text);
        var genericOwnerTypeParameter = TypeParameter(Identifier("TOwner"));
        var genericOwnerType = ParseTypeName(genericOwnerTypeParameter.Identifier.Text);
        var genericOwnerIdTypeParameter = TypeParameter(Identifier("TId"));
        var genericOwnerIdType = ParseTypeName(genericOwnerIdTypeParameter.Identifier.Text);

        var queryTypeName = GenericName(
            Identifier("IQueryable"),
            TypeArgumentList(SingletonSeparatedList(genericEntityType)));
        var queryParameter = Parameter(Identifier("query"))
            .WithType(queryTypeName);
        var idParameter = Parameter(Identifier("ownerId"))
            .WithType(genericOwnerIdType);

        return new(
            genericEntityTypeParameter,
            genericOwnerTypeParameter,
            genericOwnerIdTypeParameter,
            genericEntityType,
            genericOwnerType,
            genericOwnerIdType,
            queryParameter,
            idParameter);
    }

    public readonly struct MethodBuilder
    {
        private readonly GenericContext _context;
        private readonly SyntaxGenerationCache _cache;
        private readonly MethodDeclarationSyntax _method;

        public MethodBuilder(
            GenericContext context,
            SyntaxGenerationCache cache,
            MethodDeclarationSyntax method)
        {
            _context = context;
            _cache = cache;
            _method = method;
        }

        // Method<TEntity, TOwnerId>
        public MethodDeclarationSyntax TypeParams2()
        {
            using var typeParams = _cache.TypeParameters.Borrow();
            typeParams.Add(_context.EntityTypeParameter);
            typeParams.Add(_context.OwnerIdTypeParameter);
            var method = _method.WithTypeParameterList(TypeParameterList(SeparatedList(
                typeParams)));
            return method;
        }

        // Method<TEntity, TOwner, TId>
        public MethodDeclarationSyntax TypeParams3()
        {
            using var typeParams = _cache.TypeParameters.Borrow();
            typeParams.Add(_context.EntityTypeParameter);
            typeParams.Add(_context.OwnerTypeParameter);
            typeParams.Add(_context.OwnerIdTypeParameter);
            var method = _method.WithTypeParameterList(TypeParameterList(SeparatedList(
                typeParams)));
            return method;
        }
    }

    public MethodBuilder CreateQueryMethod(SyntaxGenerationCache cache, SyntaxToken methodName)
    {
        using var parameters = cache.Parameters.Borrow();
        parameters.Add(QueryParameter);
        parameters.Add(OwnerIdParameter);
        var method = FluentExtensionMethod(methodName, parameters);

        method = method.WithBody(Block(cache.Statements));

        return new MethodBuilder(this, cache, method);
    }

    public MethodBuilder CreateMethod(SyntaxGenerationCache cache, MethodDeclarationSyntax method)
    {
        return new MethodBuilder(this, cache, method);
    }

    public IfStatementSyntax CreateBranch(
        SyntaxGenerationCache syntax,
        MemberAccessExpressionSyntax memberToCall,
        NodeSyntaxCache syntaxCache,
        TypeSyntax ownerIdType)
    {
        var typeOfT = TypeOfExpression(EntityType);
        var typeOfEntity = TypeOfExpression(syntaxCache.EntityType);
        var typeCheck = BinaryExpression(
            SyntaxKind.EqualsExpression, typeOfT, typeOfEntity);

        // var q = (IQueryable<Entity1>) query;
        var (qIdentifier, qDeclaration) = QDeclaration(syntaxCache);
        // var id = Coerce<TId, OwnerIdType>(ownerId);
        var (idIdentifier, idDeclaration) = IdDeclaration(syntax, ownerIdType);

        // return (returnType) Class.Method(q, id);
        syntax.Arguments[0] = Argument(IdentifierName(qIdentifier));
        syntax.Arguments[1] = Argument(IdentifierName(idIdentifier));
        var invocation = InvocationExpression(memberToCall)
            .WithArgumentList(ArgumentList(SeparatedList(syntax.Arguments)));
        var castToReturnType = CastExpression(
            QueryParameter.Type!, invocation);

        var returnStatement = ReturnStatement(castToReturnType);

        BlockSyntax block;
        using (var s = syntax.Statements2.Borrow())
        {
            s.Add(LocalDeclarationStatement(qDeclaration));
            s.Add(LocalDeclarationStatement(idDeclaration));
            s.Add(returnStatement);
            block = Block(s);
        }

        var ifStatement = IfStatement(typeCheck, block);
        return ifStatement;
    }

    public SingleVariableDeclarationInfo QDeclaration(
        NodeSyntaxCache syntaxCache)
    {
        var qIdentifier = Identifier("q");
        var qType = syntaxCache.QueryType;
        // q = (IQueryable<Entity1>) query;
        var qDeclarator = VariableDeclarator(qIdentifier)
            .WithInitializer(EqualsValueClause(
                CastExpression(qType, IdentifierName(QueryParameter.Identifier))));
        // var q = (IQueryable<Entity1>) query;
        var qDeclaration = VariableDeclaration(
            IdentifierName("var"), SingletonSeparatedList(qDeclarator));
        return new(qIdentifier, qDeclaration);
    }

    // var id = Coerce<TId, OwnerIdType>(ownerId);
    public SingleVariableDeclarationInfo IdDeclaration(
        SyntaxGenerationCache syntax,
        TypeSyntax idType)
    {
        var idIdentifier = Identifier("id");
        syntax.TypeArguments[0] = OwnerIdType;
        syntax.TypeArguments[1] = idType;
        var coerceTypeArguments = TypeArgumentList(SeparatedList(syntax.TypeArguments));
        var coerceCall = InvocationExpression(
            GenericName(Identifier("Coerce"), coerceTypeArguments),
            ArgumentList(SingletonSeparatedList(Argument(
                IdentifierName(OwnerIdParameter.Identifier)))));
        var idDeclarator = VariableDeclarator(idIdentifier)
            .WithInitializer(EqualsValueClause(coerceCall));
        var idDeclaration = VariableDeclaration(
            IdentifierName("var"), SingletonSeparatedList(idDeclarator));
        return new(idIdentifier, idDeclaration);
    }
}
