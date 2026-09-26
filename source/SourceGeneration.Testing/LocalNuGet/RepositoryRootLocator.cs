using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGeneration.Testing.LocalNuGet;

internal static class RepositoryRootLocator
{
    public static Task<string> FindAsync(
        string startDirectory,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Find(startDirectory, cancellationToken),
            cancellationToken);
    }

    private static string Find(
        string startDirectory,
        CancellationToken cancellationToken)
    {
        var firstDirectory = new DirectoryInfo(
            Path.GetFullPath(startDirectory));

        if (!firstDirectory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"Repository-root discovery cannot start because "
                + $"'{firstDirectory.FullName}' does not exist.");
        }

        var gitRoot = FindParent(
            firstDirectory,
            ContainsGitMarker,
            cancellationToken);
        if (gitRoot is not null)
        {
            return gitRoot;
        }

        var solutionRoot = FindParent(
            firstDirectory,
            ContainsSolution,
            cancellationToken);
        if (solutionRoot is not null)
        {
            return solutionRoot;
        }

        throw new DirectoryNotFoundException(
            $"Could not find a repository root from "
            + $"'{firstDirectory.FullName}'. Expected a .git entry or "
            + $"a .sln/.slnx file in that directory or one of its parents.");
    }

    private static string? FindParent(
        DirectoryInfo firstDirectory,
        Func<DirectoryInfo, bool> isMatch,
        CancellationToken cancellationToken)
    {
        for (var directory = firstDirectory;
             directory is not null;
             directory = directory.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isMatch(directory))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool ContainsGitMarker(DirectoryInfo directory)
    {
        var path = Path.Combine(directory.FullName, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }

    private static bool ContainsSolution(DirectoryInfo directory)
    {
        foreach (var path in Directory.EnumerateFiles(
                     directory.FullName,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (string.Equals(
                    extension,
                    ".sln",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    extension,
                    ".slnx",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
