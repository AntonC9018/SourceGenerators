using System;
using System.IO;
using System.Linq;
using System.Text;
using NuGet.Configuration;

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

    public void WriteNuGetConfig(string consumerDirectory)
    {
        var settings = new Settings(consumerDirectory, "NuGet.Config");
        settings.AddOrUpdate(
            ConfigurationConstants.Config,
            new AddItem(
                ConfigurationConstants.GlobalPackagesFolder,
                GlobalPackagesPath));
        settings.AddOrUpdate(
            ConfigurationConstants.PackageSources,
            new ClearItem());
        settings.AddOrUpdate(
            ConfigurationConstants.PackageSources,
            new SourceItem("local", FeedPath));
        settings.SaveToDisk();
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
