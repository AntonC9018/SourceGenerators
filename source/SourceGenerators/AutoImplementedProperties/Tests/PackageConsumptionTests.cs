using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace Tests;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task AutoImplementedPropertiesPackage()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();

        var test = new PackageConsumptionTestBuilder(nameof(AutoImplementedPropertiesPackage))
            .AddPackage(
                "source/SourceGenerators/AutoImplementedProperties/SourceGenerator/AutoImplementedProperties.SourceGenerator.csproj")
            .AddSingleFileConsumer(
                "source/SourceGenerators/AutoImplementedProperties/Tests/Consumers/AutoImplementedProperties.Basic.cs",
                consumer => consumer.ExpectRun())
            .AddProjectConsumer(
                "source/SourceGenerators/AutoImplementedProperties/Tests/Consumers/AutoImplementedProperties.ProjectFlow",
                consumer => consumer
                    .WithMainProject("App/App.csproj")
                    .ExpectRun())
            .Build();

        await tester.AssertAsync(test);
    }
}
