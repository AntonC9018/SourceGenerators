using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace SourceGeneration.Testing.LocalNuGet;

internal sealed class DotNetCli
{
    private readonly Command _baseCommand;
    private readonly string _configuration;
    private readonly TimeSpan _timeout;

    public DotNetCli(string configuration, TimeSpan timeout)
    {
        _configuration = configuration;
        _timeout = timeout;
        _baseCommand = Cli.Wrap("dotnet")
            .WithEnvironmentVariables(environment => environment
                .Set("DOTNET_NOLOGO", "1")
                .Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
                .Set("MSBUILDDISABLENODEREUSE", "1"))
            .WithValidation(CommandResultValidation.None);
    }

    public DotNetTarget ForProject(
        string projectPath,
        string workingDirectory)
    {
        return CreateTarget(projectPath, workingDirectory, "--project");
    }

    public DotNetTarget ForFile(
        string filePath,
        string workingDirectory)
    {
        return CreateTarget(filePath, workingDirectory, "--file");
    }

    public async Task ExecuteCheckedAsync(
        DotNetCommandBuilder command,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new DotNetCommandFailedException(
                "dotnet command failed.",
                result);
        }
    }

    public async Task<DotNetCommandResult> ExecuteAsync(
        DotNetCommandBuilder builder,
        CancellationToken cancellationToken)
    {
        var invocation = builder.Build();
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            var result = await invocation.Command
                .ExecuteBufferedAsync(timeout.Token);

            return new DotNetCommandResult(
                invocation.Command.TargetFilePath,
                invocation.Arguments,
                invocation.Command.WorkingDirPath,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
        catch (OperationCanceledException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"dotnet command timed out after {_timeout}. "
                + $"Command: {invocation.Command.TargetFilePath} {invocation.Command.Arguments}. "
                + $"Working directory: {invocation.Command.WorkingDirPath}.",
                exception);
        }
    }

    private DotNetTarget CreateTarget(
        string targetPath,
        string workingDirectory,
        string runTargetOption)
    {
        return new DotNetTarget(
            _baseCommand.WithWorkingDirectory(workingDirectory),
            targetPath,
            _configuration,
            runTargetOption);
    }
}

internal sealed class DotNetTarget
{
    private readonly Command _baseCommand;
    private readonly string _targetPath;
    private readonly string _configuration;
    private readonly string _runTargetOption;

    public DotNetTarget(
        Command baseCommand,
        string targetPath,
        string configuration,
        string runTargetOption)
    {
        _baseCommand = baseCommand;
        _targetPath = targetPath;
        _configuration = configuration;
        _runTargetOption = runTargetOption;
    }

    public DotNetCommandBuilder Build()
    {
        return CreateCommand("build");
    }

    public DotNetCommandBuilder Pack()
    {
        return CreateCommand("pack");
    }

    public DotNetCommandBuilder Run()
    {
        return CreateCommand("run", _runTargetOption);
    }

    private DotNetCommandBuilder CreateCommand(
        string verb,
        string? targetOption = null)
    {
        var command = new DotNetCommandBuilder(_baseCommand)
            .Add(verb);

        if (targetOption is not null)
        {
            command.Add(targetOption);
        }

        return command
            .Add(_targetPath)
            .AddOption("--configuration", _configuration);
    }
}

internal sealed class DotNetCommandBuilder
{
    private readonly Command _baseCommand;
    private readonly List<string> _arguments = new();

    public DotNetCommandBuilder(Command baseCommand)
    {
        _baseCommand = baseCommand;
    }

    public DotNetCommandBuilder NoBuild()
    {
        return Add("--no-build");
    }

    public DotNetCommandBuilder NoCache()
    {
        return Add("--no-cache");
    }

    public DotNetCommandBuilder OutputTo(string path)
    {
        return AddOption("--output", path);
    }

    internal DotNetCommandBuilder Add(string argument)
    {
        _arguments.Add(argument);
        return this;
    }

    internal DotNetCommandBuilder AddOption(
        string option,
        string value)
    {
        return Add(option).Add(value);
    }

    internal DotNetInvocation Build()
    {
        var arguments = _arguments.ToArray();
        return new DotNetInvocation(
            _baseCommand.WithArguments(arguments),
            arguments);
    }
}

internal sealed record DotNetInvocation(
    Command Command,
    IReadOnlyList<string> Arguments);

internal sealed record DotNetCommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
