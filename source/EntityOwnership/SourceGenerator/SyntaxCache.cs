using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

internal record NodeSyntaxCache(
    TypeSyntax EntityType,
    ParameterSyntax IdParameter,
    ParameterSyntax QueryParameter)
{
    public MethodDeclarationSyntax? DirectOwnerMethod { get; set; }
    public MethodDeclarationSyntax? RootOwnerMethod { get; set; }

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

        return new NodeSyntaxCache(entityTypeSyntax, idParameter, queryParameter);
    }
}

internal class SyntaxGenerationCache
{
    public readonly StatementSyntax[] Statements = new StatementSyntax[3];
    public readonly ArgumentSyntax[] Arguments = new ArgumentSyntax[2];
    public readonly ParameterSyntax[] Parameters = new ParameterSyntax[2];
    public readonly TypeParameterSyntax[] TypeParameters = new TypeParameterSyntax[2];
    public readonly TypeSyntax[] TypeArguments = new TypeSyntax[2];
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
}
