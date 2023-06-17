using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Models;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static EntityOwnership.SourceGenerator.SyntaxFactoryHelper;

namespace EntityOwnership.SourceGenerator;

internal static class OwnershipSyntaxHelper
{
    public static MemberAccessExpressionSyntax? GetOwnerIdExpression(this GraphNode graphNode, ExpressionSyntax parent)
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

    public static CompilationUnitSyntax GenerateExtensionMethodClasses(Graph graph)
    {
        foreach (var graphNode in graph.Nodes)
            graphNode.SyntaxCache = NodeSyntaxCache.Create(graphNode);

        var cache = new SyntaxGenerationCache();
        var members = new List<MemberDeclarationSyntax>();

        AddDirectOwnerFilterMethods(graph, cache, members);
        AddRootOwnerFilterMethods(graph, cache, members);
        var overloadClass = ContainerClass(StaticSyntaxCache.OverloadsClassIdentifier, members);
        members.Clear();

        AddGenericMethods(graph, cache, members);
        var genericMethodsClass = ContainerClass(StaticSyntaxCache.GenericMethodsClassIdentifier, members);
        members.Clear();

        var usings = new List<UsingDirectiveSyntax>();
        usings.Add(UsingDirective(IdentifierName("System.Linq.Expressions")));
        usings.Add(UsingDirective(IdentifierName("System.Linq")));
        var usingList = List(usings);

        members.Add(overloadClass);
        members.Add(genericMethodsClass);
        var @namespace = NamespaceDeclaration(IdentifierName("EntityOwnership"))
            .WithMembers(List(members));

        var compilationUnit = CompilationUnit()
            .WithLeadingTrivia(GeneratedFileHelper.GetTriviaList(nullableEnable: true))
            .WithUsings(usingList)
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@namespace));

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
                    OwnerNode: { SyntaxCache: { } ownerSyntaxCache } ownerNode,
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

            var parameter = Parameter(Identifier("e"));
            ExpressionSyntax? lhsExpression = graphNode.GetOwnerIdExpression(
                IdentifierName(parameter.Identifier));
            if (lhsExpression is null)
                continue;

            // e => e.OwnerId == ownerId
            var lambda = EqualsCheckLambda(
                parameter, lhsExpression, IdentifierName(ownerSyntaxCache.IdParameter.Identifier));

            // query.Where(e => e.OwnerId == ownerId)
            var whereCall = WhereInvocation(
                IdentifierName(syntaxCache.QueryParameter.Identifier), lambda);

            // return ...
            var returnStatement = ReturnStatement(whereCall);

            // IQueryable<Entity> DirectOwnerFilter(this IQueryable<Entity> query, ID ownerId)
            cache.Parameters[0] = syntaxCache.QueryParameter;
            cache.Parameters[1] = ownerSyntaxCache.IdParameter;
            var method = FluentExtensionMethod(StaticSyntaxCache.DirectOwnerFilterIdentifier, cache.Parameters)
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

            cache.Parameters[0] = syntaxCache.QueryParameter;

            // If the node is itself the root, meaning it has no owner,
            // we have to check its own id.
            MethodDeclarationSyntax? method;
            if (graphNode.OwnerNode is null)
                method = GetMethod_SelfIsRoot(graphNode, syntaxCache);
            else
                method = GetMethod_RegularOwnerIdChecks(graphNode, syntaxCache);

            if (method is null)
                continue;

            outResult.Add(method);
            syntaxCache.RootOwnerMethod = method;
        }

        MethodDeclarationSyntax? GetMethod_SelfIsRoot(
            GraphNode graphNode,
            NodeSyntaxCache syntaxCache)
        {
            if (graphNode.IdProperty is not { } idProperty)
                return null;

            cache.Parameters[1] = syntaxCache.IdParameter;
            var method = FluentExtensionMethod(StaticSyntaxCache.RootOwnerFilterIdentifier, cache.Parameters);
            // e => e.Id == ownerId
            var parameter = Identifier("e");
            var lambda = EqualsCheckLambda(
                Parameter(parameter),
                PropertyAccess(IdentifierName(parameter), idProperty),
                IdentifierName(syntaxCache.IdParameter.Identifier));
            var whereInvocation = WhereInvocation(
                IdentifierName(syntaxCache.QueryParameter.Identifier), lambda);
            var returnStatement = ReturnStatement(whereInvocation);
            method = method.WithBody(Block(returnStatement));
            return method;
        }

        MethodDeclarationSyntax? GetMethod_RegularOwnerIdChecks(
            GraphNode graphNode,
            NodeSyntaxCache syntaxCache)
        {
            if (graphNode is not
                {
                    OwnerNavigation: { } ownerNavigation,
                    RootOwner: { SyntaxCache: { } rootOwnerCache } rootOwnerNode,
                })
            {
                return null;
            }

            cache.Parameters[1] = rootOwnerCache.IdParameter;
            var method = FluentExtensionMethod(StaticSyntaxCache.RootOwnerFilterIdentifier, cache.Parameters);

            if (ReferenceEquals(graphNode, rootOwnerNode))
            {
                if (syntaxCache.DirectOwnerMethod is not { } directOwnerMethod)
                    return null;

                // Just call the root filter:
                // return DirectOwnerFilter(query, ownerId);
                cache.Arguments[0] = Argument(IdentifierName(syntaxCache.QueryParameter.Identifier));
                cache.Arguments[1] = Argument(IdentifierName(rootOwnerCache.IdParameter.Identifier));
                var invocation = InvocationExpression(IdentifierName(directOwnerMethod.Identifier))
                    .WithArgumentList(ArgumentList(SeparatedList(cache.Arguments)));
                return method.WithBody(Block(ReturnStatement(invocation)));
            }

            var entityParameter = Parameter(Identifier("e"));
            var parentExpression = IdentifierName(entityParameter.Identifier);
            var memberAccessChain = PropertyAccess(parentExpression, ownerNavigation);

            if (!CompleteMemberAccessChain())
                return null;

            var lambda = EqualsCheckLambda(
                entityParameter,
                memberAccessChain,
                IdentifierName(rootOwnerCache.IdParameter.Identifier));
            var returnStatement = ReturnStatement(
                WhereInvocation(
                    IdentifierName(syntaxCache.QueryParameter.Identifier),
                    lambda));
            method = method.WithBody(Block(returnStatement));
            return method;

            bool CompleteMemberAccessChain()
            {
                GraphNode node = graphNode;
                GraphNode nodeOwner = graphNode.OwnerNode!;

                while (true)
                {
                    if (nodeOwner.OwnerNode is not { } potentialRoot)
                        break;
                    if (node.OwnerNavigation is not { } ownerOwnerNavigation)
                        return false;
                    memberAccessChain = PropertyAccess(memberAccessChain, ownerOwnerNavigation);

                    node = nodeOwner;
                    nodeOwner = potentialRoot;
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

    */
    private static void AddGenericMethods(
        Graph graph,
        SyntaxGenerationCache cache,
        List<MemberDeclarationSyntax> outResult)
    {
        var genericContext = GenericContext.Create();
        var statements = new List<StatementSyntax>(graph.Nodes.Length + 1);
        outResult.Add(CreateSwitchMethod(0));
        outResult.Add(CreateSwitchMethod(1));
        outResult.Add(StaticSyntaxCache.CoerceMethod);

        MethodDeclarationSyntax CreateSwitchMethod(int methodToCallIndex)
        {
            SyntaxToken methodToCallIdentifier = methodToCallIndex switch
            {
                0 => StaticSyntaxCache.DirectOwnerFilterIdentifier,
                1 => StaticSyntaxCache.RootOwnerFilterIdentifier,
                _ => throw new ArgumentOutOfRangeException(nameof(methodToCallIndex))
            };
            var methodToCall = MethodAccess(
                StaticSyntaxCache.OverloadsClassIdentifier, methodToCallIdentifier);

            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                if (methodToCallIndex switch
                    {
                        0 => syntaxCache.DirectOwnerMethod,
                        1 => syntaxCache.RootOwnerMethod,
                        _ => throw new ArgumentOutOfRangeException(nameof(methodToCallIndex))
                    } is null)
                {
                    continue;
                }
                if (syntaxCache.RootOwnerMethod is null)
                    continue;
                var branch = genericContext.CreateBranch(
                    cache, methodToCall, syntaxCache);
                statements.Add(branch);
            }

            // throw new InvalidOperationException();
            statements.Add(ThrowStatement(ObjectCreationExpression(
                IdentifierName("InvalidOperationException"))));

            var method = genericContext.CreateMethod(
                cache, methodToCallIdentifier, statements);

            statements.Clear();

            return method;
        }
    }
}

internal record GenericContext(
    TypeParameterSyntax EntityTypeParameter,
    TypeParameterSyntax OwnerIdTypeParameter,
    TypeSyntax EntityType,
    TypeSyntax OwnerIdType,
    ParameterSyntax QueryParameter,
    ParameterSyntax IdParameter)
{
    public static GenericContext Create()
    {
        var genericEntityTypeParameter = TypeParameter(Identifier("T"));
        var genericEntityType = ParseTypeName(genericEntityTypeParameter.Identifier.Text);
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
            genericOwnerIdTypeParameter,
            genericEntityType,
            genericOwnerIdType,
            queryParameter,
            idParameter);
    }

    public MethodDeclarationSyntax CreateMethod(
        SyntaxGenerationCache syntax,
        SyntaxToken methodName,
        IEnumerable<StatementSyntax> statements)
    {
        syntax.Parameters[0] = QueryParameter;
        syntax.Parameters[1] = IdParameter;
        var method = FluentExtensionMethod(methodName, syntax.Parameters);

        syntax.TypeParameters[0] = EntityTypeParameter;
        syntax.TypeParameters[1] = OwnerIdTypeParameter;
        method = method.WithTypeParameterList(TypeParameterList(SeparatedList(
            syntax.TypeParameters)));

        method = method.WithBody(Block(statements));

        return method;
    }

    public IfStatementSyntax CreateBranch(
        SyntaxGenerationCache syntax,
        MemberAccessExpressionSyntax memberToCall,
        NodeSyntaxCache nodeSyntaxCache)
    {
        var typeOfT = TypeOfExpression(EntityType);
        var typeOfEntity = TypeOfExpression(nodeSyntaxCache.EntityType);
        var typeCheck = BinaryExpression(
            SyntaxKind.EqualsExpression, typeOfT, typeOfEntity);

        var qIdentifier = Identifier("q");
        VariableDeclarationSyntax qDeclaration;
        {
            var qType = nodeSyntaxCache.QueryType;
            // q = (IQueryable<Entity1>) query;
            var qDeclarator = VariableDeclarator(qIdentifier)
                .WithInitializer(EqualsValueClause(
                    CastExpression(qType, IdentifierName(QueryParameter.Identifier))));
            // var q = (IQueryable<Entity1>) query;
            qDeclaration = VariableDeclaration(
                IdentifierName("var"), SingletonSeparatedList(qDeclarator));
        }

        // var id = Coerce<OwnerIdType, IdType>(ownerId);
        var idIdentifier = Identifier("id");
        VariableDeclarationSyntax idDeclaration;
        {
            syntax.TypeArguments[0] = OwnerIdType;
            syntax.TypeArguments[1] = nodeSyntaxCache.IdType;
            var coerceTypeArguments = TypeArgumentList(SeparatedList(syntax.TypeArguments));
            var coerceCall = InvocationExpression(
                GenericName(Identifier("Coerce"), coerceTypeArguments),
                ArgumentList(SingletonSeparatedList(Argument(
                    IdentifierName(IdParameter.Identifier)))));
            var idDeclarator = VariableDeclarator(idIdentifier)
                .WithInitializer(EqualsValueClause(coerceCall));
            idDeclaration = VariableDeclaration(
                IdentifierName("var"), SingletonSeparatedList(idDeclarator));
        }

        // return Class.Method(q, id);
        syntax.Arguments[0] = Argument(IdentifierName(qIdentifier));
        syntax.Arguments[1] = Argument(IdentifierName(idIdentifier));
        var invocation = InvocationExpression(memberToCall)
            .WithArgumentList(ArgumentList(SeparatedList(syntax.Arguments)));

        var returnStatement = ReturnStatement(invocation);

        syntax.Statements[0] = LocalDeclarationStatement(qDeclaration);
        syntax.Statements[1] = LocalDeclarationStatement(idDeclaration);
        syntax.Statements[2] = returnStatement;
        var block = Block(syntax.Statements);

        var ifStatement = IfStatement(typeCheck, block);
        return ifStatement;
    }
}
