using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Helpers;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SourceGeneration.Models;

/// <summary>
/// A model describing a type info in a type hierarchy.
/// </summary>
internal readonly record struct TypeInfo(string Name, TypeKind Kind, bool IsRecord)
{
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
        return Kind switch
        {
            TypeKind.Struct => StructDeclaration(Name),
            TypeKind.Interface => InterfaceDeclaration(Name),
            TypeKind.Class when IsRecord =>
                RecordDeclaration(Token(SyntaxKind.RecordKeyword), Name)
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken)),
            _ => ClassDeclaration(Name),
        };
    }

    public void WriteAsTypeDeclaration(
        IndentedTextWriter writer,
        bool allowDeclaringFields = false,
        Accessibility accessibility = Accessibility.NotApplicable)
    {
        if (allowDeclaringFields)
        {
            if (Kind == TypeKind.Struct)
            {
                // [StructLayout(LayoutKind.Auto)]
                // Otherwise, the fields can't be defined across multiple declarations.
                writer.WriteLine($"[global::{typeof(StructLayoutAttribute).Namespace}.StructLayout(global::{typeof(LayoutKind).Namespace}.{nameof(LayoutKind)}.{nameof(LayoutKind.Auto)})]");
            }
        }

        if (accessibility != Accessibility.NotApplicable)
        {
            writer.Write($"{SyntaxFacts.GetText(accessibility)} ");
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
}
