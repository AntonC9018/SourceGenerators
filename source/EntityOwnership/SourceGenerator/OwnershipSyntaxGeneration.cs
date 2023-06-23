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

        var cache = new SyntaxGenerationCache();
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

            var parameter = syntaxCache.LambdaParameter;
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
            using var parameters = cache.Parameters.Borrow();
            parameters.Add(syntaxCache.QueryParameter);
            parameters.Add(ownerSyntaxCache.IdParameter);
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

            parameters.Add(syntaxCache.IdParameter);
            var method = FluentExtensionMethod(
                StaticSyntaxCache.RootOwnerFilterIdentifier, parameters);

            // e => e.Id == ownerId
            var parameter = syntaxCache.LambdaParameter;
            var lambda = EqualsCheckLambda(
                parameter,
                PropertyAccess(IdentifierName(parameter.Identifier), idProperty),
                IdentifierName(syntaxCache.IdParameter.Identifier));
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
                    RootOwnerNode: { SyntaxCache: { } rootOwnerCache } rootOwnerNode,
                })
            {
                return null;
            }

            parameters.Add(rootOwnerCache.IdParameter);
            var method = FluentExtensionMethod(
                StaticSyntaxCache.RootOwnerFilterIdentifier, parameters);

            if (ReferenceEquals(ownerNode, rootOwnerNode))
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

            var parentExpression = IdentifierName(syntaxCache.LambdaParameter.Identifier);
            var memberAccessChain = PropertyAccess(parentExpression, ownerNavigation);

            if (!CompleteMemberAccessChain())
                return null;

            var lambda = EqualsCheckLambda(
                syntaxCache.LambdaParameter,
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
        outResult.Add(CreateSomeOwnerFilterMethod());
        outResult.Add(StaticSyntaxCache.CoerceMethod);

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
                if (ownerNode?.SyntaxCache is not { } ownerSyntaxCache)
                    continue;

                var branch = genericContext.CreateBranch(
                    cache, methodToCall, syntaxCache, ownerSyntaxCache);
                statements.Add(branch);
            }

            statements.Add(ThrowInvalidOperationStatement);

            var methodName = methodIndex switch
            {
                0 => StaticSyntaxCache.DirectOwnerFilterTIdentifier,
                1 => StaticSyntaxCache.RootOwnerFilterTIdentifier,
                _ => throw new ArgumentOutOfRangeException(nameof(methodIndex))
            };

            var method = genericContext.CreateMethod(cache, methodName)
                .TypeParams2();

            return method;
        }

        MethodDeclarationSyntax CreateSomeOwnerFilterMethod()
        {
            using var statements = cache.Statements.Borrow();
            foreach (var graphNode in graph.Nodes)
            {
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                if (graphNode.Cycle is not null)
                    continue;
                // var q = (IQueryable<Entity1>) query;
                var q = genericContext.QDeclaration(syntaxCache);
                var startExpression = IdentifierName(syntaxCache.LambdaParameter.Identifier);
                var typeofGenericOwner = TypeOfExpression(genericContext.OwnerType);
                using var statements2 = cache.Statements2.Borrow();

                statements2.Add(LocalDeclarationStatement(q.Declaration));

                foreach (var (ownerNode, idAccess) in GetIdNavigations(startExpression, graphNode))
                {
                    if (ownerNode.SyntaxCache is not { } ownerSyntaxCache)
                        continue;
                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        typeofGenericOwner,
                        TypeOfExpression(ownerSyntaxCache.EntityType));
                    var id = genericContext.IdDeclaration(cache, ownerSyntaxCache);

                    var lambda = EqualsCheckLambda(
                        syntaxCache.LambdaParameter,
                        idAccess, IdentifierName(id.Identifier));
                    var whereCall = WhereInvocation(
                        IdentifierName(q.Identifier), lambda);
                    var castedWhereCall = CastExpression(
                        genericContext.QueryParameter.Type!, whereCall);
                    var returnStatement = ReturnStatement(castedWhereCall);

                    using var statements3 = cache.Statements3.Borrow();
                    statements3.Add(LocalDeclarationStatement(id.Declaration));
                    statements3.Add(returnStatement);
                    var block = Block(statements3);

                    var ifStatement = IfStatement(typeCheck, block);
                    statements2.Add(ifStatement);
                }

                {
                    statements2.Add(ThrowInvalidOperationStatement);

                    var typeCheck = BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        TypeOfExpression(genericContext.EntityType),
                        TypeOfExpression(syntaxCache.EntityType));
                    var ifStatement = IfStatement(typeCheck, Block(statements2));

                    statements.Add(ifStatement);
                }

                // q --> q.Navigation.Id
                IEnumerable<(GraphNode OwnerNode, ExpressionSyntax IdAccess)> GetIdNavigations(
                    ExpressionSyntax start, GraphNode leaf)
                {
                    if (leaf.IdProperty is { } leafIdProperty)
                        yield return (leaf, PropertyAccess(start, leafIdProperty));
                    if (leaf.OwnerNode is not { } ownerNode)
                        yield break;

                    GraphNode node = graphNode;
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

                        yield return (ownerNode, ownerIdAccess);

                        if (chainToParent is null)
                            yield break;

                        node = ownerNode;
                        ownerNode = nextOwnerNode;
                        chainToNode = chainToParent;
                    }

                    if (node.GetOwnerIdExpression(chainToNode) is { } idAccess)
                        yield return (ownerNode, idAccess);
                }
            }

            statements.Add(ThrowInvalidOperationStatement);
            var method = genericContext.CreateMethod(cache, Identifier("SomeOwnerFilterT"))
                .TypeParams3();
            return method;
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
        var typeType = ParseTypeName(typeof(Type).FullName);
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
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;
                var typeCheck = BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    IdentifierName("entityType"),
                    TypeOfExpression(syntaxCache.EntityType));
                var returnIdType = ReturnStatement(
                    TypeOfExpression(syntaxCache.IdType));
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

        // SupportsSomeOwnerFilter(Type entity, Type owner) => bool
        void AddSupportsSomeOwnerFilterMethods()
        {
            var ownerTypeParameter = Parameter(Identifier("ownerType"))
                .WithType(typeType);
            var idTypeParameter = Parameter(Identifier("idType"))
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
    ParameterSyntax IdParameter)
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

        public readonly MethodDeclarationSyntax TypeParams2()
        {
            using var typeParams = _cache.TypeParameters.Borrow();
            typeParams.Add(_context.EntityTypeParameter);
            typeParams.Add(_context.OwnerIdTypeParameter);
            var method = _method.WithTypeParameterList(TypeParameterList(SeparatedList(
                typeParams)));
            return method;
        }

        public readonly MethodDeclarationSyntax TypeParams3()
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

    public MethodBuilder CreateMethod(SyntaxGenerationCache cache, SyntaxToken methodName)
    {
        using var parameters = cache.Parameters.Borrow();
        parameters.Add(QueryParameter);
        parameters.Add(IdParameter);
        var method = FluentExtensionMethod(methodName, parameters);

        method = method.WithBody(Block(cache.Statements));

        return new MethodBuilder(this, cache, method);
    }

    public IfStatementSyntax CreateBranch(
        SyntaxGenerationCache syntax,
        MemberAccessExpressionSyntax memberToCall,
        NodeSyntaxCache syntaxCache,
        NodeSyntaxCache ownerSyntaxCache)
    {
        var typeOfT = TypeOfExpression(EntityType);
        var typeOfEntity = TypeOfExpression(syntaxCache.EntityType);
        var typeCheck = BinaryExpression(
            SyntaxKind.EqualsExpression, typeOfT, typeOfEntity);

        // var q = (IQueryable<Entity1>) query;
        var (qIdentifier, qDeclaration) = QDeclaration(syntaxCache);
        // var id = Coerce<TId, OwnerIdType>(ownerId);
        var (idIdentifier, idDeclaration) = IdDeclaration(syntax, ownerSyntaxCache);

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
        NodeSyntaxCache ownerSyntaxCache)
    {
        var idIdentifier = Identifier("id");
        syntax.TypeArguments[0] = OwnerIdType;
        syntax.TypeArguments[1] = ownerSyntaxCache.IdType;
        var coerceTypeArguments = TypeArgumentList(SeparatedList(syntax.TypeArguments));
        var coerceCall = InvocationExpression(
            GenericName(Identifier("Coerce"), coerceTypeArguments),
            ArgumentList(SingletonSeparatedList(Argument(
                IdentifierName(IdParameter.Identifier)))));
        var idDeclarator = VariableDeclarator(idIdentifier)
            .WithInitializer(EqualsValueClause(coerceCall));
        var idDeclaration = VariableDeclaration(
            IdentifierName("var"), SingletonSeparatedList(idDeclarator));
        return new(idIdentifier, idDeclaration);
    }
}
