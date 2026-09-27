using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGeneration.Testing.LocalNuGet;

internal static class NuGetPackageReader
{
    public static async Task<PackageIdentity> ReadIdentityAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        await using var packageStream = new FileStream(
            packagePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var nuspecEntry = archive
            .Entries
            .SingleOrDefault(static entry =>
                entry.FullName.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));

        if (nuspecEntry is null)
        {
            throw new InvalidOperationException(
                $"Package '{packagePath}' does not contain a .nuspec file.");
        }

        await using var nuspecStream = nuspecEntry.Open();
        var document = await XDocument.LoadAsync(
            nuspecStream,
            LoadOptions.None,
            cancellationToken);
        var xmlNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(xmlNamespace + "metadata");
        var id = metadata?.Element(xmlNamespace + "id")?.Value;
        var version = metadata?.Element(xmlNamespace + "version")?.Value;

        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                $"Package '{packagePath}' has an invalid .nuspec file.");
        }

        return new PackageIdentity(id, version);
    }
}

internal sealed record PackageIdentity(string Id, string Version);
