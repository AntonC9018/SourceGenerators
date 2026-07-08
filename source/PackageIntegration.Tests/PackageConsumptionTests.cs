using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace PackageIntegration.Tests;

public sealed class PackageConsumptionTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();

    private readonly LocalNuGetPackageTester _tester = new(RepositoryRoot);

    [Fact]
    public Task AutoImplementedPropertiesPackage()
    {
        return _tester.AssertAsync(new PackageConsumptionTest(
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
        return _tester.AssertAsync(new PackageConsumptionTest(
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
        return _tester.AssertAsync(new PackageConsumptionTest(
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
        return _tester.AssertAsync(new PackageConsumptionTest(
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

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFilePath)!,
            "..",
            ".."));
    }
}
