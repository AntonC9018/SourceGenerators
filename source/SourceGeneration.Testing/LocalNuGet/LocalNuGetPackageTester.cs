using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;

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

public sealed record LocalNuGetPackageTesterOptions
{
    public string Configuration { get; init; } = "Release";

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

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
    private readonly string _configuration;
    private readonly TimeSpan _commandTimeout;
    private readonly Command _dotNetCommand;

    public LocalNuGetPackageTester(string repositoryRoot)
        : this(repositoryRoot, new LocalNuGetPackageTesterOptions())
    {
    }

    public LocalNuGetPackageTester(
        string repositoryRoot,
        LocalNuGetPackageTesterOptions options)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("A repository root is required.", nameof(repositoryRoot));
        }

        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Configuration))
        {
            throw new ArgumentException("A build configuration is required.", nameof(options));
        }

        if (options.CommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CommandTimeout,
                "The command timeout must be positive.");
        }

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _configuration = options.Configuration;
        _commandTimeout = options.CommandTimeout;
        _dotNetCommand = Cli.Wrap("dotnet")
            .WithWorkingDirectory(_repositoryRoot)
            .WithEnvironmentVariables(environment => environment
                .Set("DOTNET_NOLOGO", "1")
                .Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
                .Set("MSBUILDDISABLENODEREUSE", "1"))
            .WithValidation(CommandResultValidation.None);
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

            var project = CreateProjectCommands(projectPath, _repositoryRoot);

            await RunDotNetOrThrowAsync(project.Build(), cancellationToken)
                .ConfigureAwait(false);

            var existingPackages = Directory
                .EnumerateFiles(tempFeed, "*.nupkg")
                .Where(static p => !p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await RunDotNetOrThrowAsync(
                    project.Pack(tempFeed, noBuild: true),
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

            var package = await ReadPackedPackageAsync(newPackages[0], cancellationToken)
                .ConfigureAwait(false);
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
        await WriteNuGetConfigAsync(
                nuGetConfigPath,
                tempFeed,
                globalPackagesFolder,
                cancellationToken)
            .ConfigureAwait(false);

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
        await CopyFileAsync(sourcePath, tempFilePath, cancellationToken)
            .ConfigureAwait(false);
        await InjectFilePackageVersionsAsync(tempFilePath, packages, cancellationToken)
            .ConfigureAwait(false);

        var project = CreateProjectCommands(tempFilePath, fixtureRoot, "--file");

        await RunDotNetOrThrowAsync(project.Build(noCache: true), cancellationToken)
            .ConfigureAwait(false);

        if (fixture.Run is not null)
        {
            var result = await RunDotNetAsync(project.Run(noBuild: true), cancellationToken)
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

        await CopyDirectoryAsync(sourcePath, fixtureRoot, cancellationToken)
            .ConfigureAwait(false);
        await InjectProjectPackageVersionsAsync(fixtureRoot, packages, cancellationToken)
            .ConfigureAwait(false);

        var mainProjectPath = ResolveMainProjectPath(fixture, fixtureRoot);

        var project = CreateProjectCommands(mainProjectPath, fixtureRoot);

        await RunDotNetOrThrowAsync(project.Build(noCache: true), cancellationToken)
            .ConfigureAwait(false);

        if (fixture.Run is not null)
        {
            var result = await RunDotNetAsync(project.Run(noBuild: true), cancellationToken)
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

    private static async Task InjectFilePackageVersionsAsync(
        string filePath,
        IReadOnlyDictionary<string, PackedPackage> packages,
        CancellationToken cancellationToken)
    {
        var matchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = await File.ReadAllTextAsync(filePath, cancellationToken)
            .ConfigureAwait(false);
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
        await File.WriteAllTextAsync(filePath, updatedText, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task InjectProjectPackageVersionsAsync(
        string fixtureRoot,
        IReadOnlyDictionary<string, PackedPackage> packages,
        CancellationToken cancellationToken)
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
            var document = await LoadDocumentAsync(
                    projectPath,
                    LoadOptions.PreserveWhitespace,
                    cancellationToken)
                .ConfigureAwait(false);
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

            await SaveDocumentAsync(
                    document,
                    projectPath,
                    SaveOptions.DisableFormatting,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static async Task<PackedPackage> ReadPackedPackageAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        await using var packageStream = OpenReadStream(packagePath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var nuspecEntry = archive
            .Entries
            .SingleOrDefault(static e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (nuspecEntry is null)
        {
            throw new InvalidOperationException($"Package '{packagePath}' does not contain a .nuspec file.");
        }

        await using var stream = nuspecEntry.Open();
        var document = await XDocument.LoadAsync(
                stream,
                LoadOptions.None,
                cancellationToken)
            .ConfigureAwait(false);
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
        DotNetCommand command,
        CancellationToken cancellationToken)
    {
        var result = await RunDotNetAsync(command, cancellationToken)
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

    private async Task<DotNetCommandResult> RunDotNetAsync(
        DotNetCommand command,
        CancellationToken cancellationToken)
    {
        using var commandCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandCancellationTokenSource.CancelAfter(_commandTimeout);

        try
        {
            var result = await command.Command
                .ExecuteBufferedAsync(commandCancellationTokenSource.Token)
                .ConfigureAwait(false);

            return new DotNetCommandResult(
                command.Command.TargetFilePath,
                command.Arguments,
                command.Command.WorkingDirPath,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
        catch (OperationCanceledException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"dotnet command timed out after {_commandTimeout}. "
                + $"Command: {command.Command.TargetFilePath} {command.Command.Arguments}. "
                + $"Working directory: {command.Command.WorkingDirPath}.",
                exception);
        }
    }

    private static Task WriteNuGetConfigAsync(
        string path,
        string tempFeed,
        string globalPackagesFolder,
        CancellationToken cancellationToken)
    {
        var escapedTempFeed = SecurityElement.Escape(tempFeed);
        var escapedGlobalPackagesFolder = SecurityElement.Escape(globalPackagesFolder);

        return File.WriteAllTextAsync(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{escapedGlobalPackagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{escapedTempFeed}" />
              </packageSources>
            </configuration>
            """,
            cancellationToken);
    }

    private string ResolveRepositoryPath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(_repositoryRoot, path));
    }

    private DotNetProjectCommands CreateProjectCommands(
        string projectPath,
        string workingDirectory,
        string runTargetOption = "--project")
    {
        return new DotNetProjectCommands(
            _dotNetCommand.WithWorkingDirectory(workingDirectory),
            projectPath,
            _configuration,
            runTargetOption);
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var directoryName = Path.GetFileName(directory);
            if (IsBuildOutputDirectory(directoryName))
            {
                continue;
            }

            await CopyDirectoryAsync(
                    directory,
                    Path.Combine(destinationDirectory, directoryName),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            await CopyFileAsync(
                    file,
                    Path.Combine(destinationDirectory, Path.GetFileName(file)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = OpenReadStream(sourcePath);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<XDocument> LoadDocumentAsync(
        string path,
        LoadOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenReadStream(path);
        return await XDocument.LoadAsync(stream, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task SaveDocumentAsync(
        XDocument document,
        string path,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await document.SaveAsync(stream, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static FileStream OpenReadStream(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static bool IsBuildOutputDirectory(string directoryName)
    {
        return string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase);
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

    private sealed record DotNetCommand(
        Command Command,
        IReadOnlyList<string> Arguments);

    private sealed record DotNetCommandResult(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class DotNetProjectCommands
    {
        private readonly Command _baseCommand;
        private readonly string _projectPath;
        private readonly string _configuration;
        private readonly string _runTargetOption;

        public DotNetProjectCommands(
            Command baseCommand,
            string projectPath,
            string configuration,
            string runTargetOption)
        {
            _baseCommand = baseCommand;
            _projectPath = projectPath;
            _configuration = configuration;
            _runTargetOption = runTargetOption;
        }

        public DotNetCommand Build(bool noCache = false)
        {
            return Create(
                "build",
                targetOption: null,
                noCache ? new[] { "--no-cache" } : Array.Empty<string>());
        }

        public DotNetCommand Pack(string packageOutputPath, bool noBuild = false)
        {
            var options = new List<string>();
            if (noBuild)
            {
                options.Add("--no-build");
            }

            options.Add("-p:PackageOutputPath=" + packageOutputPath);
            return Create("pack", targetOption: null, options);
        }

        public DotNetCommand Run(bool noBuild = false)
        {
            return Create(
                "run",
                _runTargetOption,
                noBuild ? new[] { "--no-build" } : Array.Empty<string>());
        }

        private DotNetCommand Create(
            string verb,
            string? targetOption,
            IReadOnlyCollection<string> options)
        {
            var arguments = new List<string>(5 + options.Count)
            {
                verb,
            };

            if (targetOption is not null)
            {
                arguments.Add(targetOption);
            }

            arguments.Add(_projectPath);
            arguments.Add("--configuration");
            arguments.Add(_configuration);
            arguments.AddRange(options);

            var argumentArray = arguments.ToArray();
            return new DotNetCommand(
                _baseCommand.WithArguments(argumentArray),
                argumentArray);
        }
    }
}

internal enum ConsumerFixtureKind
{
    SingleFile,
    ProjectDirectory,
}
