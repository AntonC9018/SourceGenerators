using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

[DebuggerDisplay("{EntityType}; {IdType}")]
internal record NodeSyntaxCache(
    TypeSyntax EntityType,
    ParameterSyntax IdParameter,
    ParameterSyntax QueryParameter,
    ParameterSyntax LambdaParameter)
{
    public MethodDeclarationSyntax? DirectOwnerMethod { get; set; }
    public MethodDeclarationSyntax? RootOwnerMethod { get; set; }

    // entityType == typeof(Entity)
    public BinaryExpressionSyntax? EntityTypeCheck { get; set; }

    public TypeSyntax QueryType => QueryParameter.Type!;
    public TypeSyntax IdType => IdParameter.Type!;

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

        // TODO: Maybe name it after the type, or at least the first letter of the type?
        var lambdaParameter = Parameter(Identifier("e"));

        return new NodeSyntaxCache(
            entityTypeSyntax,
            idParameter,
            queryParameter,
            lambdaParameter);
    }
}

internal class SyntaxGenerationCache
{
    public readonly ArgumentSyntax[] Arguments = new ArgumentSyntax[2];
    public readonly ParameterSyntax[] Parameters = new ParameterSyntax[2];
    public readonly List<TypeParameterSyntax> TypeParameters = new();
    public readonly TypeSyntax[] TypeArguments = new TypeSyntax[2];
    public readonly List<StatementSyntax> Statements = new();
    public readonly List<StatementSyntax> Statements2 = new();
    public readonly List<StatementSyntax> Statements3 = new();
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

    // I'm sure this one is never cached though.
    public static readonly MethodDeclarationSyntax CoerceMethod = (MethodDeclarationSyntax) ParseMemberDeclaration("""
        private static U Coerce<T, U>(T value)
        {
            if (value is not U u)
                // TODO: throw new WrongIdTypeException(expected: typeof(U), actual: typeof(T));
                throw new InvalidOperationException();

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

}
