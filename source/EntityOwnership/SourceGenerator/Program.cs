using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneration.Extensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityOwnership.SourceGenerator;

/// <summary>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OwnershipGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (c, _) => c.GetEntityTypeInfo())
            .Where(r => r is not null);

        var source1 = context.CompilationProvider.Combine(entities.Collect());
        var source2 = context.AnalyzerConfigOptionsProvider.Combine(source1);

        context.RegisterSourceOutput(source2, static (context, source) =>
        {
            var (analyzerOptions, (compilation, entities)) = source;

            if (entities.Length == 0)
                return;

#pragma warning disable CS8620 // Wrong nullability. Compiler can't figure out that entities won't have nulls.
            var graph = Graph.Create(compilation, entities);
#pragma warning restore CS8620

            var entityOwnership = IdentifierName("EntityOwnership");
            NameSyntax generatedNamespace;
            if (analyzerOptions.GlobalOptions.GetRootNamespace() is { } rootNamespaceProp
                && ParseName(rootNamespaceProp) is { ContainsDiagnostics: false } rootNamespace)
            {
                generatedNamespace = QualifiedName(rootNamespace, entityOwnership);
            }
            else
            {
                generatedNamespace = entityOwnership;
            }

            var compilationRoot = OwnershipSyntaxHelper.GenerateExtensionMethodClasses(graph, generatedNamespace);

            context.AddSource(
                "EntityOwnershipExtensions.cs",
                compilationRoot.GetText(Encoding.UTF8));

            context.AddSource(
                "SomeOwnerFilter.cs",
                $"namespace {generatedNamespace};\r\n\r\n" +
                SourceText.From(StaticSyntaxCache.SomeOwnerFilterClass, Encoding.UTF8));
            context.AddSource(
                "RootOwnerFilter.cs",
                $"namespace {generatedNamespace};\r\n\r\n" +
                SourceText.From(StaticSyntaxCache.RootOwnerFilterClass, Encoding.UTF8));
            context.AddSource(
                "DirectOwnerFilter.cs",
                $"namespace {generatedNamespace};\r\n\r\n" +
                SourceText.From(StaticSyntaxCache.DirectOwnerFilterClass, Encoding.UTF8));
        });
    }
}
