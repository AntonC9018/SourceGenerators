using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoImplementedProperties.Attributes;
using AutoImplementedProperties.SourceGenerator;
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

public static class TestHelper
{
    public static Task Verify(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source,
            new(
                // preprocessorSymbols: new[]
                // {
                //     Constants.ConditionString
                // }
            ));
        var references = GetAllMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

        var generator = new AutoImplementedPropertyGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var compilation1,
            out var diagnostics);

        if (diagnostics.Any())
            throw new Exception(string.Join(Environment.NewLine, diagnostics));

        {
            var compilationDiagnostics = compilation1.GetDiagnostics();
            if (compilationDiagnostics.Any())
                throw new Exception(string.Join(Environment.NewLine, compilationDiagnostics));
        }

        return Verifier
            .Verify(driver)
            .UseDirectory("Snapshots");
    }

    private static IEnumerable<MetadataReference> GetReferencesOfType(Type[] types)
    {
        var assemblyPaths = types.Select(t => t.Assembly.Location).Distinct();
        var refs = assemblyPaths.Select(a => MetadataReference.CreateFromFile(a));
        return refs;
    }

    public static IEnumerable<MetadataReference> GetAllMetadataReferences()
    {
        var defaultMetadataReferences = ReferenceAssemblies.NetStandard20;
        var additionalTypes = new[]
        {
            typeof(AutoImplementPropertiesAttribute),
        };
        var additionalMetadataReferences = GetReferencesOfType(additionalTypes);
        var currentAssembly = Assembly.GetExecutingAssembly()!;
        var referencedAssemblies = currentAssembly.GetReferencedAssemblies();
        var allMetadataReferences = defaultMetadataReferences.Concat(additionalMetadataReferences);
        foreach (var reference in allMetadataReferences)
            yield return reference;
        foreach (var loadedAssembly in referencedAssemblies)
            yield return MetadataReference.CreateFromFile(Assembly.Load(loadedAssembly).Location);
    }
}
