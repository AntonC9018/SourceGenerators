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

        await tester.AssertAsync(new PackageConsumptionTest(
            nameof(AutoConstructorPackage),
            new[]
            {
                new PackageProject(
                    "source/SourceGenerators/AutoConstructor/SourceGenerator/AutoConstructor.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/SourceGenerators/AutoConstructor/Tests/Consumers/AutoConstructor.Basic.cs",
                    new RunOptions()),
            }));
    }
}
