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

        await tester.AssertAsync(new PackageConsumptionTest(
            nameof(AutoImplementedPropertiesPackage),
            new[]
            {
                new PackageProject(
                    "source/SourceGenerators/AutoImplementedProperties/SourceGenerator/AutoImplementedProperties.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/SourceGenerators/AutoImplementedProperties/Tests/Consumers/AutoImplementedProperties.Basic.cs",
                    new RunOptions()),
                ConsumerFixture.ProjectDirectory(
                    "source/SourceGenerators/AutoImplementedProperties/Tests/Consumers/AutoImplementedProperties.ProjectFlow",
                    mainProjectPath: "App/App.csproj",
                    run: new RunOptions()),
            }));
    }
}
