using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AutoConstructor.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

[DebuggerDisplay("{EntityType}; {IdType}")]
internal record NodeSyntaxCache(
    TypeSyntax EntityType,
    ParameterSyntax? IdParameter,
    ParameterSyntax QueryParameter,
    ParameterSyntax LambdaParameter,
    string EscapedEntityTypeName)
{
    public MethodDeclarationSyntax? DirectOwnerMethod { get; set; }
    public MethodDeclarationSyntax? RootOwnerMethod { get; set; }

    // entityType == typeof(Entity)
    public BinaryExpressionSyntax? EntityTypeCheck { get; set; }

    public TypeSyntax QueryType => QueryParameter.Type!;
    public TypeSyntax? IdType => IdParameter?.Type;

    public List<(GraphNode OwnerNode, string Name)> OwnerIdAccesses { get; } = new();
    public List<(GraphNode OwnerNode, string Name)> OwnerAccesses { get; } = new();

    public List<(GraphNode OwnerNode, string Name)> GetOwnerAccessesList(OwnerExpressionKind kind) =>
        kind switch
        {
            OwnerExpressionKind.Id => OwnerIdAccesses,
            OwnerExpressionKind.Owner => OwnerAccesses,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static NodeSyntaxCache? Create(GraphNode node)
    {
        string? fullyQualifiedIdTypeName;
        {
            if (node.IdProperty is { } idProperty)
                fullyQualifiedIdTypeName = idProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            else if (node.Source.Type.Id is { } id)
                fullyQualifiedIdTypeName = id.Type.FullyQualifiedName;
            else
                fullyQualifiedIdTypeName = null;
        }
        string metadataName = node.Source.Type.TypeMetadataName;
        // NOTE: parsing the metadata here should be fine, since we only consider concrete types, not generic ones.
        var entityTypeSyntax = ParseTypeName(metadataName);
        var idTypeSyntax = fullyQualifiedIdTypeName is null ? null : ParseTypeName(fullyQualifiedIdTypeName);
        var queryTypeName = GenericName(
            Identifier("IQueryable"),
            TypeArgumentList(SingletonSeparatedList(entityTypeSyntax)));

        var queryParameter = Parameter(Identifier("query"))
            .WithType(queryTypeName);
        var idParameter = idTypeSyntax is null
            ? null
            : Parameter(Identifier("ownerId")).WithType(idTypeSyntax);

        var firstLetter = node.Type.Name[..1].ToLowerInvariant();
        var lambdaParameter = Parameter(Identifier(firstLetter))
            .WithType(entityTypeSyntax);

        var escapedEntityTypeName = metadataName.Replace(".", "_");

        return new NodeSyntaxCache(
            entityTypeSyntax,
            idParameter,
            queryParameter,
            lambdaParameter,
            escapedEntityTypeName);
    }

    public string GetOwnerAccessName(NodeSyntaxCache ownerSyntaxCache, OwnerExpressionKind kind) =>
        kind switch
        {
            OwnerExpressionKind.Id => $"Id__{EscapedEntityTypeName}__{ownerSyntaxCache.EscapedEntityTypeName}",
            OwnerExpressionKind.Owner => $"Owner__{EscapedEntityTypeName}__{ownerSyntaxCache.EscapedEntityTypeName}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };


    public string GetDependentTypesArrayName()
    {
        return $"DependentTypes__{EscapedEntityTypeName}";
    }
}

public enum OwnerExpressionKind
{
    Id,
    Owner,
}

public sealed class BorrowableList<T> : IEnumerable<T>, IDisposable
{
    private bool IsInUse { get; set; }
    private List<T> List { get; } = new();
    public void Dispose()
    {
        List.Clear();
        IsInUse = false;
    }
    public BorrowableList<T> Borrow()
    {
        static void Throw()
        {
            throw new InvalidOperationException("Cannot borrow a list that is already in use.");
        }

        if (IsInUse)
            Throw();
        IsInUse = true;
        return this;
    }
    public void Add(T item)
    {
        static void Throw()
        {
            throw new InvalidOperationException(
                "Cannot add to a list that is not in use. " +
                "Call Borrow() before adding items to the list.");
        }

        if (!IsInUse)
            Throw();
        List.Add(item);
    }
    public IEnumerator<T> GetEnumerator() => List.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public static implicit operator List<T>(BorrowableList<T> list) => list.List;
}

internal class SyntaxGenerationCache
{
    public static readonly ThreadLocal<SyntaxGenerationCache> Instance = new(() => new SyntaxGenerationCache());

    public readonly ArgumentSyntax[] Arguments = new ArgumentSyntax[2];
    public readonly TypeSyntax[] TypeArguments = new TypeSyntax[2];

    public readonly BorrowableList<ParameterSyntax> Parameters = new();
    public readonly BorrowableList<TypeParameterSyntax> TypeParameters = new();
    public readonly BorrowableList<StatementSyntax> Statements = new();
    public readonly BorrowableList<StatementSyntax> Statements2 = new();
    public readonly BorrowableList<StatementSyntax> Statements3 = new();
}

internal static class StaticSyntaxCache
{
    // This is probably not required, because roslyn has got to already cache these,
    // I'm doing this for the sole purpose of writing less strings in the source code.
    public static readonly SyntaxToken DirectOwnerFilterIdentifier = Identifier("DirectOwnerFilter");
    public static readonly SyntaxToken RootOwnerFilterIdentifier = Identifier("RootOwnerFilter");
    public static readonly SyntaxToken DirectOwnerFilterTIdentifier = Identifier("DirectOwnerFilterT");
    public static readonly SyntaxToken RootOwnerFilterTIdentifier = Identifier("RootOwnerFilterT");
    public static readonly SyntaxToken OverloadsClassIdentifier = Identifier("EntityOwnershipOverloads");
    public static readonly SyntaxToken GenericMethodsClassIdentifier = Identifier("EntityOwnershipGenericMethods");
    public static readonly SyntaxToken HelperClassIdentifier = Identifier("EntityOwnershipHelper");
    public static readonly SyntaxToken GetOwnerIdExpressionIdentifier = Identifier("GetOwnerIdExpression");
    public static readonly SyntaxToken GetOwnerExpressionIdentifier = Identifier("GetOwnerExpression");
    public static readonly SyntaxToken TrySetOwnerIdIdentifier = Identifier("TrySetOwnerId");
    public static readonly SyntaxToken GetDependentTypesIdentifier = Identifier("GetDependentTypes");
    public static readonly SyntaxToken GenericSomeOwnerFilterIdentifier = Identifier("SomeOwnerFilterT");
    public static readonly SyntaxToken GenericGetSomeOwnerFilterIdentifier = Identifier("GetSomeOwnerFilterT");

    public static SyntaxToken GetGetOwnerExpressionIdentifier(OwnerExpressionKind expressionKind) =>
        expressionKind switch
        {
            OwnerExpressionKind.Id => GetOwnerIdExpressionIdentifier,
            OwnerExpressionKind.Owner => GetOwnerExpressionIdentifier,
            _ => throw new ArgumentOutOfRangeException(nameof(expressionKind), expressionKind, null),
        };

    // I'm sure this one is never cached though.
    public static readonly MethodDeclarationSyntax CoerceMethod = (MethodDeclarationSyntax) ParseMemberDeclaration($$"""
         private static U Coerce<T, U>(T value)
         {
             if (value is not U u)
                 throw new global::{{typeof(WrongIdTypeException).FullName!}}(expected: typeof(U), actual: typeof(T));
             return u;
         }
    """)!;

    private static readonly string SupportsXOwnerFilter2Method = """
        public static bool Supports{X}OwnerFilter(Type entityType, Type idType)
        {
            var ownerType = Get{X}OwnerType(entityType);
            if (ownerType is null)
                return false;

            var ownerIdType = GetIdType(ownerType);
            return Supports{X}OwnerFilter(entityType) && ownerIdType == idType;
        }
    """;

    public static readonly MethodDeclarationSyntax SomeOwnerFilterTMethod = (MethodDeclarationSyntax) ParseMemberDeclaration($$"""
        public static IQueryable<TEntity> SomeOwnerFilterT<TEntity, TOwner, TOwnerId>(this IQueryable<TEntity> query, TOwnerId ownerId)
            where TEntity : class
        {
            var filter = GetSomeOwnerFilterT<TEntity, TOwner, TOwnerId>(ownerId);
            if (filter is null)
                throw new InvalidOperationException();
            return query.Where(filter);
        }
    """)!;

    // Replace X for Y
    private static MethodDeclarationSyntax SupportsXOwnerFilter2Syntax(string newX) => (MethodDeclarationSyntax)
        ParseMemberDeclaration(SupportsXOwnerFilter2Method.Replace("{X}", newX))!;

    public static readonly MethodDeclarationSyntax SupportsRootOwnerFilter2Method =
        SupportsXOwnerFilter2Syntax("Root");
    public static readonly MethodDeclarationSyntax SupportsDirectOwnerFilter2Method =
        SupportsXOwnerFilter2Syntax("Direct");


    public static readonly MethodDeclarationSyntax SupportsSomeOwnerFilterMethod = (MethodDeclarationSyntax) ParseMemberDeclaration("""
        public static bool SupportsSomeOwnerFilter(Type entityType, Type ownerType, Type idType)
        {
            var ownerIdType = GetIdType(ownerType);
            return SupportsSomeOwnerFilter(entityType, ownerType) && ownerIdType == idType;
        }
    """)!;

    private static readonly string XOwnerFilterClass = $$"""
    using System.Linq;

    public sealed class {X}OwnerFilter : global::{{typeof(IRootOwnerFilter).Namespace!}}.I{X}OwnerFilter
    {
        private {X}OwnerFilter() {}
        public static {X}OwnerFilter Instance { get; } = new();

        public bool CanFilter<TEntity, TOwnerId>()
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            var idType = typeof(TOwnerId);
            return {{HelperClassIdentifier.ToString()}}.Supports{X}OwnerFilter(entityType, idType);
        }

        public IQueryable<TEntity> Filter<TEntity, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
            where TEntity : class
        {
            return {{GenericMethodsClassIdentifier.ToString()}}.{X}OwnerFilterT<TEntity, TOwnerId>(query, ownerId);
        }
    {TrySetOwnerIdMethod}
    }
    """;

    private static readonly string XTrySetOwnerIdMethod = $$"""

        public bool TrySetOwnerId<TEntity, TOwner, TOwnerId>(TEntity entity, TOwnerId ownerId)
            where TEntity : class
        {
            return {{GenericMethodsClassIdentifier.ToString()}}.{{TrySetOwnerIdIdentifier.ToString()}}<TEntity, TOwner, TOwnerId>(entity, ownerId);
        }
    """;

    // Replace X for Y
    private static string XClassImplementation(string newX) => XOwnerFilterClass
        .Replace("{X}", newX)
        .Replace("{TrySetOwnerIdMethod}", newX == "Direct" ? XTrySetOwnerIdMethod : "");
    public static readonly string RootOwnerFilterClass =
        XClassImplementation("Root");
    public static readonly string DirectOwnerFilterClass =
        XClassImplementation("Direct");

    public static readonly string SomeOwnerFilterClass = $$"""
    using System.Linq;
    using System.Linq.Expressions;

    public sealed class SomeOwnerFilter : global::{{typeof(ISomeOwnerFilter).FullName!}}
    {
        private SomeOwnerFilter() {}
        public static SomeOwnerFilter Instance { get; } = new();

        public bool CanFilter<TEntity, TOwner, TOwnerId>()
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            var ownerType = typeof(TOwner);
            var idType = typeof(TOwnerId);
            return {{HelperClassIdentifier.ToString()}}.SupportsSomeOwnerFilter(entityType, ownerType, idType);
        }

        public IQueryable<TEntity> Filter<TEntity, TOwner, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
            where TEntity : class
        {
            return {{GenericMethodsClassIdentifier.ToString()}}.SomeOwnerFilterT<TEntity, TOwner, TOwnerId>(query, ownerId);
        }

        public Expression<System.Func<TEntity, bool>>? GetFilter<TEntity, TOwner, TOwnerId>(TOwnerId ownerId)
            where TEntity : class
        {
            return {{GenericMethodsClassIdentifier.ToString()}}.GetSomeOwnerFilterT<TEntity, TOwner, TOwnerId>(ownerId);
        }
    }
    """;
}
