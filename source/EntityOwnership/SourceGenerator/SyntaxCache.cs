using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

[DebuggerDisplay("{EntityType}; {IdType}")]
internal record NodeSyntaxCache(
    TypeSyntax EntityType,
    ParameterSyntax IdParameter,
    ParameterSyntax QueryParameter,
    ParameterSyntax LambdaParameter,
    string EscapedEntityTypeName)
{
    public MethodDeclarationSyntax? DirectOwnerMethod { get; set; }
    public MethodDeclarationSyntax? RootOwnerMethod { get; set; }

    // entityType == typeof(Entity)
    public BinaryExpressionSyntax? EntityTypeCheck { get; set; }

    public TypeSyntax QueryType => QueryParameter.Type!;
    public TypeSyntax IdType => IdParameter.Type!;

    public List<(GraphNode OwnerNode, string Name)> OwnerIdAccesses { get; } = new();

    public static NodeSyntaxCache? Create(GraphNode node)
    {
        string fullyQualifiedIdTypeName;
        {
            if (node.IdProperty is { } idProperty)
                fullyQualifiedIdTypeName = idProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            else if (node.Source.Type.Id is { } id)
                fullyQualifiedIdTypeName = id.FullyQualifiedTypeName;
            else
                return null;
        }
        string fullyQualifiedTypeName = node.Source.Type.FullyQualifiedTypeName;
        var entityTypeSyntax = ParseTypeName(fullyQualifiedTypeName);
        var idTypeSyntax = ParseTypeName(fullyQualifiedIdTypeName);
        var queryTypeName = GenericName(
            Identifier("IQueryable"),
            TypeArgumentList(SingletonSeparatedList(entityTypeSyntax)));

        var queryParameter = Parameter(Identifier("query"))
            .WithType(queryTypeName);
        var idParameter = Parameter(Identifier("ownerId"))
            .WithType(idTypeSyntax);

        var firstLetter = node.Type.Name[..1].ToLowerInvariant();
        var lambdaParameter = Parameter(Identifier(firstLetter));

        var escapedEntityTypeName = fullyQualifiedTypeName.Replace(".", "_");

        return new NodeSyntaxCache(
            entityTypeSyntax,
            idParameter,
            queryParameter,
            lambdaParameter,
            escapedEntityTypeName);
    }

    public string GetOwnerIdAccessName(NodeSyntaxCache ownerSyntaxCache)
    {
        return $"Id__{EscapedEntityTypeName}__{ownerSyntaxCache.EscapedEntityTypeName}";
    }
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
    public static readonly SyntaxToken TrySetOwnerIdIdentifier = Identifier("TrySetOwnerId");

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

}
