using System;
using Microsoft.CodeAnalysis;

namespace SourceGeneration.Testing;

/// <summary>
/// Identifies a diagnostic and its severity without assigning a source location.
/// </summary>
public sealed class DiagnosticExpectation
{
    public DiagnosticExpectation(string id, DiagnosticSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Severity = severity;
    }

    public string Id { get; }

    public DiagnosticSeverity Severity { get; }
}
