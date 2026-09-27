# Source Generators

This is a collection of source generators I made while working at Flowqe.

The original repository can be found [here](https://dev.azure.com/flowqe-inc/Flowqe.Public/_git/Flowqe.SourceGenerators)

It's licensed under MIT.

## Project SDK

`Anton.SourceGeneration.Sdk` is an MSBuild Project SDK for C# source generators,
analyzers, code fixes, their tests, and an analyzer package. It is separate from
the `Anton.SourceGeneration` helper library. After the SDK is published to NuGet,
use a versioned SDK declaration in each project:

```xml
<Project Sdk="Anton.SourceGeneration.Sdk/1.0.0" />
```

Set `PackageId` and `Version` in the `.Package` project when its published
package name differs from the project name. Analyzer projects that declare new
diagnostics should include `AnalyzerReleases.Unshipped.md` as required by the
Roslyn analyzer rules enabled by this SDK.

The project file name selects the role. For a product named `MyRules`, use this
layout:

```text
MyRules/
  Shared/MyRules.Shared.csproj
  SourceGenerator/MyRules.SourceGenerator.csproj
  Analyzers/MyRules.Analyzers.csproj
  CodeFixes/MyRules.CodeFixes.csproj
  Package/MyRules.Package.csproj
  SourceGeneratorTests/MyRules.SourceGenerator.Tests.csproj
  AnalyzerTests/MyRules.Analyzers.Tests.csproj
  CodeFixTests/MyRules.CodeFixes.Tests.csproj
```

`Shared` uses the standard `Microsoft.NET.Sdk` and targets `netstandard2.0`.
Every project using `Anton.SourceGeneration.Sdk` references the sibling `Shared`
project. The SDK also adds the `Anton.SourceGeneration` and `Anton.Utils.Shared`
helper packages. Role-specific references are selected by the file name:

| Project suffix | Additional sibling reference | Result |
| --- | --- | --- |
| `.SourceGenerator` | None | Packs a source generator |
| `.Analyzers` | None | Builds an analyzer for the package project |
| `.CodeFixes` | `.Analyzers` | Builds a code fix for the package project |
| `.Package` | `.Analyzers`, `.CodeFixes` | Packs both into one NuGet package |
| `.SourceGenerator.Tests` | `.SourceGenerator` | Tests a generator |
| `.Analyzers.Tests` | `.Analyzers` | Tests an analyzer |
| `.CodeFixes.Tests` | `.Analyzers`, `.CodeFixes` | Tests a code fix |
| `.Tests` | None | Tests shared code or helpers |

Production projects target `netstandard2.0`; test projects target `net10.0`.
Set `TargetFramework` in a project or `Directory.Build.props` to override the
default. This repository overrides its existing tests to `net11.0` in
`source/Directory.Build.props`.

The SDK packs Roslyn assemblies under `analyzers/dotnet/cs`, including the
referenced shared and helper assemblies. A `.Package` project combines its
analyzer and code fix in one package. The shared assembly and
`Anton.Utils.Shared` also appear under `lib/netstandard2.0` for consumers.

Run `./build/test-sdk-consumer.sh` to pack the SDK and helpers into a temporary
local NuGet feed, then build and test an external project using
`<Project Sdk="Anton.SourceGeneration.Sdk/1.0.0">`. The script tests all project
roles and checks that packed analyzers and generators load in consumer builds.
The release workflow publishes packages to NuGet.org only on a push to `master`.

## Solutions

Open `SourceGenerators.slnx` to work with the complete repository, or use the
solution in an individual `source/SourceGenerators/<generator>` directory for a
smaller scope.

Regenerate all `.slnx` files with:

```shell
./build/generate-solutions.sh
```

## Notes

The polyfills and the msbuild configuration were mostly copied from [ComputeSharp](https://github.com/Sergio0694/ComputeSharp/tree/main) (thanks, Sergio!)


Run this command to refresh cached source generators:

```
dotnet build-server shutdown
```
