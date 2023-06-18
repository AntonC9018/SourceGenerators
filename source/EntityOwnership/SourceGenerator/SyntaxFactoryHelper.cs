using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

public static class SyntaxFactoryHelper
{
    public static MemberAccessExpressionSyntax PropertyAccess(
        ExpressionSyntax expression,
        IPropertySymbol property)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            expression,
            IdentifierName(property.Name));
    }

    private static readonly SyntaxTokenList _PublicStaticPartial = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
        Token(SyntaxKind.PartialKeyword),
    });

    public static ClassDeclarationSyntax ContainerClass(SyntaxToken name, IEnumerable<MemberDeclarationSyntax> members)
    {
        return ClassDeclaration(name)
            .WithModifiers(TokenList(_PublicStaticPartial))
            .WithMembers(List(members));
    }

    public static MemberAccessExpressionSyntax MethodAccess(SyntaxToken parent, SyntaxToken methodName)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(parent),
            IdentifierName(methodName));
    }

    private static readonly SyntaxTokenList _PublicStatic = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
    });

    public static MethodDeclarationSyntax FluentExtensionMethod(
        SyntaxToken name, ParameterSyntax[] parameters)
    {
        var fluentBuilderType = parameters[0];
        var method = MethodDeclaration(
            returnType: fluentBuilderType.Type!,
            identifier: name);

        method = method.WithModifiers(_PublicStatic);

        parameters[0] = parameters[0].AddModifiers(Token(SyntaxKind.ThisKeyword));
        method = method.WithParameterList(ParameterList(SeparatedList(parameters)));

        return method;
    }

    public static SimpleLambdaExpressionSyntax EqualsCheckLambda(
        ParameterSyntax parameter,
        ExpressionSyntax lhs,
        ExpressionSyntax rhs)
    {
        return SimpleLambdaExpression(
            parameter, BinaryExpression(SyntaxKind.EqualsExpression, lhs, rhs));
    }

    public static InvocationExpressionSyntax WhereInvocation(
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
}
