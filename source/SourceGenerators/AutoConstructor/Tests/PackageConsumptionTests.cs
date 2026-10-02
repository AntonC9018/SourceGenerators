using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace Tests;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task AutoConstructorPackage()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();

        var test = new PackageConsumptionTestBuilder(nameof(AutoConstructorPackage))
            .AddPackage(
                "source/SourceGenerators/AutoConstructor/SourceGenerator/AutoConstructor.SourceGenerator.csproj")
            .AddSingleFileConsumer(
                "source/SourceGenerators/AutoConstructor/Tests/Consumers/AutoConstructor.Basic.cs",
                consumer => consumer.ExpectRun())
            .Build();

        await tester.AssertAsync(test);
    }
}
