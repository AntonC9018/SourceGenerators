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
<Project Sdk="Anton.SourceGeneration.Sdk/1.1.0" />
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

The `.CodeFixes` role also references `System.Composition.AttributedModel` for
MEF export attributes, alongside Roslyn Workspaces.
The `.Analyzers.Tests` and `.CodeFixes.Tests` roles reference
`Anton.SourceGeneration.RoslynTesting` and add a static using for
`InterpolateDiagnostic`. See the
[Roslyn testing README](source/SourceGeneration.RoslynTesting/README.md) for
`TestCode` and the test builders.

SDK source generator, analyzer, and code-fix projects target `netstandard2.0`;
test projects target `net10.0`.
Set `TargetFramework` in a project or `Directory.Build.props` to override the
default. This repository overrides its existing tests to `net11.0` in
`source/Directory.Build.props`.

The SDK packs Roslyn assemblies under `analyzers/dotnet/cs`, including the
referenced shared and helper assemblies. A `.Package` project combines its
analyzer and code fix in one package. The shared assembly and
`Anton.Utils.Shared` also appear under `lib/netstandard2.0` for consumers.

Starting with SDK 1.1.0, set `AntonSourceGenerationInjectPackages` to `false`
to manage all package references yourself:

```xml
<Project Sdk="Anton.SourceGeneration.Sdk/1.1.0">
  <PropertyGroup>
    <AntonSourceGenerationInjectPackages>false</AntonSourceGenerationInjectPackages>
  </PropertyGroup>
  <ItemGroup>
    <!-- Add the PackageReference items required by your project here. -->
  </ItemGroup>
</Project>
```

Set the property in the project body, `Directory.Build.props`, or on the command
line (`-p:AntonSourceGenerationInjectPackages=false`). `Directory.Build.targets`
is imported after the package items are evaluated and is too late. Unset values
and values other than `false` preserve injection; MSBuild compares this value
without regard to case, so `False` also disables it.

The switch suppresses every SDK-injected package, including
`Anton.SourceGeneration`, and the package-derived static using for diagnostic
markers. Project-name role selection, target-framework defaults, sibling project
references, validation, and packing conventions still apply. Consumers supply
and maintain compatible versions of the following packages (test-role additions
apply on top of the `.Tests` row):

| Project suffix | SDK-injected packages |
| --- | --- |
| `.SourceGenerator`, `.Analyzers`, `.CodeFixes`, `.Package` | PolySharp; Microsoft.CodeAnalysis.Analyzers; Microsoft.CodeAnalysis.CSharp; Anton.SourceGeneration; Anton.Utils.Shared |
| `.CodeFixes` (additional) | Microsoft.CodeAnalysis.CSharp.Workspaces; System.Composition.AttributedModel |
| `.Tests` | Anton.SourceGeneration; Anton.Utils.Shared; Microsoft.NET.Test.Sdk; xunit; xunit.runner.visualstudio; coverlet.collector; Microsoft.CodeAnalysis.CSharp; Microsoft.CodeAnalysis.CSharp.Workspaces |
| `.SourceGenerator.Tests` (additional) | Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing |
| `.Analyzers.Tests` (additional) | Microsoft.CodeAnalysis.CSharp.Analyzer.Testing; Anton.SourceGeneration.RoslynTesting |
| `.CodeFixes.Tests` (additional) | Microsoft.CodeAnalysis.CSharp.CodeFix.Testing; Anton.SourceGeneration.RoslynTesting |

The SDK's `Anton*Version` properties no longer select versions for opted-out
packages. Keep Roslyn versions compatible across your projects and supply
polyfills if your `netstandard2.0` code needs them without PolySharp. `.Package`
projects still set `SuppressDependenciesWhenPacking=true` by convention. Opted-out
`.SourceGenerator` projects own their `PackageReference` metadata, such as
`PrivateAssets="all"` for Roslyn and build dependencies. Supply required helper
assemblies through your own references or package items.

Run `./build/test-sdk-consumer.sh` to pack the SDK and helpers into a temporary
local NuGet feed, then build and test an external project using
`<Project Sdk="Anton.SourceGeneration.Sdk/1.1.0">`. The script tests all project
roles and checks that packed analyzers and generators load in consumer builds.
The release workflow publishes packages to NuGet.org only on a push to `master`.

## Package tests

`Anton.SourceGeneration.PackageTesting` provides `LocalNuGetPackageTester` for
tests that pack a project, restore it in a fresh consumer, and check the result.
It targets `net10.0`, so the .NET 11 tests in this repository can use it too.
The builder can configure a project consumer to verify a diagnostic, apply the
packaged code fix, compare the fixed source, and build again. See the
[package-testing README](source/SourceGeneration.PackageTesting/README.md) for
the API and path rules. Projects that only use the SDK's Roslyn unit-test
dependencies do not need this package.

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
