using System;
using System.Collections.Generic;

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
        return new ConsumerFixture(
            ConsumerFixtureKind.SingleFile,
            path,
            mainProjectPath: null,
            run);
    }

    public static ConsumerFixture ProjectDirectory(
        string path,
        string? mainProjectPath = null,
        RunOptions? run = null)
    {
        return new ConsumerFixture(
            ConsumerFixtureKind.ProjectDirectory,
            path,
            mainProjectPath,
            run);
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

internal enum ConsumerFixtureKind
{
    SingleFile,
    ProjectDirectory,
}
