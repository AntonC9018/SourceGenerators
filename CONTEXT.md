# SourceGenerators

This repository provides build conventions and reusable code for projects that extend the C# compiler.

## Language

**SourceGeneration SDK**:
`Anton.SourceGeneration.Sdk`, the project SDK for external source generator, analyzer, and code fix projects.
_Avoid_: SourceGeneration library, project template

**SourceGeneration library**:
`Anton.SourceGeneration`, the reusable Roslyn helper library for code in those projects.
_Avoid_: SDK
