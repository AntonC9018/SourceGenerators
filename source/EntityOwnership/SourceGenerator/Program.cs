using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static EntityOwnership.SourceGenerator.SyntaxFactoryHelper;

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
                transform: (c, _) => c.GetEntityTypeInfo())
            .Where(r => r is not null);

        var source = context.CompilationProvider.Combine(entities.Collect());

        context.RegisterSourceOutput(source, static (context, source) =>
        {
            var (compilation, entities) = source;

#pragma warning disable CS8620 // Wrong nullability. Compiler can't figure out that entities won't have nulls.
            var graph = Graph.Create(compilation, entities);
#pragma warning restore CS8620

            var compilationRoot = OwnershipSyntaxHelper.GenerateExtensionMethodClasses(graph);

            context.AddSource(
                "EntityOwnershipExtensions.cs",
                compilationRoot.GetText(Encoding.UTF8));
        });
    }




}
