using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGeneration.Testing.LocalNuGet;

public sealed class LocalNuGetPackageTester
{
    private readonly DotNetCli _dotNet;
    private readonly ConsumerFixtureSetupHelper _fixtureSetup;

    private LocalNuGetPackageTester(
        string repositoryRoot,
        LocalNuGetPackageTesterOptions options)
    {
        RepositoryRoot = repositoryRoot;
        _dotNet = new DotNetCli(
            options.Configuration,
            options.CommandTimeout);
        _fixtureSetup = new ConsumerFixtureSetupHelper(RepositoryRoot);
    }

    public string RepositoryRoot { get; }

    public static async ValueTask<LocalNuGetPackageTester> CreateAsync(
        string? repositoryRoot = null,
        LocalNuGetPackageTesterOptions? options = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string callerFilePath = "")
    {
        cancellationToken.ThrowIfCancellationRequested();

        options ??= new LocalNuGetPackageTesterOptions();
        ValidateOptions(options);

        var resolvedRepositoryRoot = repositoryRoot is null
            ? await RepositoryRootLocator.FindAsync(
                GetCallerDirectory(callerFilePath),
                cancellationToken)
            : ResolveExplicitRepositoryRoot(repositoryRoot);

        return new LocalNuGetPackageTester(
            resolvedRepositoryRoot,
            options);
    }

    public async Task AssertAsync(
        PackageConsumptionTest test,
        CancellationToken cancellationToken = default)
    {
        ValidateTest(test);

        using var workspace = PackageTestWorkspace.Create(test.Name);
        try
        {
            var packageVersions = await PackPackagesAsync(
                test.Packages,
                workspace.FeedPath,
                cancellationToken);

            foreach (var fixture in test.Consumers)
            {
                await AssertConsumerAsync(
                    fixture,
                    packageVersions,
                    workspace,
                    cancellationToken);
            }
        }
        catch
        {
            workspace.Preserve();
            throw;
        }
    }

    private static void ValidateTest(PackageConsumptionTest test)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (test.Packages.Count == 0)
        {
            throw new ArgumentException(
                "At least one package project is required.",
                nameof(test));
        }

        if (test.Consumers.Count == 0)
        {
            throw new ArgumentException(
                "At least one consumer fixture is required.",
                nameof(test));
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> PackPackagesAsync(
        IReadOnlyList<PackageProject> packageProjects,
        string feedPath,
        CancellationToken cancellationToken)
    {
        var versions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var packageProject in packageProjects)
        {
            var projectPath = ResolveRepositoryPath(packageProject.ProjectPath);
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException(
                    "Package project was not found.",
                    projectPath);
            }

            var project = _dotNet.ForProject(projectPath, RepositoryRoot);
            var existingPackages = EnumeratePackages(feedPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await _dotNet.ExecuteCheckedAsync(
                project.Pack().OutputTo(feedPath),
                cancellationToken);

            var newPackages = EnumeratePackages(feedPath)
                .Where(path => !existingPackages.Contains(path))
                .ToArray();

            if (newPackages.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected '{projectPath}' to produce exactly one .nupkg, "
                    + $"but found {newPackages.Length}.");
            }

            var package = await NuGetPackageReader.ReadAsync(
                newPackages[0],
                cancellationToken);
            AssertPackageEntries(packageProject, package);
            if (!versions.TryAdd(package.Id, package.Version))
            {
                throw new InvalidOperationException(
                    $"Package id '{package.Id}' was produced more than once.");
            }
        }

        return versions;
    }

    private static void AssertPackageEntries(
        PackageProject project,
        PackageIdentity package)
    {
        if (project.ExpectedEntries is null)
        {
            return;
        }

        var missing = project.ExpectedEntries
            .Where(entry => !package.Entries.Contains(entry))
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Package '{package.Id}' is missing entries: {string.Join(", ", missing)}.");
    }

    private async Task AssertConsumerAsync(
        ConsumerFixture fixture,
        IReadOnlyDictionary<string, string> packageVersions,
        PackageTestWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var consumerDirectory = workspace.CreateConsumerDirectory(fixture.Path);
        cancellationToken.ThrowIfCancellationRequested();
        workspace.WriteNuGetConfig(consumerDirectory);

        var consumer = await _fixtureSetup.PrepareAsync(
            fixture,
            consumerDirectory,
            packageVersions,
            cancellationToken);
        CopyRepositoryGlobalJson(consumerDirectory);
        var target = consumer.Kind switch
        {
            ConsumerFixtureKind.SingleFile => _dotNet.ForFile(
                consumer.TargetPath,
                consumerDirectory),
            ConsumerFixtureKind.ProjectDirectory => _dotNet.ForProject(
                consumer.TargetPath,
                consumerDirectory),
            _ => throw new ArgumentOutOfRangeException(
                nameof(fixture),
                consumer.Kind,
                "Unknown fixture kind."),
        };

        if (fixture.CodeFix is null)
        {
            await _dotNet.ExecuteCheckedAsync(
                target.Build().NoCache(),
                cancellationToken);
        }
        else
        {
            await AssertCodeFixAsync(
                target,
                fixture.CodeFix,
                consumerDirectory,
                cancellationToken);
        }

        if (fixture.Run is null)
        {
            return;
        }

        var result = await _dotNet.ExecuteAsync(
            target.Run().NoBuild(),
            cancellationToken);
        AssertRunResult(result, fixture.Run);
    }

    private void CopyRepositoryGlobalJson(string consumerDirectory)
    {
        var repositoryGlobalJson = Path.Combine(RepositoryRoot, "global.json");
        if (!File.Exists(repositoryGlobalJson))
        {
            return;
        }

        var consumerGlobalJson = Path.Combine(consumerDirectory, "global.json");
        if (File.Exists(consumerGlobalJson))
        {
            return;
        }

        File.Copy(repositoryGlobalJson, consumerGlobalJson);
    }

    private async Task AssertCodeFixAsync(
        DotNetTarget target,
        CodeFixExpectation codeFix,
        string consumerDirectory,
        CancellationToken cancellationToken)
    {
        ValidateCodeFix(codeFix);

        var sourcePath = Path.GetFullPath(
            Path.Combine(consumerDirectory, codeFix.SourcePath));
        var expectedPath = ResolveRepositoryPath(codeFix.ExpectedSourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "The consumer source file was not found.",
                sourcePath);
        }

        if (!File.Exists(expectedPath))
        {
            throw new FileNotFoundException(
                "The expected fixed source file was not found.",
                expectedPath);
        }

        var build = target.Build()
            .NoCache()
            .WarningsAsErrors(codeFix.DiagnosticId);
        var beforeFix = await _dotNet.ExecuteAsync(build, cancellationToken);
        if (beforeFix.IsSuccess)
        {
            throw new DotNetCommandFailedException(
                $"Expected diagnostic {codeFix.DiagnosticId} before the code fix.",
                beforeFix);
        }

        var diagnosticMarker = "error " + codeFix.DiagnosticId;
        if (!ContainsDiagnostic(beforeFix, diagnosticMarker))
        {
            throw new DotNetCommandFailedException(
                $"The consumer failed without diagnostic {codeFix.DiagnosticId}.",
                beforeFix);
        }

        var format = target.FormatAnalyzers(codeFix.DiagnosticId);
        await _dotNet.ExecuteCheckedAsync(format, cancellationToken);

        var actualSource = await File.ReadAllTextAsync(
            sourcePath,
            cancellationToken);
        var expectedSource = await File.ReadAllTextAsync(
            expectedPath,
            cancellationToken);
        var normalizedActual = NormalizeLineEndings(actualSource);
        var normalizedExpected = NormalizeLineEndings(expectedSource);
        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The code fix did not produce the expected source. "
                + $"Expected: {expectedPath}. Actual: {sourcePath}.");
        }

        var finalBuild = target.Build()
            .NoCache()
            .WarningsAsErrors(codeFix.DiagnosticId);
        await _dotNet.ExecuteCheckedAsync(finalBuild, cancellationToken);
    }

    private static bool ContainsDiagnostic(
        DotNetCommandResult result,
        string diagnosticMarker)
    {
        if (result.StandardOutput.Contains(diagnosticMarker, StringComparison.Ordinal))
        {
            return true;
        }

        if (result.StandardError.Contains(diagnosticMarker, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static void ValidateCodeFix(CodeFixExpectation codeFix)
    {
        if (string.IsNullOrWhiteSpace(codeFix.DiagnosticId))
        {
            throw new ArgumentException("A diagnostic id is required.", nameof(codeFix));
        }

        if (string.IsNullOrWhiteSpace(codeFix.SourcePath))
        {
            throw new ArgumentException("A consumer source path is required.", nameof(codeFix));
        }

        if (string.IsNullOrWhiteSpace(codeFix.ExpectedSourcePath))
        {
            throw new ArgumentException("An expected source path is required.", nameof(codeFix));
        }
    }

    private static string NormalizeLineEndings(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static void AssertRunResult(
        DotNetCommandResult result,
        RunOptions expected)
    {
        const string message = "dotnet run did not match the expected result.";

        if (result.ExitCode != expected.ExpectedExitCode)
        {
            var err = $"{message} Expected exit code {expected.ExpectedExitCode}, but got {result.ExitCode}.";
            throw new DotNetCommandFailedException(err, result);
        }

        if (expected.StandardOutputContains is not null
            && !result.StandardOutput.Contains(
                expected.StandardOutputContains,
                StringComparison.Ordinal))
        {
            var err = $"{message} Standard output did not contain '{expected.StandardOutputContains}'.";
            throw new DotNetCommandFailedException(err, result);
        }

        if (expected.StandardErrorContains is not null
            && !result.StandardError.Contains(
                expected.StandardErrorContains,
                StringComparison.Ordinal))
        {
            var err = $"{message} Standard error did not contain '{expected.StandardErrorContains}'.";
            throw new DotNetCommandFailedException(err, result);
        }
    }

    private static IEnumerable<string> EnumeratePackages(string feedPath)
    {
        return Directory
            .EnumerateFiles(feedPath, "*.nupkg")
            .Where(static path =>
                !path.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveRepositoryPath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(RepositoryRoot, path));
    }

    private static void ValidateOptions(
        LocalNuGetPackageTesterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Configuration))
        {
            throw new ArgumentException(
                "A build configuration is required.",
                nameof(options));
        }

        if (options.CommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CommandTimeout,
                "The command timeout must be positive.");
        }
    }

    private static string ResolveExplicitRepositoryRoot(
        string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException(
                "The repository root cannot be empty.",
                nameof(repositoryRoot));
        }

        var fullPath = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The repository root '{fullPath}' does not exist.");
        }

        return fullPath;
    }

    private static string GetCallerDirectory(string callerFilePath)
    {
        var directory = Path.GetDirectoryName(callerFilePath);
        return string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
    }
}
