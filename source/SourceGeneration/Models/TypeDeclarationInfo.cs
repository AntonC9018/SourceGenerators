using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Helpers;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SourceGeneration.Models;

/// <summary>
/// A model describing one type declaration in a generated partial declaration path.
/// </summary>
internal readonly record struct TypeDeclarationInfo(
    string Name,
    TypeKind Kind,
    bool IsRecord,
    Accessibility Accessibility = Accessibility.NotApplicable,
    bool AllowsInstanceFieldsAcrossPartials = false)
{
    public static TypeDeclarationInfo From(INamedTypeSymbol typeSymbol)
    {
        return new(
            typeSymbol.Name,
            typeSymbol.TypeKind,
            typeSymbol.IsRecord);
    }

    /// <summary>
    /// Creates a <see cref="TypeDeclarationSyntax"/> instance for the current info.
    /// </summary>
    /// <returns>A <see cref="TypeDeclarationSyntax"/> instance for the current info.</returns>
    public TypeDeclarationSyntax GetSyntax()
    {
        // Create the partial type declaration with the kind.
        // This code produces a class declaration as follows:
        //
        // <TYPE_KIND> <TYPE_NAME>
        // {
        // }
        //
        // Note that specifically for record declarations, we also need to explicitly add the open
        // and close brace tokens, otherwise member declarations will not be formatted correctly.
        TypeDeclarationSyntax syntax = (Kind, IsRecord) switch
        {
            (TypeKind.Struct, true) =>
                RecordDeclaration(SyntaxKind.RecordStructDeclaration, Token(SyntaxKind.RecordKeyword), Name)
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken)),
            (TypeKind.Struct, false) => StructDeclaration(Name),
            (TypeKind.Interface, _) => InterfaceDeclaration(Name),
            (TypeKind.Class, true) =>
                RecordDeclaration(SyntaxKind.RecordDeclaration, Token(SyntaxKind.RecordKeyword), Name)
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken)),
            _ => ClassDeclaration(Name),
        };

        if (AllowsInstanceFieldsAcrossPartials && Kind == TypeKind.Struct)
        {
            syntax = syntax.AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(ParseName($"global::{typeof(StructLayoutAttribute).FullName}"))
                    .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(
                        AttributeArgument(ParseExpression($"global::{typeof(LayoutKind).FullName}.{nameof(LayoutKind.Auto)}"))))))));
        }

        if (Accessibility != Accessibility.NotApplicable)
        {
            syntax = syntax.WithModifiers(GetAccessibilityModifiers(Accessibility));
        }

        return syntax;
    }

    public void WriteAsPartialDeclaration(IndentedTextWriter writer)
    {
        if (AllowsInstanceFieldsAcrossPartials && Kind == TypeKind.Struct)
        {
            writer.WriteLine($"[global::{typeof(StructLayoutAttribute).FullName}(global::{typeof(LayoutKind).FullName}.{nameof(LayoutKind.Auto)})]");
        }

        if (Accessibility != Accessibility.NotApplicable)
        {
            writer.Write($"{SyntaxFacts.GetText(Accessibility)} ");
        }

        writer.Write("partial ");

        if (IsRecord)
        {
            writer.Write("record ");
        }

        writer.Write(Kind switch
        {
            TypeKind.Struct => "struct ",
            TypeKind.Interface => "interface ",
            _ => "class ",
        });

        writer.Write(Name);
    }

    private static SyntaxTokenList GetAccessibilityModifiers(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => TokenList(Token(SyntaxKind.PublicKeyword)),
            Accessibility.Internal => TokenList(Token(SyntaxKind.InternalKeyword)),
            Accessibility.Private => TokenList(Token(SyntaxKind.PrivateKeyword)),
            Accessibility.Protected => TokenList(Token(SyntaxKind.ProtectedKeyword)),
            Accessibility.ProtectedOrInternal => TokenList(
                Token(SyntaxKind.ProtectedKeyword),
                Token(SyntaxKind.InternalKeyword)),
            Accessibility.ProtectedAndInternal => TokenList(
                Token(SyntaxKind.PrivateKeyword),
                Token(SyntaxKind.ProtectedKeyword)),
            _ => default,
        };
    }
}
