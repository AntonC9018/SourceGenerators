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

        var test = new PackageConsumptionTestBuilder(nameof(PropertyCacheHelperPackage))
            .AddPackage(
                "source/SourceGenerators/PropertyCacheHelper/SourceGenerator/PropertyCacheHelper.SourceGenerator.csproj")
            .AddSingleFileConsumer(
                "source/SourceGenerators/PropertyCacheHelper/Tests/Consumers/PropertyCacheHelper.Basic.cs",
                consumer => consumer.ExpectRun())
            .Build();

        await tester.AssertAsync(test);
    }
}
