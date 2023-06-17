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
            var tempParams = new ParameterSyntax[2];
            var tempArguments = new ArgumentSyntax[2];
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

                 IQueryable<Entity> DirectOwnerFilter(IQueryable<Entity> query, ID ownerId)
                 {
                    return query.Where(e => e.OwnerId == ownerId);
                 }

                 */

                var parameter = Parameter(Identifier("e"));
                ExpressionSyntax? lhsExpression = GetOwnerIdExpression(graphNode, IdentifierName(parameter.Identifier));
                if (lhsExpression is null)
                    continue;

                // e => e.OwnerId == ownerId
                var lambda = SimpleLambdaExpression(
                    parameter,
                    BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        lhsExpression,
                        IdentifierName(ownerSyntaxCache.IdParameter.Identifier)));

                // query.Where(e => e.OwnerId == ownerId)
                var whereCall = InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(syntaxCache.QueryParameter.Identifier),
                            IdentifierName("Where")))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(Argument(lambda))));

                // return ...
                var returnStatement = ReturnStatement(whereCall);

                // IQueryable<Entity> DirectOwnerFilter(IQueryable<Entity> query, ID ownerId)
                var method = MethodDeclaration(
                    returnType: syntaxCache.QueryType,
                    identifier: directOwnerFilterIdentifier)

                    .WithBody(Block(returnStatement));

                tempParams[0] = syntaxCache.QueryParameter;
                tempParams[1] = ownerSyntaxCache.IdParameter;
                method = method.WithParameterList(ParameterList(SeparatedList(tempParams)));

                directOwnerFilterMethods.Add(method);
            }

            var rootOwnerFilterIdentifier = Identifier("RootOwnerFilter");
            foreach (var graphNode in graph.Nodes)
            {
                var rootOwnerFilterMethods = overloadMethods;

                if (graphNode is not
                    {
                        Cycle: null,
                        OwnerNavigation: { } ownerNavigation,
                        SyntaxCache: { } syntaxCache,
                        OwnerNode: {  } ownerNode,
                        RootOwner: { SyntaxCache: { } rootOwnerCache } rootOwnerNode,
                    })
                {
                    continue;
                }

                var method = MethodDeclaration(
                    returnType: syntaxCache.QueryType,
                    identifier: rootOwnerFilterIdentifier);

                tempParams[0] = syntaxCache.QueryParameter;
                tempParams[1] = rootOwnerCache.IdParameter;
                method = method.WithParameterList(ParameterList(SeparatedList(tempParams)));

                if (ReferenceEquals(ownerNode, rootOwnerNode))
                {
                    // Just call the root filter:
                    // return DirectOwnerFilter(query, ownerId);
                    tempArguments[0] = Argument(IdentifierName(syntaxCache.QueryParameter.Identifier));
                    tempArguments[1] = Argument(IdentifierName(rootOwnerCache.IdParameter.Identifier));
                    var invocation = InvocationExpression(IdentifierName(directOwnerFilterIdentifier))
                        .WithArgumentList(ArgumentList(SeparatedList(tempArguments)));
                    method = method.WithBody(Block(ReturnStatement(invocation)));
                }
                else
                {
                    var entityParameter = Parameter(Identifier("e"));
                    var parentExpression = IdentifierName(entityParameter.Identifier);
                    var memberAccessChain = PropertyAccess(parentExpression, ownerNavigation);

                    if (!CompleteMemberAccessChain())
                        continue;

                    bool CompleteMemberAccessChain()
                    {
                        GraphNode node = ownerNode;
                        GraphNode nodeOwner = ownerNode.OwnerNode!;

                        while (true)
                        {
                            if (node.OwnerNavigation is not { } ownerOwnerNavigation)
                                return false;
                            memberAccessChain = PropertyAccess(memberAccessChain, node.OwnerNavigation);

                            if (nodeOwner.OwnerNode is not { } potentialRoot)
                                break;

                            node = nodeOwner;
                            nodeOwner = potentialRoot;
                        }

                        // nodeOwner is now the root node, so we can reach for the owner id instead of the navigation.
                        memberAccessChain = GetOwnerIdExpression(nodeOwner, memberAccessChain);

                        return true;
                    }
                }

                rootOwnerFilterMethods.Add(method);
            }

            var overloadClass = ((ClassDeclarationSyntax) ParseMemberDeclaration("""
                    public static partial class EntityOwnershipOverloads{}
                """)!)
                .WithMembers(List<MemberDeclarationSyntax>(overloadMethods));

            // Now the switch

        });
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
        public ParameterListSyntax? MethodParameterList { get; set; }
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
                TypeArgumentList(
                    SeparatedList(new[] { entityTypeSyntax })));

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
