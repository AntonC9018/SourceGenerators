using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace PackageIntegration.Tests;

public sealed class PackageConsumptionTests
{
    private readonly Task<LocalNuGetPackageTester> _testerTask =
        LocalNuGetPackageTester.CreateAsync().AsTask();

    [Fact]
    public Task AutoImplementedPropertiesPackage()
    {
        return AssertAsync(new PackageConsumptionTest(
            nameof(AutoImplementedPropertiesPackage),
            new[]
            {
                new PackageProject("source/AutoImplementedProperties/SourceGenerator/AutoImplementedProperties.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/PackageIntegration.Tests/Consumers/AutoImplementedProperties.Basic.cs",
                    new RunOptions()),
                ConsumerFixture.ProjectDirectory(
                    "source/PackageIntegration.Tests/Consumers/AutoImplementedProperties.ProjectFlow",
                    mainProjectPath: "App/App.csproj",
                    run: new RunOptions()),
            }));
    }

    [Fact]
    public Task AutoConstructorPackage()
    {
        return AssertAsync(new PackageConsumptionTest(
            nameof(AutoConstructorPackage),
            new[]
            {
                new PackageProject("source/AutoConstructor/SourceGenerator/AutoConstructor.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/PackageIntegration.Tests/Consumers/AutoConstructor.Basic.cs",
                    new RunOptions()),
            }));
    }

    [Fact]
    public Task PropertyCacheHelperPackage()
    {
        return AssertAsync(new PackageConsumptionTest(
            nameof(PropertyCacheHelperPackage),
            new[]
            {
                new PackageProject("source/PropertyCacheHelper/SourceGenerator/PropertyCacheHelper.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/PackageIntegration.Tests/Consumers/PropertyCacheHelper.Basic.cs",
                    new RunOptions()),
            }));
    }

    [Fact]
    public Task EntityOwnershipPackage()
    {
        return AssertAsync(new PackageConsumptionTest(
            nameof(EntityOwnershipPackage),
            new[]
            {
                new PackageProject("source/EntityOwnership/SourceGenerator/EntityOwnership.SourceGenerator.csproj"),
            },
            new[]
            {
                ConsumerFixture.SingleFile(
                    "source/PackageIntegration.Tests/Consumers/EntityOwnership.Basic.cs",
                    new RunOptions()),
            }));
    }

    private async Task AssertAsync(PackageConsumptionTest test)
    {
        var tester = await _testerTask;
        await tester.AssertAsync(test);
    }
}
