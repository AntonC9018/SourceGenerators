using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGeneration.Testing.LocalNuGet;

internal sealed class ConsumerFixtureSetupHelper
{
    // TODO: I'd like to replace that eventually.
    private static readonly Regex PackageDirectiveRegex = new(
        @"^(?<prefix>\s*#:\s*package\s+)(?<id>[^\s@]+)(?<version>@[^\s]+)?(?<suffix>\s*)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly string _repositoryRoot;

    public ConsumerFixtureSetupHelper(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public Task<PreparedConsumer> PrepareAsync(
        ConsumerFixture fixture,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> packageVersions,
        CancellationToken cancellationToken)
    {
        return fixture.Kind switch
        {
            ConsumerFixtureKind.SingleFile => PrepareSingleFileAsync(
                fixture,
                destinationDirectory,
                packageVersions,
                cancellationToken),
            ConsumerFixtureKind.ProjectDirectory => PrepareProjectDirectoryAsync(
                fixture,
                destinationDirectory,
                packageVersions,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(fixture),
                fixture.Kind,
                "Unknown fixture kind."),
        };
    }

    private async Task<PreparedConsumer> PrepareSingleFileAsync(
        ConsumerFixture fixture,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> packageVersions,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveRepositoryPath(fixture.Path);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Single-file consumer fixture was not found.",
                sourcePath);
        }

        var destinationPath = Path.Combine(
            destinationDirectory,
            Path.GetFileName(sourcePath));

        await CopyFileAsync(sourcePath, destinationPath, cancellationToken);
        await InjectFilePackageVersionsAsync(
            destinationPath,
            packageVersions,
            cancellationToken);

        return new PreparedConsumer(
            ConsumerFixtureKind.SingleFile,
            destinationPath);
    }

    private async Task<PreparedConsumer> PrepareProjectDirectoryAsync(
        ConsumerFixture fixture,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> packageVersions,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveRepositoryPath(fixture.Path);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Project-directory consumer fixture was not found: {sourcePath}");
        }

        await CopyDirectoryAsync(
            sourcePath,
            destinationDirectory,
            cancellationToken);
        await InjectProjectPackageVersionsAsync(
            destinationDirectory,
            packageVersions,
            cancellationToken);

        return new PreparedConsumer(
            ConsumerFixtureKind.ProjectDirectory,
            ResolveMainProjectPath(fixture, destinationDirectory));
    }

    private async Task InjectFilePackageVersionsAsync(
        string filePath,
        IReadOnlyDictionary<string, string> packageVersions,
        CancellationToken cancellationToken)
    {
        var matchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = await File.ReadAllTextAsync(filePath, cancellationToken);
        var updatedSource = PackageDirectiveRegex.Replace(source, match =>
        {
            var packageId = match.Groups["id"].Value;
            if (!packageVersions.TryGetValue(packageId, out var version))
            {
                return match.Value;
            }

            matchedPackages.Add(packageId);
            var prefix = match.Groups["prefix"].Value;
            var suffix = match.Groups["suffix"].Value;
            return $"{prefix}{packageId}@{version}{suffix}";
        });

        ThrowIfMissingPackageReferences(
            filePath,
            packageVersions,
            matchedPackages);
        await File.WriteAllTextAsync(
            filePath,
            updatedSource,
            cancellationToken);
    }

    private static async Task InjectProjectPackageVersionsAsync(
        string fixtureRoot,
        IReadOnlyDictionary<string, string> packageVersions,
        CancellationToken cancellationToken)
    {
        var matchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectPaths = Directory
            .EnumerateFiles(fixtureRoot, "*.csproj", SearchOption.AllDirectories)
            .ToArray();

        if (projectPaths.Length == 0)
        {
            throw new InvalidOperationException(
                $"Directory fixture '{fixtureRoot}' does not contain a .csproj file.");
        }

        foreach (var projectPath in projectPaths)
        {
            var document = await LoadDocumentAsync(
                projectPath,
                cancellationToken);

            foreach (var packageReference in document
                         .Descendants()
                         .Where(static element =>
                             element.Name.LocalName == "PackageReference"))
            {
                var packageId = packageReference.Attribute("Include")?.Value;
                if (packageId is null
                    || !packageVersions.TryGetValue(packageId, out var version))
                {
                    continue;
                }

                packageReference.SetAttributeValue("Version", version);
                matchedPackages.Add(packageId);
            }

            await SaveDocumentAsync(
                document,
                projectPath,
                cancellationToken);
        }

        ThrowIfMissingPackageReferences(
            fixtureRoot,
            packageVersions,
            matchedPackages);
    }

    private static void ThrowIfMissingPackageReferences(
        string fixturePath,
        IReadOnlyDictionary<string, string> packageVersions,
        ISet<string> matchedPackages)
    {
        var missingPackages = packageVersions
            .Keys
            .Where(packageId => !matchedPackages.Contains(packageId))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingPackages.Length != 0)
        {
            throw new InvalidOperationException(
                $"Consumer fixture '{fixturePath}' does not reference package(s): "
                + $"{string.Join(", ", missingPackages)}.");
        }
    }

    private string ResolveMainProjectPath(
        ConsumerFixture fixture,
        string fixtureRoot)
    {
        if (fixture.MainProjectPath is not null)
        {
            var projectPath = Path.GetFullPath(
                Path.Combine(fixtureRoot, fixture.MainProjectPath));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException(
                    "The specified main project was not found.",
                    projectPath);
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
                $"Directory fixture '{fixture.Path}' contains multiple .csproj files, "
                + "so mainProjectPath is required."),
        };
    }

    private string ResolveRepositoryPath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(_repositoryRoot, path));
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
                cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            await CopyFileAsync(
                file,
                Path.Combine(destinationDirectory, Path.GetFileName(file)),
                cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = OpenRead(sourcePath);
        await using var destination = OpenWrite(
            destinationPath,
            FileMode.CreateNew);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<XDocument> LoadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        return await XDocument.LoadAsync(
            stream,
            LoadOptions.PreserveWhitespace,
            cancellationToken);
    }

    private static async Task SaveDocumentAsync(
        XDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenWrite(path, FileMode.Create);
        await document.SaveAsync(
            stream,
            SaveOptions.DisableFormatting,
            cancellationToken);
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
    }

    private static FileStream OpenWrite(
        string path,
        FileMode mode)
    {
        return new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = mode,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });
    }

    private static bool IsBuildOutputDirectory(string directoryName)
    {
        bool Check(string name)
        {
            string.Equals(
                directoryName,
                name,
                StringComparison.OrdinalIgnoreCase)
        }
        return Check("bin") || Check("obj");
    }
}

internal sealed record PreparedConsumer(
    ConsumerFixtureKind Kind,
    string TargetPath);
