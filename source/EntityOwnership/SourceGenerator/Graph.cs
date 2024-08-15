using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using SourceGeneration.Extensions;

namespace EntityOwnership.SourceGenerator;

using static Diagnostics;

internal class Graph
{
    public required GraphNode[] Nodes { get; init; }
    public required List<GraphNode> RootOwners { get; init; }
    public required Dictionary<INamedTypeSymbol, GraphNode> Mapping { get; init; }
    public required List<Diagnostic> Diagnostics { get; init; }

    public static Graph Create(
        Compilation compilation, ImmutableArray<OwnershipEntityTypeInfo> entitiesInput)
    {
        HashSet<string> foundKeys = new();
        // Partial types sometimes produce duplicates of the same symbol
        var entities = entitiesInput.Where(
                e => foundKeys.Add(e.Type.TypeMetadataName))
            // TODO: Remove the memory allocation here.
            .ToArray();

        var mapping = new Dictionary<INamedTypeSymbol, GraphNode>(entities.Length, SymbolEqualityComparer.Default);
        var unlinkedNodes = new GraphNode[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            var entityInfo = entities[i];
            var type = compilation.GetTypeByMetadataName(entityInfo.Type.TypeMetadataName)!;

            IPropertySymbol? GetPropertyOrNull(string? name)
            {
                if (name is null)
                {
                    return null;
                }

                return type
                    .GetMembersEvenIfUnimplemented(name)
                    .OfType<IPropertySymbol>()
                    .First();
            }

            var idProperty = GetPropertyOrNull(entityInfo.Type.Id?.PropertyName);
            var ownerNavigationProperty = GetPropertyOrNull(entityInfo.OwnerType?.NavigationPropertyName);
            var ownerIdProperty = GetPropertyOrNull(entityInfo.OwnerType?.Id?.PropertyName);
            var ownerType = entityInfo.OwnerType?.TypeMetadataName is { } ownerTypeReference
                ? compilation.GetTypeByMetadataName(ownerTypeReference)
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
            mapping.Add(graphNode.Type, graphNode);
        }

        var diagnostics = new List<Diagnostic>();

        // Link nodes to owner nodes.
        var rootOwners = new List<GraphNode>();
        for (int i = 0; i < entities.Length; i++)
        {
            var graphNode = unlinkedNodes[i];
            if (graphNode.OwnerType is not { } ownerType)
            {
                rootOwners.Add(graphNode);
                continue;
            }
            if (!mapping.TryGetValue(ownerType, out var ownerGraphNode))
            {
                diagnostics.Add(Diagnostic.Create(
                    NoOwnerTypeInGraph,
                    graphNode.Type.Locations[0],
                    ownerType.Name,
                    graphNode.Type.Name));
                continue;
            }
            if (ReferenceEquals(ownerGraphNode, graphNode))
            {
                diagnostics.Add(Diagnostic.Create(
                    TypeHasItselfAsOwner,
                    graphNode.Type.Locations[0],
                    graphNode.Type.Name));
                continue;
            }
            graphNode.OwnerNode = ownerGraphNode;
            ownerGraphNode.DirectChildren.Add(graphNode);
        }
        var graphNodes = unlinkedNodes;

        // Detect cycles.
        {
            List<GraphNode> orderedCycle = new();
            HashSet<GraphNode> cycle = new();

            foreach (var graphNode in graphNodes)
            {
                if (graphNode.Cycle is null)
                {
                    Recurse(graphNode);
                }

                cycle.Clear();
                orderedCycle.Clear();

                void Recurse(GraphNode node)
                {
                    if (!cycle.Add(node))
                    {
                        string diagnosticCycle = string.Join(" -> ", cycle.Select(n => n.Type.Name));

                        foreach (var n in orderedCycle)
                        {
                            n.Cycle = orderedCycle;

                            var diagnostic = Diagnostic.Create(
                                TypeWasPartOfCycle,
                                n.Type.Locations[0],
                                diagnosticCycle);
                            diagnostics.Add(diagnostic);
                        }

                        orderedCycle = new();
                        cycle.Clear();
                        return;
                    }

                    orderedCycle.Add(node);

                    if (node.OwnerNode is { } owner)
                    {
                        Recurse(owner);
                    }
                }
            }
        }

        {
            foreach (var graphNode in graphNodes)
            {
                if (graphNode.Cycle is null)
                {
                    SetRootOwner(graphNode);
                }
            }

            void SetRootOwner(GraphNode node)
            {
                if (node.RootOwnerNode is not null)
                {
                    return;
                }

                if (node.OwnerNode is not { } owner)
                {
                    return;
                }

                // Could happen if something went wrong previously
                if (ReferenceEquals(owner, node))
                {
                    return;
                }

                SetRootOwner(owner);
                node.RootOwnerNode = owner.RootOwnerNode ?? owner;
            }
        }

        return new Graph
        {
            Nodes = graphNodes,
            Mapping = mapping,
            RootOwners = rootOwners,
            Diagnostics = diagnostics,
        };
    }
}

[DebuggerDisplay("{Type}")]
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
    public bool OwnerNotInGraph => OwnerType is not null && OwnerNode is null;

    public List<GraphNode> DirectChildren { get; } = new();

    // Can be used internally as a flag.
    public bool HasBeenProcessed { get; set; }

    // If the node is part of a cycle, this property will be set.
    public List<GraphNode>? Cycle { get; set; }

    // Used internally by the syntax generator
    internal NodeSyntaxCache? SyntaxCache { get; set; }

    public override bool Equals(object? other)
    {
        if (other is not GraphNode otherNode)
        {
            return false;
        }

        return otherNode.Type.Equals(Type, SymbolEqualityComparer.Default);
    }

    public override int GetHashCode()
    {
        return SymbolEqualityComparer.Default.GetHashCode(Type);
    }
}

internal static class GraphNodeExtensions
{
    public static IEnumerable<GraphNode> GetAllDependentNodesDepthFirst(this GraphNode node)
    {
        foreach (var child in node.DirectChildren)
        {
            // I'm pretty sure the children can't be part of a cycle,
            // since it's checked via the owner.
            Debug.Assert(child.Cycle is null);

            foreach (var descendant in GetAllDependentNodesDepthFirst(child))
            {
                yield return descendant;
            }
        }

        foreach (var child in node.DirectChildren)
        {
            yield return child;
        }
    }
}
