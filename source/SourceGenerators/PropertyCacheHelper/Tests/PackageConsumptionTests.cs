using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace Tests;

public sealed class PackageConsumptionTests
{
    [Fact]
    public async Task PropertyCacheHelperPackage()
    {
        var tester = await LocalNuGetPackageTester.CreateAsync();

        await tester.AssertAsync(new PackageConsumptionTest(
            nameof(PropertyCacheHelperPackage),
            new[]
            {
                new PackageProject(
                    "source/SourceGenerators/PropertyCacheHelper/SourceGenerator/PropertyCacheHelper.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/SourceGenerators/PropertyCacheHelper/Tests/Consumers/PropertyCacheHelper.Basic.cs",
                    new RunOptions()),
            }));
    }
}
