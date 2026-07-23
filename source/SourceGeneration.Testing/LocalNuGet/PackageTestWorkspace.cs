using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGeneration.Testing.LocalNuGet;

internal sealed class PackageTestWorkspace : IDisposable
{
    private bool _deleteOnDispose = true;

    private PackageTestWorkspace(string rootPath)
    {
        RootPath = rootPath;
        FeedPath = Path.Combine(rootPath, "feed");
        GlobalPackagesPath = Path.Combine(rootPath, "global-packages");
        ConsumersPath = Path.Combine(rootPath, "consumers");

        Directory.CreateDirectory(FeedPath);
        Directory.CreateDirectory(GlobalPackagesPath);
        Directory.CreateDirectory(ConsumersPath);
    }

    public string RootPath { get; }

    public string FeedPath { get; }

    public string GlobalPackagesPath { get; }

    public string ConsumersPath { get; }

    public static PackageTestWorkspace Create(string testName)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "SourceGeneration.PackageIntegration",
            $"{SanitizeFileName(testName)}-{Guid.NewGuid():N}");

        return new PackageTestWorkspace(rootPath);
    }

    public string CreateConsumerDirectory(string fixturePath)
    {
        var fixtureName = SanitizeFileName(
            Path.GetFileNameWithoutExtension(fixturePath));
        var directoryName = $"{fixtureName}-{Guid.NewGuid():N}";
        var path = Path.Combine(ConsumersPath, directoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public Task WriteNuGetConfigAsync(
        string consumerDirectory,
        CancellationToken cancellationToken)
    {
        var escapedFeedPath = EscapeXmlAttribute(FeedPath);
        var escapedGlobalPackagesPath = EscapeXmlAttribute(GlobalPackagesPath);
        var path = Path.Combine(consumerDirectory, "NuGet.Config");

        return File.WriteAllTextAsync(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{escapedGlobalPackagesPath}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{escapedFeedPath}" />
              </packageSources>
            </configuration>
            """,
            cancellationToken);
    }

    public void Preserve()
    {
        _deleteOnDispose = false;
    }

    public void Dispose()
    {
        if (!_deleteOnDispose)
        {
            return;
        }

        _deleteOnDispose = false;
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string EscapeXmlAttribute(string value)
    {
        return SecurityElement.Escape(value)!;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "fixture";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
