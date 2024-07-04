using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SourceGeneration.Extensions;

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

    public static readonly SyntaxTokenList PublicStaticPartial = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
        Token(SyntaxKind.PartialKeyword),
    });

    public static ClassDeclarationSyntax ContainerClass(SyntaxToken name, IEnumerable<MemberDeclarationSyntax> members)
    {
        return ClassDeclaration(name)
            .WithModifiers(TokenList(PublicStaticPartial))
            .WithMembers(List(members));
    }

    public static MemberAccessExpressionSyntax MethodAccess(SyntaxToken parent, SyntaxToken methodName)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(parent),
            IdentifierName(methodName));
    }

    public static readonly SyntaxTokenList PublicStatic = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
    });

    public static readonly SyntaxTokenList PublicStaticReadonly = TokenList(new[]
    {
        Token(SyntaxKind.PublicKeyword),
        Token(SyntaxKind.StaticKeyword),
        Token(SyntaxKind.ReadOnlyKeyword),
    });

    private static readonly SyntaxToken[] _ThisKeyword = { Token(SyntaxKind.ThisKeyword) };

    public static MethodDeclarationSyntax FluentExtensionMethod(
        SyntaxToken name, List<ParameterSyntax> parameters)
    {
        var fluentBuilderType = parameters[0];
        var method = MethodDeclaration(
            returnType: fluentBuilderType.Type!,
            identifier: name);

        method = method.WithModifiers(PublicStatic);

        parameters[0] = parameters[0].AddModifiers(_ThisKeyword);
        method = method.WithParameterList(ParameterList(SeparatedList(parameters)));

        return method;
    }

    public static ParenthesizedLambdaExpressionSyntax EqualsCheckLambda(
        ParameterSyntax parameter,
        ExpressionSyntax lhs,
        ExpressionSyntax rhs)
    {
        return ParenthesizedLambdaExpression(BinaryExpression(SyntaxKind.EqualsExpression, lhs, rhs))
            .WithParameterList(ParameterList(SingletonSeparatedList(parameter)));
    }

    public static InvocationExpressionSyntax WhereInvocation(
        ExpressionSyntax parent,
        LambdaExpressionSyntax predicate)
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

    public static readonly StatementSyntax ReturnTrue = ReturnStatement(LiteralExpression(SyntaxKind.TrueLiteralExpression));
    public static readonly StatementSyntax ReturnFalse = ReturnStatement(LiteralExpression(SyntaxKind.FalseLiteralExpression));
    public static readonly StatementSyntax ReturnNull = ReturnStatement(LiteralExpression(SyntaxKind.NullLiteralExpression));

    public static readonly StatementSyntax ThrowInvalidOperationStatement = ThrowNewExceptionStatement(
            ParseTypeName(typeof(InvalidOperationException).FullName!));

    public static StatementSyntax ThrowNewExceptionStatement(TypeSyntax exception) =>
        ThrowStatement(
            ObjectCreationExpression(exception)
                .WithArgumentList(ArgumentList()));

    public static NameSyntax ToNameSyntax(this INamespaceSymbol namespaceSymbol)
    {
        var ownNameSyntax = IdentifierName(namespaceSymbol.Name);
        if (namespaceSymbol.ContainingNamespace is { } ns)
        {
            var parentNamespace = ToNameSyntax(ns);
            return QualifiedName(parentNamespace, ownNameSyntax);
        }
        return ownNameSyntax;
    }

    public static readonly SyntaxTokenList PrivateStaticReadonly = TokenList(new[]
    {
        Token(SyntaxKind.PrivateKeyword),
        Token(SyntaxKind.StaticKeyword),
        Token(SyntaxKind.ReadOnlyKeyword),
    });

    public static VariableDeclarationSyntax SingleVariableDeclaration(
        TypeSyntax type,
        VariableDeclaratorSyntax declaration)
    {
        return VariableDeclaration(type, SingletonSeparatedList(declaration));
    }
}

public record struct SingleVariableDeclarationInfo(
    SyntaxToken Identifier, VariableDeclarationSyntax Declaration);
