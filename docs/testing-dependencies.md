# Shared testing dependencies

SDK 1.1.1 and `Anton.SourceGeneration.RoslynTesting` 1.0.1 address
[SourceGenerators #12](https://github.com/AntonC9018/SourceGenerators/issues/12).
The repair is a dependency requirement at the shared source; consumers do not
need `NoWarn`, `WarningsNotAsErrors`, `NuGetAuditSuppress`, or disabled auditing.

## Compatible versions

The tested combination keeps the existing compiler and testing APIs:

- `Anton.SourceGeneration.Sdk` 1.1.1, with `Anton.SourceGeneration.RoslynTesting`
  1.0.1 for analyzer and code-fix tests.
- `Microsoft.CodeAnalysis.CSharp` and `.CSharp.Workspaces` 4.10.0.
- Roslyn Testing packages (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`,
  `.CodeFix.Testing`, and `.SourceGenerators.Testing`) 1.1.4.
- The existing transitive NuGet packages, including `NuGet.Packaging` 7.0.3.
- `System.Security.Cryptography.Pkcs` **9.0.21 or later compatible version**.
- `Microsoft.NET.Test.Sdk` 17.10.0, xUnit 2.9.0, Visual Studio runner 2.8.1,
  and coverlet.collector 6.0.2; test projects target `net10.0` by default.

Roslyn Testing 1.1.4 brings `NuGet.Packaging` 7.0.3, which otherwise selects
PKCS 9.0.6. The two reported advisories,
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) and
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx), identify
9.0.21 as the first patched 9.x version (10.x requires 10.0.13).
Requiring the patched 9.x package preserves the existing dependency major
version and avoids an unrelated Roslyn or NuGet API migration. A newer
NuGet.Packaging alone is insufficient: its 7.9.0 manifest still requests the
affected PKCS 8.0.1. These package manifests and advisory ranges were checked
on 2026-10-15.

The SDK adds PKCS to all three Roslyn test roles, including generator tests
which do not use RoslynTesting. The helper also declares it as a public NuGet
dependency so ordinary `Microsoft.NET.Sdk` consumers receive the repair.
Production analyzer/code-fix/generator roles and ordinary `.Tests` projects
do not gain this test-only dependency.

## Adoption

After publication, change versioned project SDK declarations to:

```xml
<Project Sdk="Anton.SourceGeneration.Sdk/1.1.1" />
```

For direct helper consumers, upgrade the package reference to
`Anton.SourceGeneration.RoslynTesting` 1.0.1. Analyzer/code-fix test consumers
temporarily staying on SDK 1.1.0 can set `AntonRoslynTestingVersion` to `1.0.1`
in their project or `Directory.Build.props`, or pass
`-p:AntonRoslynTestingVersion=1.0.1` while validating a local feed. This selects
the repaired helper dependency without changing audit policy. SDK 1.1.0
generator-test projects should upgrade the SDK because they do not use that
helper. Remove any older helper-version override when upgrading the SDK.

Before publication, pack this checkout into a local feed and use an isolated
package cache so an already cached version cannot mask the change. The
CodingRules `build/prepare-local-sdk.sh` script does this for its local helper
cache. Package publication is performed separately by the release workflow.

The existing `AntonSourceGenerationInjectPackages=false` switch is the separate
optional-reference feature from
[SourceGenerators #9](https://github.com/AntonC9018/SourceGenerators/issues/9).
Its behavior is unchanged: opted-out consumers own their dependency graph.
Turning off package injection is not the adoption path for this repair.

## Regression checks

Run from the SourceGenerators repository root:

```sh
./build/test-sdk-consumer.sh
```

This packs SDK 1.1.1 and helper 1.0.1 into a temporary feed, copies fixtures
outside the repository, and uses an empty NuGet package cache. It separately
restores, builds, and tests the SDK roles and an ordinary `Microsoft.NET.Sdk`
project using only the helper package for Roslyn testing dependencies. The
latter reuses the analyzer and code-fix behavioral tests. The script checks
that every Roslyn testing graph resolves PKCS 9.0.21, auditing covers all
dependencies at low severity and above, and warnings remain errors. It also
retains the explicit-package opt-out and packaged analyzer/generator checks.

The prior CodingRules baseline was:

```sh
dotnet test CodingRules.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903
```

Its 885 passing tests were behavior evidence with a command-scoped advisory
exception, not normal restore success. The validation below distinguishes
the repaired graph from that baseline.

## Validation on 2026-10-15

Before the repair, both `dotnet restore CodingRules.slnx` in CodingRules and
`dotnet restore source/SourceGeneration.RoslynTesting/SourceGeneration.RoslynTesting.csproj`
in SourceGenerators failed with NU1902/NU1903 for PKCS 9.0.6. The fresh SDK
consumer script also failed while packing that helper. No exception was used
to obtain these failures or the successful runs below.

In SourceGenerators (SDK 11.0.100-preview.4.26230.115):

```sh
dotnet restore source/SourceGeneration.RoslynTesting/SourceGeneration.RoslynTesting.csproj
./build/test-sdk-consumer.sh
dotnet restore SourceGenerators.slnx
dotnet build SourceGenerators.slnx -c Release --no-restore
dotnet test SourceGenerators.slnx -c Release --no-build --no-restore
```

The fresh SDK harness passed all 15 tests: generator (1), analyzer (5), code
fix (1), direct helper (6), general (1), and explicit packages (1). All builds
had zero warnings and errors. The package-content assertions and generator
consumer build passed; the analyzer consumer failed with the intended
`PROBE001` diagnostic. Dependency and audit-policy assertions passed.
The full SourceGenerators solution also restored and built with zero warnings
and errors, and all 15 tests passed across its five test assemblies.

In the unchanged CodingRules checkout (SDK 10.0.112), first prepare the local
feed, then select helper 1.0.1 for its existing SDK 1.1.0 projects:

```sh
./build/prepare-local-sdk.sh /home/anton/temp/SourceGenerators
dotnet restore CodingRules.slnx -p:AntonRoslynTestingVersion=1.0.1
dotnet build CodingRules.slnx -c Release --no-restore -p:AntonRoslynTestingVersion=1.0.1
dotnet test CodingRules.slnx -c Release --no-build --no-restore -p:AntonRoslynTestingVersion=1.0.1
```

For validation of SDK 1.1.1 defaults, a separate temporary consumer was made
from `git archive HEAD` of CodingRules at
`55545a84dd48e5ff5cd9100765f58034bebb8e77`, copying its prepared `artifacts/local-feed`
and changing only the copied project SDK declarations from 1.1.0 to 1.1.1.
Its `artifacts/nuget-packages` cache started empty. In that directory:

```sh
dotnet restore CodingRules.slnx
dotnet build CodingRules.slnx -c Release --no-restore
dotnet test CodingRules.slnx -c Release --no-build --no-restore
```

Both CodingRules variants restored successfully, built with zero warnings
and errors, and passed all 885 tests (41 analyzer and 844 code-fix tests,
including all six packaged code-fix consumption checks). Their analyzer and
code-fix test assets confirmed helper 1.0.1,
PKCS 9.0.21, `enableAudit=true`, `auditLevel=low`, `auditMode=all`, and
`allWarningsAsErrors=true`, without `warnNotAsError`. The version property in
the first variant selects a dependency; it is not an advisory exception. The
second variant uses normal restore with no command-line property at all.
No tracked file in the original CodingRules checkout was changed.
