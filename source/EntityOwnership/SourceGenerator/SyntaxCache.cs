using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var entityTypeSyntax = SyntaxFactory.ParseTypeName(fullyQualifiedTypeName);
        var idTypeSyntax = SyntaxFactory.ParseTypeName(fullyQualifiedIdPropertyName);
        var queryTypeName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("IQueryable"),
            SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(entityTypeSyntax)));

        var queryParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("query"))
            .WithType(queryTypeName);
        var idParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("ownerId"))
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
