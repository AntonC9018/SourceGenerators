using System;
using SourceGeneration.Extensions;
using SourceGeneration.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle;

namespace SourceGeneration.Models;

/// <summary>
/// A model describing where a generated partial type declaration is emitted.
/// </summary>
internal sealed partial record TypeDeclarationPath(
    string HintName,
    string Namespace,
    EquatableArray<TypeDeclarationInfo> ContainingTypes,
    TypeDeclarationInfo TargetType)
{
    /// <summary>
    /// Creates a new <see cref="TypeDeclarationPath"/> instance from a type that already exists in source.
    /// </summary>
    public static TypeDeclarationPath FromExisting(INamedTypeSymbol targetType)
    {
        using var containingTypes = ImmutableArrayBuilder<TypeDeclarationInfo>.Rent();

        for (INamedTypeSymbol? parent = targetType.ContainingType;
             parent is not null;
             parent = parent.ContainingType)
        {
            containingTypes.Insert(0, TypeDeclarationInfo.From(parent));
        }

        return new(
            targetType.GetFullyQualifiedMetadataName(),
            targetType.ContainingNamespace.ToDisplayString(new(typeQualificationStyle: NameAndContainingTypesAndNamespaces)),
            containingTypes.ToImmutable(),
            TypeDeclarationInfo.From(targetType));
    }

    /// <summary>
    /// Creates a declaration path for a generated type that does not already exist in source.
    /// </summary>
    public static TypeDeclarationPath FromSynthetic(
        INamespaceOrTypeSymbol container,
        TypeDeclarationInfo targetType,
        string hintName)
    {
        using var containingTypes = ImmutableArrayBuilder<TypeDeclarationInfo>.Rent();

        if (container is INamedTypeSymbol containerType)
        {
            for (INamedTypeSymbol? parent = containerType;
                 parent is not null;
                 parent = parent.ContainingType)
            {
                containingTypes.Insert(0, TypeDeclarationInfo.From(parent));
            }
        }

        INamespaceSymbol? containingNamespace = container switch
        {
            INamespaceSymbol namespaceSymbol => namespaceSymbol,
            INamedTypeSymbol typeSymbol => typeSymbol.ContainingNamespace,
            _ => null,
        };

        return new(
            hintName,
            containingNamespace?.ToDisplayString(new(typeQualificationStyle: NameAndContainingTypesAndNamespaces)) ?? "",
            containingTypes.ToImmutable(),
            targetType);
    }

    /// <summary>
    /// Creates a <see cref="CompilationUnitSyntax"/> instance for the current declaration path.
    /// </summary>
    /// <param name="memberDeclarations">The member declarations to add to the generated type.</param>
    /// <param name="nullableEnable"></param>
    /// <returns>A <see cref="CompilationUnitSyntax"/> instance for the current declaration path.</returns>
    public CompilationUnitSyntax ToCompilationUnit(
        MemberDeclarationSyntax[] memberDeclarations,
        bool nullableEnable = true)
    {
        return ToCompilationUnit(memberDeclarations, new GeneratedFileOptions(nullableEnable));
    }

    public CompilationUnitSyntax ToCompilationUnit(
        MemberDeclarationSyntax[] memberDeclarations,
        GeneratedFileOptions options)
    {
        TypeDeclarationSyntax typeDeclarationSyntax =
            TargetType.GetSyntax()
            .AddModifiers(Token(SyntaxKind.PartialKeyword))
            .AddMembers(memberDeclarations);

        for (int i = ContainingTypes.Length - 1; i >= 0; i--)
        {
            typeDeclarationSyntax =
                ContainingTypes[i].GetSyntax()
                .AddModifiers(Token(SyntaxKind.PartialKeyword))
                .AddMembers(typeDeclarationSyntax);
        }

        SyntaxTriviaList syntaxTriviaList = GeneratedFileHelper.GetTriviaList(options);

        if (Namespace is "")
        {
            // If there is no namespace, attach the pragma directly to the declared type,
            // and skip the namespace declaration. This will produce code as follows:
            //
            // <SYNTAX_TRIVIA>
            // <TYPE_DECLARATION_PATH>
            return
                CompilationUnit()
                .AddMembers(typeDeclarationSyntax.WithLeadingTrivia(syntaxTriviaList))
                .NormalizeWhitespace(eol: "\n");
        }

        // Create the compilation unit with disabled warnings, target namespace and generated type.
        // This will produce code as follows:
        //
        // <SYNTAX_TRIVIA>
        // namespace <NAMESPACE>;
        //
        // <TYPE_DECLARATION_PATH>
        return
            CompilationUnit().AddMembers(
            FileScopedNamespaceDeclaration(IdentifierName(Namespace))
            .WithLeadingTrivia(syntaxTriviaList)
            .AddMembers(typeDeclarationSyntax))
            .NormalizeWhitespace(eol: "\n");
    }

    public DeclarationPathScope WriteTo(IndentedTextWriter writer)
    {
        int blockCount = 0;

        if (Namespace.Length > 0)
        {
            writer.WriteLine($"namespace {Namespace}");
            writer.WriteBlock();
            blockCount++;
        }

        foreach (TypeDeclarationInfo containingType in ContainingTypes)
        {
            containingType.WriteAsPartialDeclaration(writer);
            writer.WriteLine();
            writer.WriteBlock();
            blockCount++;
        }

        TargetType.WriteAsPartialDeclaration(writer);
        writer.WriteLine();
        writer.WriteBlock();
        blockCount++;

        return new(writer, blockCount);
    }

    public TypeDeclarationPath WithTargetType(
        TypeDeclarationInfo targetType,
        string? hintName = null)
    {
        return this with
        {
            HintName = hintName ?? HintName,
            TargetType = targetType,
        };
    }
}

internal readonly record struct GeneratedFileOptions(bool NullableEnable)
{
    public static GeneratedFileOptions NullableEnabled { get; } = new(true);

    public static GeneratedFileOptions NullableDisabled { get; } = new(false);
}

internal static class GeneratedFileHelper
{
    // Prepare the leading trivia for the generated compilation unit.
    // This will produce code as follows:
    /*
        // <auto-generated/>
        #pragma warning disable
        #nullable enable
    */
    public static SyntaxTriviaList GetTriviaList(bool nullableEnable)
    {
        return GetTriviaList(new GeneratedFileOptions(nullableEnable));
    }

    public static SyntaxTriviaList GetTriviaList(GeneratedFileOptions options)
    {
        SyntaxTriviaList syntaxTriviaList;
        var autogenComment = Comment("// <auto-generated/>");
        var warningDisable = PragmaWarningDirectiveTrivia(
            Token(SyntaxKind.DisableKeyword), !options.NullableEnable);
        var nullableEnableDirective = NullableDirectiveTrivia(
            Token(SyntaxKind.EnableKeyword), options.NullableEnable);
        syntaxTriviaList = TriviaList(
            autogenComment,
            Trivia(warningDisable),
            Trivia(nullableEnableDirective));
        return syntaxTriviaList;
    }

    public static void WriteFileStart(this IndentedTextWriter w, bool nullableEnable)
    {
        w.WriteFileStart(new GeneratedFileOptions(nullableEnable));
    }

    public static void WriteFileStart(this IndentedTextWriter w, GeneratedFileOptions options)
    {
        w.WriteLine("// <auto-generated/>");
        w.WriteLine("#pragma warning disable");
        if (options.NullableEnable)
        {
            w.WriteLine("#nullable enable");
        }
    }

    public static DeclarationPathScope WriteDeclarationPath(
        this IndentedTextWriter writer,
        TypeDeclarationPath path)
    {
        return path.WriteTo(writer);
    }
}

internal readonly struct DeclarationPathScope(
    IndentedTextWriter writer,
    int blockCount) : IDisposable
{
    public void Dispose()
    {
        for (int i = 0; i < blockCount; i++)
        {
            var block = new IndentedTextWriter.Block(writer);
            block.Dispose();
        }
    }
}
