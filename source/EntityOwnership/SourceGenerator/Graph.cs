using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityOwnership.SourceGenerator;

internal class Graph
{
    public required GraphNode[] Nodes { get; init; }
    public required List<GraphNode> RootOwners { get; init; }
    public required Dictionary<INamedTypeSymbol, GraphNode> Mapping { get; init; }
    public required List<HashSet<GraphNode>> Cycles { get; init; }


    public static Graph Create(Compilation compilation, ImmutableArray<OwnershipEntityTypeInfo> entities)
    {
        var mapping = new Dictionary<INamedTypeSymbol, GraphNode>(entities.Length, SymbolEqualityComparer.Default);
        var unlinkedNodes = new GraphNode[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            var entityInfo = entities[i];
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

        // Link nodes to owner nodes.
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

        // Detect cycles.
        List<HashSet<GraphNode>> cycles = new();
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
                        cycles.Add(cycle);
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
                if (node.RootOwnerNode is not null)
                    return;
                if (node.OwnerNode is not { } owner)
                    return;
                SetRootOwner(owner);
                node.RootOwnerNode = owner.RootOwnerNode ?? owner;
            }
        }

        return new Graph
        {
            Nodes = graphNodes,
            Mapping = mapping,
            RootOwners = rootOwners,
            Cycles = cycles,
        };
    }
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
    public GraphNode? RootOwnerNode { get; set; }

    // Can be used internally as a flag.
    public bool HasBeenProcessed { get; set; }

    // If the node is part of a cycle, this property will be set.
    public HashSet<GraphNode>? Cycle { get; set; }

    // Used internally by the syntax generator
    internal NodeSyntaxCache? SyntaxCache { get; set; }
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
