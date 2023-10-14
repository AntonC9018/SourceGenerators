using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyTests;
using VerifyXunit;
using Basic.Reference.Assemblies;

namespace AutoImplementedProperties.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init() => VerifySourceGenerators.Initialize();
}

public sealed class TestHelper<TSourceGenerator>
    where TSourceGenerator : IIncrementalGenerator, new()
{
    private readonly IEnumerable<MetadataReference> _assemblyReferences;
    private readonly string _sourceFilePath;

    public TestHelper(
        IEnumerable<MetadataReference> assemblyReferences,
        [CallerFilePath] string sourceFilePath = "")
    {
        _assemblyReferences = assemblyReferences;
        _sourceFilePath = sourceFilePath;
    }

    public Task Verify(string source)
    {
        return _Verify(source, _assemblyReferences, _sourceFilePath);
    }

    private static async Task _Verify(
        string source,
        IEnumerable<MetadataReference> assemblyReferences,
        [CallerFilePath] string sourceFilePath = "")
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source,
            new(
                // preprocessorSymbols: new[]
                // {
                //     Constants.ConditionString
                // }
            ));
        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: new[] { syntaxTree },
            references: assemblyReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

        var generator = new TSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var compilation1,
            out var diagnostics);

        async Task WriteAllGeneratedFiles()
        {
            var result = driver.GetRunResult();
            foreach (var generatorResult in result.Results)
            {
                foreach (var generatedSource in generatorResult.GeneratedSources)
                {
                    await File.WriteAllTextAsync(
                        Path.Combine("GeneratedFiles", generatedSource.HintName),
                        generatedSource.SourceText.ToString());
                }
            }
        }

        if (diagnostics.Any())
        {
            await WriteAllGeneratedFiles();
            throw new Exception(string.Join(Environment.NewLine, diagnostics));
        }

        {
            var compilationDiagnostics = compilation1.GetDiagnostics();
            if (compilationDiagnostics.Any())
            {
                await WriteAllGeneratedFiles();
                throw new Exception(string.Join(Environment.NewLine, compilationDiagnostics));
            }
        }

        await Verifier
            // ReSharper disable once ExplicitCallerInfoArgument
            .Verify(driver, sourceFile: sourceFilePath)
            .UseDirectory("Snapshots");
    }

}

public static class TestHelper
{
    public static IEnumerable<MetadataReference> GetAllMetadataReferences(params Type[] requiredTypes)
    {
        var defaultMetadataReferences = ReferenceAssemblies.NetStandard20;
        var additionalTypes = requiredTypes;
        var assemblyPaths = additionalTypes.Select(t => t.Assembly.Location).Distinct();
        var additionalMetadataReferences = assemblyPaths.Select(a => MetadataReference.CreateFromFile(a));
        var allMetadataReferences = defaultMetadataReferences.Concat(additionalMetadataReferences);
        return allMetadataReferences;
    }
}
