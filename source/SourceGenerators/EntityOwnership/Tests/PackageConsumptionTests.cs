using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace Tests;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task EntityOwnershipPackage()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();

        await tester.AssertAsync(new PackageConsumptionTest(
            nameof(EntityOwnershipPackage),
            new[]
            {
                new PackageProject(
                    "source/SourceGenerators/EntityOwnership/SourceGenerator/EntityOwnership.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/SourceGenerators/EntityOwnership/Tests/Consumers/EntityOwnership.Basic.cs",
                    new RunOptions()),
            }));
    }
}
