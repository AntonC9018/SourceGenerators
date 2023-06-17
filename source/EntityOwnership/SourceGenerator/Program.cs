using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Extensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

/// <summary>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OwnershipGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: (c, _) => GetEntityTypeInfo(c))
            .Where(r => r is not null);

        var source = context.CompilationProvider.Combine(entities.Collect());

        context.RegisterSourceOutput(source, static (context, source) =>
        {
            var (compilation, entities) = source;

#pragma warning disable CS8620 // Wrong nullability. Compiler can't figure out that entities won't have nulls.
            var graph = CreateGraph(compilation, entities);
#pragma warning restore CS8620

            static MemberAccessExpressionSyntax PropertyAccess(
                ExpressionSyntax expression,
                IPropertySymbol property)
            {
                return MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    expression,
                    IdentifierName(property.Name));
            }

            static MemberAccessExpressionSyntax? GetOwnerIdExpression(GraphNode graphNode, ExpressionSyntax parent)
            {
                if (graphNode.OwnerIdProperty is not null)
                {
                    // e.OwnerId
                    return PropertyAccess(parent, graphNode.OwnerIdProperty);
                }
                else if (graphNode.OwnerNavigation is { } ownerNavigation)
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

            foreach (var graphNode in graph.Nodes)
                graphNode.SyntaxCache = SyntaxCache.Create(graphNode);

            var directOwnerFilterIdentifier = Identifier("DirectOwnerFilter");

            var overloadMethods = new List<MethodDeclarationSyntax>();
            var temp = new TempArrays();
            foreach (var graphNode in graph.Nodes)
            {
                var directOwnerFilterMethods = overloadMethods;

                if (graphNode is not
                    {
                        OwnerNode: { SyntaxCache: {} ownerSyntaxCache } ownerNode,
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
                ExpressionSyntax? lhsExpression = GetOwnerIdExpression(
                    graphNode, IdentifierName(parameter.Identifier));
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
                temp.Parameters[0] = syntaxCache.QueryParameter;
                temp.Parameters[1] = ownerSyntaxCache.IdParameter;
                var method = FluentExtensionMethod(directOwnerFilterIdentifier, temp.Parameters)
                    .WithBody(Block(returnStatement));

                directOwnerFilterMethods.Add(method);
                syntaxCache.DirectOwnerMethod = method;
            }

            static SimpleLambdaExpressionSyntax EqualsCheckLambda(
                ParameterSyntax parameter,
                ExpressionSyntax lhs,
                ExpressionSyntax rhs)
            {
                return SimpleLambdaExpression(
                    parameter, BinaryExpression(SyntaxKind.EqualsExpression, lhs, rhs));
            }

            static InvocationExpressionSyntax WhereInvocation(
                ExpressionSyntax parent,
                SimpleLambdaExpressionSyntax predicate)
            {
                return InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            parent,
                            IdentifierName("Where")))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(Argument(predicate))));
            }

            var rootOwnerFilterIdentifier = Identifier("RootOwnerFilter");
            foreach (var graphNode in graph.Nodes)
            {
                var rootOwnerFilterMethods = overloadMethods;

                if (graphNode.Cycle is not null)
                    continue;
                if (graphNode.SyntaxCache is not { } syntaxCache)
                    continue;

                temp.Parameters[0] = syntaxCache.QueryParameter;

                // If the node is itself the root, meaning it has no owner,
                // we have to check its own id.
                MethodDeclarationSyntax? method;
                if (graphNode.OwnerNode is not { } ownerNode)
                {
                    method = GetMethod_SelfIsRoot(syntaxCache);
                }
                else
                {
                    method = GetMethod_RegularOwnerIdChecks(graphNode, syntaxCache);
                }

                if (method is null)
                    continue;

                rootOwnerFilterMethods.Add(method);
                syntaxCache.RootOwnerMethod = method;


                MethodDeclarationSyntax? GetMethod_SelfIsRoot(SyntaxCache syntaxCache)
                {
                    if (graphNode.IdProperty is not { } idProperty)
                        return null;

                    temp.Parameters[1] = syntaxCache.IdParameter;
                    var method = FluentExtensionMethod(rootOwnerFilterIdentifier, temp.Parameters);
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
                    SyntaxCache syntaxCache)
                {
                    if (graphNode is not
                        {
                            OwnerNavigation: { } ownerNavigation,
                            RootOwner: { SyntaxCache: { } rootOwnerCache } rootOwnerNode,
                        })
                    {
                        return null;
                    }

                    temp.Parameters[1] = rootOwnerCache.IdParameter;
                    var method = FluentExtensionMethod(rootOwnerFilterIdentifier, temp.Parameters);

                    if (ReferenceEquals(graphNode, rootOwnerNode))
                    {
                        if (syntaxCache.DirectOwnerMethod is not { } directOwnerMethod)
                            return null;

                        // Just call the root filter:
                        // return DirectOwnerFilter(query, ownerId);
                        temp.Arguments[0] = Argument(IdentifierName(syntaxCache.QueryParameter.Identifier));
                        temp.Arguments[1] = Argument(IdentifierName(rootOwnerCache.IdParameter.Identifier));
                        var invocation = InvocationExpression(IdentifierName(directOwnerMethod.Identifier))
                            .WithArgumentList(ArgumentList(SeparatedList(temp.Arguments)));
                        return method.WithBody(Block(ReturnStatement(invocation)));
                    }

                    {
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
                            memberAccessChain = GetOwnerIdExpression(node, memberAccessChain);

                            return true;
                        }
                    }
                }
            }

            var overloadClass = ((ClassDeclarationSyntax) ParseMemberDeclaration("""
                    public static partial class EntityOwnershipOverloads{}
                """)!)
                .WithMembers(List<MemberDeclarationSyntax>(overloadMethods));

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

            var genericClassMembers = new List<MemberDeclarationSyntax>();
            var genericContext = GenericContext.Create();
            var statements = new List<StatementSyntax>(graph.Nodes.Length + 1);
            genericClassMembers.Add(CreateSwitchMethod(0));
            genericClassMembers.Add(CreateSwitchMethod(1));
            genericClassMembers.Add(CreateCoerceMethod());

            MethodDeclarationSyntax CreateSwitchMethod(int methodToCallIndex)
            {
                SyntaxToken methodToCallIdentifier = methodToCallIndex switch
                {
                    0 => directOwnerFilterIdentifier,
                    1 => rootOwnerFilterIdentifier,
                    _ => throw new ArgumentOutOfRangeException(nameof(methodToCallIndex))
                };
                var methodToCall = MethodAccess(
                    overloadClass.Identifier, methodToCallIdentifier);

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
                        temp, methodToCall, syntaxCache);
                    statements.Add(branch);
                }

                // throw new InvalidOperationException();
                statements.Add(ThrowStatement(ObjectCreationExpression(
                    IdentifierName("InvalidOperationException"))));

                var method = genericContext.CreateMethod(
                    temp, methodToCallIdentifier, statements);

                statements.Clear();

                return method;
            }

            MethodDeclarationSyntax CreateCoerceMethod()
            {
                var result = (MethodDeclarationSyntax) ParseMemberDeclaration("""
                    private static U Coerce<T, U>(T value)
                    {
                        if (value is not U u)
                            // TODO: throw new WrongIdTypeException(expected: typeof(U), actual: typeof(T));
                            throw new InvalidOperationException();

                        return u;
                    }
                """)!;
                return result;
            }
        });
    }

    public static MemberAccessExpressionSyntax MethodAccess(SyntaxToken parent, SyntaxToken methodName)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(parent),
            IdentifierName(methodName));
    }

    private static IfStatementSyntax GetBranch(
        TempArrays temp,
        MemberAccessExpressionSyntax methodToCall,
        ParameterSyntax query,
        ParameterSyntax ownerId,
        TypeSyntax genericEntityType,
        TypeSyntax genericOwnerIdType,
        SyntaxCache syntaxCache)
    {
        var typeOfT = TypeOfExpression(genericEntityType);
        var typeOfEntity = TypeOfExpression(syntaxCache.EntityType);
        var typeCheck = BinaryExpression(
            SyntaxKind.EqualsExpression, typeOfT, typeOfEntity);

        var qIdentifier = Identifier("q");
        VariableDeclarationSyntax qDeclaration;
        {
            var qType = syntaxCache.QueryType;
            // q = (IQueryable<Entity1>) query;
            var qDeclarator = VariableDeclarator(qIdentifier)
                .WithInitializer(EqualsValueClause(
                    CastExpression(qType, IdentifierName(query.Identifier))));
            // var q = (IQueryable<Entity1>) query;
            qDeclaration = VariableDeclaration(
                IdentifierName("var"), SingletonSeparatedList(qDeclarator));
        }

        var idIdentifier = Identifier("id");
        VariableDeclarationSyntax idDeclaration;
        {
            var coerceTypeArguments = TypeArgumentList(SeparatedList(new[]
            {
                genericOwnerIdType,
                syntaxCache.IdType,
            }));
            var coerceCall = InvocationExpression(
                GenericName(Identifier("Coerce"), coerceTypeArguments),
                ArgumentList(SingletonSeparatedList(Argument(
                    IdentifierName(ownerId.Identifier)))));
            var idDeclarator = VariableDeclarator(idIdentifier)
                .WithInitializer(EqualsValueClause(coerceCall));
            idDeclaration = VariableDeclaration(
                IdentifierName("var"), SingletonSeparatedList(idDeclarator));
        }

        var qualifiedMethodToCall = methodToCall;

        temp.Arguments[0] = Argument(IdentifierName(qIdentifier));
        temp.Arguments[1] = Argument(IdentifierName(idIdentifier));
        var invocation = InvocationExpression(qualifiedMethodToCall)
            .WithArgumentList(ArgumentList(SeparatedList(temp.Arguments)));

        var returnStatement = ReturnStatement(invocation);

        temp.Statements[0] = LocalDeclarationStatement(qDeclaration);
        temp.Statements[1] = LocalDeclarationStatement(idDeclaration);
        temp.Statements[2] = returnStatement;
        var block = Block(temp.Statements);

        var ifStatement = IfStatement(typeCheck, block);
        return ifStatement;
    }

    private static readonly SyntaxTokenList _PublicStatic = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
    });

    private static MethodDeclarationSyntax FluentExtensionMethod(
        SyntaxToken name, ParameterSyntax[] parameters)
    {
        var queryable = parameters[0];
        var method = MethodDeclaration(
            returnType: queryable.Type!,
            identifier: name);

        method = method.WithModifiers(_PublicStatic);

        parameters[0] = parameters[0].AddModifiers(Token(SyntaxKind.ThisKeyword));
        method = method.WithParameterList(ParameterList(SeparatedList(parameters)));

        return method;
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
            TempArrays temp,
            SyntaxToken methodName,
            IEnumerable<StatementSyntax> statements)
        {
            temp.Parameters[0] = QueryParameter;
            temp.Parameters[1] = IdParameter;
            var method = FluentExtensionMethod(methodName, temp.Parameters);

            temp.TypeParameters[0] = EntityTypeParameter;
            temp.TypeParameters[1] = OwnerIdTypeParameter;
            method = method.WithTypeParameterList(TypeParameterList(SeparatedList(
                temp.TypeParameters)));

            method = method.WithBody(Block(statements));

            return method;
        }

        public IfStatementSyntax CreateBranch(
            TempArrays temp,
            MemberAccessExpressionSyntax memberToCall,
            SyntaxCache syntaxCache)
        {
            return GetBranch(
                temp,
                memberToCall,
                QueryParameter,
                IdParameter,
                EntityType,
                OwnerIdType,
                syntaxCache);
        }
    }

    internal class TempArrays
    {
        public readonly StatementSyntax[] Statements = new StatementSyntax[3];
        public readonly ArgumentSyntax[] Arguments = new ArgumentSyntax[2];
        public readonly ParameterSyntax[] Parameters = new ParameterSyntax[2];
        public readonly TypeParameterSyntax[] TypeParameters = new TypeParameterSyntax[2];
    }

    private static Graph CreateGraph(Compilation compilation, ImmutableArray<OwnershipEntityTypeInfo> entities)
    {
        var mapping = new Dictionary<INamedTypeSymbol, GraphNode>(entities.Length, SymbolEqualityComparer.Default);
        var unlinkedNodes = new GraphNode[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            var entityInfo = entities[i]!;
            var type = compilation.GetTypeByMetadataName(entityInfo.Type.FullyQualifiedTypeName)!;

            IPropertySymbol? GetPropertyOrNull(string? name)
            {
                if (name is null)
                    return null;
                return type.GetMembers(name).OfType<IPropertySymbol>().First();
            }

            var idProperty = GetPropertyOrNull(entityInfo.Type.Id?.PropertyName);
            var ownerNavigationProperty = GetPropertyOrNull(entityInfo.OwnerType?.NavigationPropertyName);
            var ownerIdProperty = GetPropertyOrNull(entityInfo.OwnerType?.Id?.PropertyName);
            var ownerType = entityInfo.OwnerType?.FullyQualifiedTypeName is { } ownerTypeName
                ? compilation.GetTypeByMetadataName(ownerTypeName)
                : null;

            var graphNode = new GraphNode
            {
                Type = type,
                Source = entityInfo,
                OwnerType = ownerType,
                IdProperty = idProperty,
                OwnerNavigation = ownerNavigationProperty,
                OwnerIdProperty = ownerIdProperty,
            };
            unlinkedNodes[i] = graphNode;
            mapping.Add(type, graphNode);
        }

        var rootOwners = new List<GraphNode>();
        for (int i = 0; i < entities.Length; i++)
        {
            var graphNode = unlinkedNodes[i];
            if (graphNode.OwnerType is null)
            {
                rootOwners.Add(graphNode);
                continue;
            }
            if (!mapping.TryGetValue(graphNode.OwnerType, out var ownerGraphNode))
            {
                // TODO: offer diagnostic, if running in the analyzer.
                rootOwners.Add(graphNode);
                continue;
            }
            graphNode.OwnerNode = ownerGraphNode;
        }

        var graphNodes = unlinkedNodes;

        // Detect cycles
        {
            HashSet<GraphNode> cycle = new();
            foreach (var graphNode in graphNodes)
            {
                Recurse(graphNode);
                if (cycle.Count > 0)
                    cycle.Clear();

                void Recurse(GraphNode node)
                {
                    if (node.HasBeenProcessed)
                        return;
                    node.HasBeenProcessed = true;

                    if (!cycle.Add(node))
                    {
                        // TODO: cycle detected, report diagnostic.
                        foreach (var n in cycle)
                            n.Cycle = cycle;
                        cycle = new();
                        return;
                    }

                    if (node.OwnerNode is { } owner)
                        Recurse(owner);
                }
            }

            foreach (var graphNode in graphNodes)
                graphNode.HasBeenProcessed = false;
        }

        {
            foreach (var graphNode in graphNodes)
            {
                if (graphNode.Cycle is null)
                    SetRootOwner(graphNode);
            }

            void SetRootOwner(GraphNode node)
            {
                if (node.RootOwner is not null)
                    return;
                if (node.OwnerNode is not { } owner)
                    return;
                SetRootOwner(owner);
                node.RootOwner = owner.RootOwner ?? owner;
            }
        }

        return new Graph
        {
            Nodes = graphNodes,
            Mapping = mapping,
            RootOwners = rootOwners,
        };
    }

    internal record SyntaxCache(
        TypeSyntax EntityType,
        ParameterSyntax IdParameter,
        ParameterSyntax QueryParameter)
    {
        public MethodDeclarationSyntax? DirectOwnerMethod { get; set; }
        public MethodDeclarationSyntax? RootOwnerMethod { get; set; }

        public TypeSyntax QueryType => QueryParameter.Type!;
        public TypeSyntax IdType => IdParameter.Type!;

        public static SyntaxCache? Create(GraphNode node)
        {
            string fullyQualifiedIdPropertyName;
            {
                if (node.Source.Type.Id is { } id)
                    fullyQualifiedIdPropertyName = id.PropertyName;
                else if (node.IdProperty is { } idProperty)
                    fullyQualifiedIdPropertyName = idProperty.Name;
                else
                    return null;
            }
            string fullyQualifiedTypeName = node.Source.Type.FullyQualifiedTypeName;
            var entityTypeSyntax = ParseTypeName(fullyQualifiedTypeName);
            var idTypeSyntax = ParseTypeName(fullyQualifiedIdPropertyName);
            var queryTypeName = GenericName(
                Identifier("IQueryable"),
                TypeArgumentList(SingletonSeparatedList(entityTypeSyntax)));

            var queryParameter = Parameter(Identifier("query"))
                .WithType(queryTypeName);
            var idParameter = Parameter(Identifier("ownerId"))
                .WithType(idTypeSyntax);

            return new SyntaxCache(entityTypeSyntax, idParameter, queryParameter);
            //
            // if (node.OwnerNode is not { } ownerNode)
            //     return null;
            //
            // if (GetOwnerIdTypeName() is not { } ownerIdTypeName)
            //     return null;
            // var ownerIdParameter = Parameter(Identifier("ownerId"))
            //     .WithType(ParseTypeName(ownerIdTypeName));
            //
            // var parameters = ImmutableArray.Create(queryParameter, ownerIdParameter);
            // var parameterList = ParameterList(SeparatedList(parameters));
            //
            // return new SyntaxCache(parameters, parameterList);
            //
            // string? GetOwnerIdTypeName()
            // {
            //     if (ownerNode.Source.Type.Id is { } ownerId)
            //         return ownerId.FullyQualifiedTypeName;
            //     else if (node.Source.OwnerType!.Value.Id is { } idToOwner)
            //         return idToOwner.FullyQualifiedTypeName;
            //     else if (ownerNode.IdProperty is { } ownerIdProperty1)
            //         return ownerIdProperty1.ToDisplayString();
            //     else if (node.OwnerIdProperty is { } ownerIdProperty2)
            //         return ownerIdProperty2.ToDisplayString();
            //     else
            //         return null;
            // }
        }
    }

    internal class Graph
    {
        public required GraphNode[] Nodes { get; init; }
        public required List<GraphNode> RootOwners { get; init; }
        public required Dictionary<INamedTypeSymbol, GraphNode> Mapping { get; init; }
    }

    internal class GraphNode
    {
        public required INamedTypeSymbol Type { get; init; }
        public required OwnershipEntityTypeInfo Source { get; init; }
        public required IPropertySymbol? IdProperty { get; init; }
        public required INamedTypeSymbol? OwnerType { get; init; }
        public required IPropertySymbol? OwnerNavigation { get; init; }
        public required IPropertySymbol? OwnerIdProperty { get; init; }
        public GraphNode? OwnerNode { get; set; }
        public GraphNode? RootOwner { get; set; }

        // Can be used internally as a flag.
        public bool HasBeenProcessed { get; set; }

        // If the node is part of a cycle, this property will be set.
        public HashSet<GraphNode>? Cycle { get; set; }

        // Used internally by the syntax generator
        internal SyntaxCache? SyntaxCache { get; set; }
        internal TypeSyntax? IdTypeSyntax { get; set; }

        public override bool Equals(object? other)
        {
            if (other is not GraphNode otherNode)
                return false;
            return otherNode.Type.Equals(Type, SymbolEqualityComparer.Default);
        }

        public override int GetHashCode()
        {
            return SymbolEqualityComparer.Default.GetHashCode(Type);
        }
    }

    internal record OwnershipEntityTypeInfo
    {
        public record struct IdInfo
        {
            public required string FullyQualifiedTypeName { get; init; }
            public required string PropertyName { get; init; }
        }
        public record struct OwnerInfo
        {
            public required string FullyQualifiedTypeName { get; init; }
            public required IdInfo? Id { get; init; }
            public required string? NavigationPropertyName { get; init; }
        }
        public record struct IdAndTypeInfo
        {
            public required string FullyQualifiedTypeName { get; init; }
            public required IdInfo? Id { get; init; }
        }

        public required OwnerInfo? OwnerType { get; init; }
        public required IdAndTypeInfo Type { get; init; }

        public bool IsOwned => OwnerType is not null;
        public bool IsOwner => OwnerType is null;
    }

    private static OwnershipEntityTypeInfo? GetEntityTypeInfo(GeneratorSyntaxContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        var classDeclaration = (ClassDeclarationSyntax) context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration)!;

        var iownedInterface = compilation.GetTypeByMetadataName(typeof(IOwned<>).FullName);
        var iownedImplementation = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.ConstructedFrom.Equals(iownedInterface, SymbolEqualityComparer.Default));

        var iownerInterface = compilation.GetTypeByMetadataName(typeof(IOwner).FullName);
        var iownerImplementation = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.Equals(iownerInterface, SymbolEqualityComparer.Default));

        if (iownedImplementation is null && iownerImplementation is null)
            return null;

        OwnershipEntityTypeInfo.IdAndTypeInfo typeInfo;
        {
            const string idPropertyName = "Id";
            var idProperty = classSymbol
                .GetMembers(idPropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            typeInfo = new()
            {
                FullyQualifiedTypeName = classSymbol.GetFullyQualifiedMetadataName(),
                Id = idProperty is null ? null : new()
                {
                    PropertyName = idProperty.Name,
                    FullyQualifiedTypeName = idProperty.Type.GetFullyQualifiedMetadataName(),
                },
            };
        }

        OwnershipEntityTypeInfo.OwnerInfo? ownerTypeInfo = null;
        if (iownedImplementation is { } impl)
        {
            var ownerType = impl.TypeArguments[0];
            var ownerTypeFullyQualifiedName = ownerType.GetFullyQualifiedMetadataName();

            var navigationPropertyName = ownerType.Name;
            var navigationProperty = classSymbol
                .GetMembers(navigationPropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(i => i.Type.Equals(ownerType, SymbolEqualityComparer.Default));

            var idPropertyName = $"{navigationPropertyName}Id";
            var idProperty = classSymbol
                .GetMembers(idPropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            ownerTypeInfo = new()
            {
                FullyQualifiedTypeName = ownerTypeFullyQualifiedName,
                Id = idProperty is null ? null : new()
                {
                    FullyQualifiedTypeName = idProperty.Type.GetFullyQualifiedMetadataName(),
                    PropertyName = idProperty.Name,
                },
                NavigationPropertyName = navigationProperty?.Name,
            };
        }

        var info = new OwnershipEntityTypeInfo
        {
            OwnerType = ownerTypeInfo,
            Type = typeInfo,
        };

        return info;
    }

    internal record Info
    {

    }
}
