using System;
using System.Collections.Generic;

namespace SourceGeneration.Testing.LocalNuGet;

internal sealed record PackageProject(string ProjectPath);

public sealed class PackageConsumptionTest
{
    internal PackageConsumptionTest(
        string name,
        IReadOnlyList<PackageProject> packages,
        IReadOnlyList<ConsumerFixture> consumers)
    {
        Name = name;
        Packages = packages;
        Consumers = consumers;
    }

    internal string Name { get; }

    internal IReadOnlyList<PackageProject> Packages { get; }

    internal IReadOnlyList<ConsumerFixture> Consumers { get; }
}

internal sealed record CodeFixExpectation(
    string DiagnosticId,
    string SourcePath,
    string ExpectedSourcePath);

internal sealed record ConsumerFixture
{
    private ConsumerFixture(
        ConsumerFixtureKind kind,
        string path,
        string? mainProjectPath,
        RunOptions? run,
        CodeFixExpectation? codeFix)
    {
        Kind = kind;
        Path = path;
        MainProjectPath = mainProjectPath;
        Run = run;
        CodeFix = codeFix;
    }

    public string Path { get; }

    public string? MainProjectPath { get; }

    public RunOptions? Run { get; }

    public CodeFixExpectation? CodeFix { get; }

    internal ConsumerFixtureKind Kind { get; }

    public static ConsumerFixture SingleFile(
        string path,
        RunOptions? run = null)
    {
        return new ConsumerFixture(
            ConsumerFixtureKind.SingleFile,
            path,
            mainProjectPath: null,
            run,
            codeFix: null);
    }

    public static ConsumerFixture ProjectDirectory(
        string path,
        string? mainProjectPath = null,
        RunOptions? run = null,
        CodeFixExpectation? codeFix = null)
    {
        return new ConsumerFixture(
            ConsumerFixtureKind.ProjectDirectory,
            path,
            mainProjectPath,
            run,
            codeFix);
    }
}

internal sealed record RunOptions(
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
