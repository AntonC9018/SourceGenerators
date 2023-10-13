using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoImplementedProperties.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyTests;
using VerifyXunit;

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
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoImplementedPropertyGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        if (result.Diagnostics.Length > 0)
            throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));

        return Verifier
            .Verify(driver)
            .UseDirectory("Snapshots");
    }
}
