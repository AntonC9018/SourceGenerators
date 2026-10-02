using System;
using System.Collections.Generic;

namespace SourceGeneration.Testing.LocalNuGet;

public sealed class PackageConsumptionTestBuilder
{
    private readonly string _name;
    private readonly List<PackageProject> _packages = new();
    private readonly List<ConsumerFixture> _consumers = new();

    public PackageConsumptionTestBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public PackageConsumptionTestBuilder AddPackage(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        _packages.Add(new PackageProject(projectPath));
        return this;
    }

    public PackageConsumptionTestBuilder AddSingleFileConsumer(
        string path,
        Action<ConsumerFixtureBuilder>? configure = null)
    {
        return AddConsumer(ConsumerFixtureKind.SingleFile, path, configure);
    }

    public PackageConsumptionTestBuilder AddProjectConsumer(
        string path,
        Action<ConsumerFixtureBuilder>? configure = null)
    {
        return AddConsumer(ConsumerFixtureKind.ProjectDirectory, path, configure);
    }

    public PackageConsumptionTest Build()
    {
        return new PackageConsumptionTest(
            _name,
            _packages.ToArray(),
            _consumers.ToArray());
    }

    private PackageConsumptionTestBuilder AddConsumer(
        ConsumerFixtureKind kind,
        string path,
        Action<ConsumerFixtureBuilder>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var builder = new ConsumerFixtureBuilder(kind, path);
        configure?.Invoke(builder);
        _consumers.Add(builder.Build());
        return this;
    }
}

public sealed class ConsumerFixtureBuilder
{
    private readonly ConsumerFixtureKind _kind;
    private readonly string _path;
    private string? _mainProjectPath;
    private RunOptions? _run;
    private CodeFixExpectation? _codeFix;

    internal ConsumerFixtureBuilder(ConsumerFixtureKind kind, string path)
    {
        _kind = kind;
        _path = path;
    }

    public ConsumerFixtureBuilder WithMainProject(string path)
    {
        RequireProjectDirectory();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _mainProjectPath = path;
        return this;
    }

    public ConsumerFixtureBuilder ExpectRun(
        int exitCode = 0,
        string? standardOutputContains = null,
        string? standardErrorContains = null)
    {
        _run = new RunOptions(
            exitCode,
            standardOutputContains,
            standardErrorContains);
        return this;
    }

    public ConsumerFixtureBuilder ExpectCodeFix(
        string diagnosticId,
        string sourcePath,
        string expectedSourcePath)
    {
        RequireProjectDirectory();
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourcePath);
        _codeFix = new CodeFixExpectation(
            diagnosticId,
            sourcePath,
            expectedSourcePath);
        return this;
    }

    internal ConsumerFixture Build()
    {
        return _kind switch
        {
            ConsumerFixtureKind.SingleFile => ConsumerFixture.SingleFile(_path, _run),
            ConsumerFixtureKind.ProjectDirectory => ConsumerFixture.ProjectDirectory(
                _path,
                _mainProjectPath,
                _run,
                _codeFix),
            _ => throw new ArgumentOutOfRangeException(nameof(_kind)),
        };
    }

    private void RequireProjectDirectory()
    {
        if (_kind != ConsumerFixtureKind.ProjectDirectory)
        {
            throw new InvalidOperationException(
                "This option requires a project-directory consumer.");
        }
    }
}
