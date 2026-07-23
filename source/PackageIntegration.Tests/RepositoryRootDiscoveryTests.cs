using System;
using System.IO;
using System.Threading.Tasks;
using SourceGeneration.Testing.LocalNuGet;
using Xunit;

namespace PackageIntegration.Tests;

public sealed class RepositoryRootDiscoveryTests
{
    [Fact]
    public async Task UsesExplicitRepositoryRootWithoutDiscovery()
    {
        var root = CreateTestDirectory();

        try
        {
            var tester = await LocalNuGetPackageTester.CreateAsync(root);

            Assert.Equal(root, tester.RepositoryRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrefersGitMarkerOverCloserSolution()
    {
        var root = CreateTestDirectory();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, ".git"),
                "gitdir: elsewhere");
            var nested = Directory.CreateDirectory(
                Path.Combine(root, "nested"));
            await File.WriteAllTextAsync(
                Path.Combine(nested.FullName, "Nested.slnx"),
                "<Solution />");
            var callerDirectory = Directory.CreateDirectory(
                Path.Combine(nested.FullName, "tests"));

            var tester = await LocalNuGetPackageTester.CreateAsync(
                callerFilePath: Path.Combine(
                    callerDirectory.FullName,
                    "Tests.cs"));

            Assert.Equal(root, tester.RepositoryRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UsesNearestSolutionWhenGitMarkerIsMissing()
    {
        var root = CreateTestDirectory();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Root.sln"),
                string.Empty);
            var nested = Directory.CreateDirectory(
                Path.Combine(root, "nested"));
            await File.WriteAllTextAsync(
                Path.Combine(nested.FullName, "Nested.slnx"),
                "<Solution />");
            var callerDirectory = Directory.CreateDirectory(
                Path.Combine(nested.FullName, "tests"));

            var tester = await LocalNuGetPackageTester.CreateAsync(
                callerFilePath: Path.Combine(
                    callerDirectory.FullName,
                    "Tests.cs"));

            Assert.Equal(nested.FullName, tester.RepositoryRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ThrowsWhenNoRepositoryMarkerExists()
    {
        var root = CreateTestDirectory();

        try
        {
            var callerDirectory = Directory.CreateDirectory(
                Path.Combine(root, "tests"));

            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => LocalNuGetPackageTester.CreateAsync(
                    callerFilePath: Path.Combine(
                        callerDirectory.FullName,
                        "Tests.cs")).AsTask());

            Assert.Contains(
                "Expected a .git entry or a .sln/.slnx file",
                exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            nameof(RepositoryRootDiscoveryTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
