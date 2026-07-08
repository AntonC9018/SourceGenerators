using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGeneration.Testing.LocalNuGet;

public sealed record PackageProject(string ProjectPath);

public sealed record PackageConsumptionTest(
    string Name,
    IReadOnlyList<PackageProject> Packages,
    IReadOnlyList<ConsumerFixture> Consumers);

public sealed record ConsumerFixture
{
    private ConsumerFixture(
        ConsumerFixtureKind kind,
        string path,
        string? mainProjectPath,
        RunOptions? run)
    {
        Kind = kind;
        Path = path;
        MainProjectPath = mainProjectPath;
        Run = run;
    }

    public string Path { get; }

    public string? MainProjectPath { get; }

    public RunOptions? Run { get; }

    internal ConsumerFixtureKind Kind { get; }

    public static ConsumerFixture SingleFile(
        string path,
        RunOptions? run = null)
    {
        return new ConsumerFixture(ConsumerFixtureKind.SingleFile, path, null, run);
    }

    public static ConsumerFixture ProjectDirectory(
        string path,
        string? mainProjectPath = null,
        RunOptions? run = null)
    {
        return new ConsumerFixture(ConsumerFixtureKind.ProjectDirectory, path, mainProjectPath, run);
    }
}

public sealed record RunOptions(
    int ExpectedExitCode = 0,
    string? StandardOutputContains = null,
    string? StandardErrorContains = null);

public sealed class DotNetCommandFailedException : Exception
{
    internal DotNetCommandFailedException(
        string message,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int exitCode,
        string standardOutput,
        string standardError)
        : base(CreateMessage(
            message,
            fileName,
            arguments,
            workingDirectory,
            exitCode,
            standardOutput,
            standardError))
    {
        FileName = fileName;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string WorkingDirectory { get; }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    private static string CreateMessage(
        string message,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        var builder = new StringBuilder();
        builder.AppendLine(message);
        builder.Append("Command: ");
        builder.Append(fileName);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteArgument(argument));
        }
        builder.AppendLine();
        builder.AppendLine($"Working directory: {workingDirectory}");
        builder.AppendLine($"Exit code: {exitCode}");
        builder.AppendLine("Standard output:");
        builder.AppendLine(standardOutput);
        builder.AppendLine("Standard error:");
        builder.AppendLine(standardError);
        return builder.ToString();
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;
    }
}

public sealed class LocalNuGetPackageTester
{
    private static readonly Regex PackageDirectiveRegex = new(
        @"^(?<prefix>\s*#:\s*package\s+)(?<id>[^\s@]+)(?<version>@[^\s]+)?(?<suffix>\s*)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly string _repositoryRoot;

    public LocalNuGetPackageTester(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("A repository root is required.", nameof(repositoryRoot));
        }

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public async Task AssertAsync(
        PackageConsumptionTest test,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (test.Packages.Count == 0)
        {
            throw new ArgumentException("At least one package project is required.", nameof(test));
        }

        if (test.Consumers.Count == 0)
        {
            throw new ArgumentException("At least one consumer fixture is required.", nameof(test));
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "SourceGeneration.PackageIntegration",
            SanitizeFileName(test.Name) + "-" + Guid.NewGuid().ToString("N"));
        var tempFeed = Path.Combine(tempRoot, "feed");
        var globalPackagesFolder = Path.Combine(tempRoot, "global-packages");
        var consumersRoot = Path.Combine(tempRoot, "consumers");

        Directory.CreateDirectory(tempFeed);
        Directory.CreateDirectory(globalPackagesFolder);
        Directory.CreateDirectory(consumersRoot);

        var deleteTemp = true;
        try
        {
            var packages = await PackPackagesAsync(test.Packages, tempFeed, cancellationToken)
                .ConfigureAwait(false);

            foreach (var fixture in test.Consumers)
            {
                await AssertConsumerAsync(
                        fixture,
                        packages,
                        consumersRoot,
                        tempFeed,
                        globalPackagesFolder,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            deleteTemp = false;
            throw;
        }
        finally
        {
            if (deleteTemp)
            {
                DeleteDirectoryBestEffort(tempRoot);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, PackedPackage>> PackPackagesAsync(
        IReadOnlyList<PackageProject> packageProjects,
        string tempFeed,
        CancellationToken cancellationToken)
    {
        var packages = new Dictionary<string, PackedPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var packageProject in packageProjects)
        {
            var projectPath = ResolveRepositoryPath(packageProject.ProjectPath);
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("Package project was not found.", projectPath);
            }

            await RunDotNetOrThrowAsync(
                    new[] { "build", projectPath, "--configuration", "Debug" },
                    _repositoryRoot,
                    cancellationToken)
                .ConfigureAwait(false);

            var existingPackages = Directory
                .EnumerateFiles(tempFeed, "*.nupkg")
                .Where(static p => !p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await RunDotNetOrThrowAsync(
                    new[]
                    {
                        "pack",
                        projectPath,
                        "--configuration",
                        "Debug",
                        "--no-build",
                        "-p:PackageOutputPath=" + tempFeed,
                    },
                    _repositoryRoot,
                    cancellationToken)
                .ConfigureAwait(false);

            var newPackages = Directory
                .EnumerateFiles(tempFeed, "*.nupkg")
                .Where(static p => !p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .Where(p => !existingPackages.Contains(p))
                .ToArray();

            if (newPackages.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected '{projectPath}' to produce exactly one .nupkg, but found {newPackages.Length}.");
            }

            var package = ReadPackedPackage(newPackages[0]);
            if (packages.ContainsKey(package.Id))
            {
                throw new InvalidOperationException($"Package id '{package.Id}' was produced more than once.");
            }

            packages.Add(package.Id, package);
        }

        return packages;
    }

    private async Task AssertConsumerAsync(
        ConsumerFixture fixture,
        IReadOnlyDictionary<string, PackedPackage> packages,
        string consumersRoot,
        string tempFeed,
        string globalPackagesFolder,
        CancellationToken cancellationToken)
    {
        var fixtureRoot = Path.Combine(
            consumersRoot,
            SanitizeFileName(Path.GetFileNameWithoutExtension(fixture.Path)) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        var nuGetConfigPath = Path.Combine(fixtureRoot, "NuGet.Config");
        WriteNuGetConfig(nuGetConfigPath, tempFeed, globalPackagesFolder);

        switch (fixture.Kind)
        {
            case ConsumerFixtureKind.SingleFile:
                await AssertSingleFileConsumerAsync(
                        fixture,
                        packages,
                        fixtureRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ConsumerFixtureKind.ProjectDirectory:
                await AssertProjectDirectoryConsumerAsync(
                        fixture,
                        packages,
                        fixtureRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fixture), fixture.Kind, "Unknown fixture kind.");
        }
    }

    private async Task AssertSingleFileConsumerAsync(
        ConsumerFixture fixture,
        IReadOnlyDictionary<string, PackedPackage> packages,
        string fixtureRoot,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveRepositoryPath(fixture.Path);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Single-file consumer fixture was not found.", sourcePath);
        }

        var tempFilePath = Path.Combine(fixtureRoot, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, tempFilePath);
        InjectFilePackageVersions(tempFilePath, packages);

        await RunDotNetOrThrowAsync(
                new[] { "build", tempFilePath, "--no-cache" },
                fixtureRoot,
                cancellationToken)
            .ConfigureAwait(false);

        if (fixture.Run is not null)
        {
            var result = await RunDotNetAsync(
                    new[] { "run", "--file", tempFilePath, "--no-build" },
                    fixtureRoot,
                    cancellationToken)
                .ConfigureAwait(false);

            AssertRunResult(
                result,
                fixture.Run,
                "dotnet run did not match the expected result.");
        }
    }

    private async Task AssertProjectDirectoryConsumerAsync(
        ConsumerFixture fixture,
        IReadOnlyDictionary<string, PackedPackage> packages,
        string fixtureRoot,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveRepositoryPath(fixture.Path);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Project-directory consumer fixture was not found: {sourcePath}");
        }

        CopyDirectory(sourcePath, fixtureRoot);
        InjectProjectPackageVersions(fixtureRoot, packages);

        var mainProjectPath = ResolveMainProjectPath(fixture, fixtureRoot);

        await RunDotNetOrThrowAsync(
                new[] { "build", mainProjectPath, "--no-cache" },
                fixtureRoot,
                cancellationToken)
            .ConfigureAwait(false);

        if (fixture.Run is not null)
        {
            var result = await RunDotNetAsync(
                    new[] { "run", "--project", mainProjectPath, "--no-build" },
                    fixtureRoot,
                    cancellationToken)
                .ConfigureAwait(false);

            AssertRunResult(
                result,
                fixture.Run,
                "dotnet run did not match the expected result.");
        }
    }

    private string ResolveMainProjectPath(ConsumerFixture fixture, string fixtureRoot)
    {
        if (fixture.MainProjectPath is not null)
        {
            var projectPath = Path.GetFullPath(Path.Combine(fixtureRoot, fixture.MainProjectPath));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("The specified main project was not found.", projectPath);
            }

            return projectPath;
        }

        var projects = Directory
            .EnumerateFiles(fixtureRoot, "*.csproj", SearchOption.AllDirectories)
            .ToArray();

        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new InvalidOperationException(
                $"Directory fixture '{fixture.Path}' does not contain a .csproj file."),
            _ => throw new InvalidOperationException(
                $"Directory fixture '{fixture.Path}' contains multiple .csproj files, so mainProjectPath is required."),
        };
    }

    private static void AssertRunResult(
        DotNetCommandResult result,
        RunOptions run,
        string message)
    {
        if (result.ExitCode != run.ExpectedExitCode)
        {
            throw new DotNetCommandFailedException(
                $"{message} Expected exit code {run.ExpectedExitCode}, but got {result.ExitCode}.",
                result.FileName,
                result.Arguments,
                result.WorkingDirectory,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        if (run.StandardOutputContains is not null
            && !result.StandardOutput.Contains(run.StandardOutputContains, StringComparison.Ordinal))
        {
            throw new DotNetCommandFailedException(
                $"{message} Standard output did not contain '{run.StandardOutputContains}'.",
                result.FileName,
                result.Arguments,
                result.WorkingDirectory,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        if (run.StandardErrorContains is not null
            && !result.StandardError.Contains(run.StandardErrorContains, StringComparison.Ordinal))
        {
            throw new DotNetCommandFailedException(
                $"{message} Standard error did not contain '{run.StandardErrorContains}'.",
                result.FileName,
                result.Arguments,
                result.WorkingDirectory,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
    }

    private static void InjectFilePackageVersions(
        string filePath,
        IReadOnlyDictionary<string, PackedPackage> packages)
    {
        var matchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = File.ReadAllText(filePath);
        var updatedText = PackageDirectiveRegex.Replace(text, match =>
        {
            var id = match.Groups["id"].Value;
            if (!packages.TryGetValue(id, out var package))
            {
                return match.Value;
            }

            matchedPackages.Add(id);
            return match.Groups["prefix"].Value
                + id
                + "@"
                + package.Version
                + match.Groups["suffix"].Value;
        });

        ThrowIfMissingPackageReferences(filePath, packages, matchedPackages);
        File.WriteAllText(filePath, updatedText);
    }

    private static void InjectProjectPackageVersions(
        string fixtureRoot,
        IReadOnlyDictionary<string, PackedPackage> packages)
    {
        var matchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectPaths = Directory
            .EnumerateFiles(fixtureRoot, "*.csproj", SearchOption.AllDirectories)
            .ToArray();

        if (projectPaths.Length == 0)
        {
            throw new InvalidOperationException($"Directory fixture '{fixtureRoot}' does not contain a .csproj file.");
        }

        foreach (var projectPath in projectPaths)
        {
            var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
            foreach (var packageReference in document
                         .Descendants()
                         .Where(static e => e.Name.LocalName == "PackageReference"))
            {
                var include = packageReference.Attribute("Include")?.Value;
                if (include is null || !packages.TryGetValue(include, out var package))
                {
                    continue;
                }

                packageReference.SetAttributeValue("Version", package.Version);
                matchedPackages.Add(include);
            }

            document.Save(projectPath, SaveOptions.DisableFormatting);
        }

        ThrowIfMissingPackageReferences(fixtureRoot, packages, matchedPackages);
    }

    private static void ThrowIfMissingPackageReferences(
        string fixturePath,
        IReadOnlyDictionary<string, PackedPackage> packages,
        ISet<string> matchedPackages)
    {
        var missingPackages = packages
            .Keys
            .Where(packageId => !matchedPackages.Contains(packageId))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingPackages.Length != 0)
        {
            throw new InvalidOperationException(
                $"Consumer fixture '{fixturePath}' does not reference package(s): {string.Join(", ", missingPackages)}.");
        }
    }

    private static PackedPackage ReadPackedPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive
            .Entries
            .SingleOrDefault(static e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (nuspecEntry is null)
        {
            throw new InvalidOperationException($"Package '{packagePath}' does not contain a .nuspec file.");
        }

        using var stream = nuspecEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(ns + "metadata");
        var id = metadata?.Element(ns + "id")?.Value;
        var version = metadata?.Element(ns + "version")?.Value;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"Package '{packagePath}' has an invalid .nuspec file.");
        }

        return new PackedPackage(id, version, packagePath);
    }

    private async Task RunDotNetOrThrowAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunDotNetAsync(arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new DotNetCommandFailedException(
                "dotnet command failed.",
                result.FileName,
                result.Arguments,
                result.WorkingDirectory,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
    }

    private static async Task<DotNetCommandResult> RunDotNetAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet.");
        }

        try
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            return new DotNetCommandResult(
                "dotnet",
                arguments.ToArray(),
                workingDirectory,
                process.ExitCode,
                standardOutput,
                standardError);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void WriteNuGetConfig(
        string path,
        string tempFeed,
        string globalPackagesFolder)
    {
        var document = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "config",
                    new XElement(
                        "add",
                        new XAttribute("key", "globalPackagesFolder"),
                        new XAttribute("value", globalPackagesFolder))),
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "local"),
                        new XAttribute("value", tempFeed)))));

        document.Save(path);
    }

    private string ResolveRepositoryPath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(_repositoryRoot, path));
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath);
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "fixture";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PackedPackage(string Id, string Version, string PackagePath);

    private sealed record DotNetCommandResult(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

internal enum ConsumerFixtureKind
{
    SingleFile,
    ProjectDirectory,
}
