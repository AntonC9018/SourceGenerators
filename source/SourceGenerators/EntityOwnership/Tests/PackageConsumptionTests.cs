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

        var test = new PackageConsumptionTestBuilder(nameof(EntityOwnershipPackage))
            .AddPackage(
                "source/SourceGenerators/EntityOwnership/SourceGenerator/EntityOwnership.SourceGenerator.csproj")
            .AddSingleFileConsumer(
                "source/SourceGenerators/EntityOwnership/Tests/Consumers/EntityOwnership.Basic.cs",
                consumer => consumer.ExpectRun())
            .Build();

        await tester.AssertAsync(test);
    }
}
