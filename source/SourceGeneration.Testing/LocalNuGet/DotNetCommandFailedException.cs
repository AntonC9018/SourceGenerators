using System;
using System.Collections.Generic;
using System.Text;
using CliWrap.Builders;

namespace SourceGeneration.Testing.LocalNuGet;

public sealed class DotNetCommandFailedException : Exception
{
    internal DotNetCommandFailedException(
        string message,
        DotNetCommandResult result)
        : base(CreateMessage(message, result))
    {
        FileName = result.FileName;
        Arguments = result.Arguments;
        WorkingDirectory = result.WorkingDirectory;
        ExitCode = result.ExitCode;
        StandardOutput = result.StandardOutput;
        StandardError = result.StandardError;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string WorkingDirectory { get; }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    private static string CreateMessage(
        string message,
        DotNetCommandResult result)
    {
        var builder = new StringBuilder()
            .AppendLine(message)
            .Append("Command: ")
            .Append(result.FileName);

        foreach (var argument in result.Arguments)
        {
            builder
                .Append(' ')
                .Append(ArgumentsBuilder.Escape(argument));
        }

        return builder
            .AppendLine()
            .AppendLine($"Working directory: {result.WorkingDirectory}")
            .AppendLine($"Exit code: {result.ExitCode}")
            .AppendLine("Standard output:")
            .AppendLine(result.StandardOutput)
            .AppendLine("Standard error:")
            .AppendLine(result.StandardError)
            .ToString();
    }
}
