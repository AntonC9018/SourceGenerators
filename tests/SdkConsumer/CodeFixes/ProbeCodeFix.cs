using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ProbeCodeFix))]
[Shared]
public sealed class ProbeCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ProbeAnalyzer.Rule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var declaration = root?
            .FindNode(context.Diagnostics[0].Location.SourceSpan)
            .FirstAncestorOrSelf<ClassDeclarationSyntax>();

        if (declaration is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Rename to GoodName",
                cancellationToken => RenameAsync(context.Document, declaration, cancellationToken),
                nameof(ProbeCodeFix)),
            context.Diagnostics[0]);
    }

    private static async Task<Document> RenameAsync(
        Document document,
        ClassDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root is null)
        {
            return document;
        }

        var renamed = declaration.WithIdentifier(
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Identifier(ProbeNames.GoodName));
        return document.WithSyntaxRoot(root.ReplaceNode(declaration, renamed));
    }
}
