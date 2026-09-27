# Anton.SourceGeneration.PackageTesting

This test library checks a NuGet package in a temporary consumer project.
`LocalNuGetPackageTester` packs the projects listed in `PackageConsumptionTest`,
creates a local feed and fresh NuGet cache, copies each consumer fixture, and
replaces fixture package versions with the versions it just packed.

For an analyzer with a code fix, add a `CodeFixExpectation` to a project
fixture:

```csharp
var tester = await LocalNuGetPackageTester.CreateAsync();
var package = new PackageProject("Package/MyRules.Package.csproj");
var codeFix = new CodeFixExpectation(
    DiagnosticId: "RULE001",
    SourcePath: "Consumer.cs",
    ExpectedSourcePath: "Tests/Expected/Consumer.cs.txt");
var consumer = ConsumerFixture.ProjectDirectory(
    path: "Tests/Consumers/Rule001",
    mainProjectPath: "Consumer.csproj",
    codeFix: codeFix);
var test = new PackageConsumptionTest(
    nameof(PackagedCodeFixWorks),
    new[] { package },
    new[] { consumer });

await tester.AssertAsync(test);
```

`SourcePath` is relative to the copied consumer directory.
`ExpectedSourcePath` is relative to the repository root. The helper checks that
the diagnostic makes the first build fail, runs `dotnet format analyzers` for
that diagnostic, compares the fixed source, and then checks that the consumer
builds. It uses the repository's `global.json` in the temporary consumer when
one exists.
Pass `ExpectedEntries` to `PackageProject` to require specific paths inside
the packed `.nupkg` before testing its consumers.

The library targets `net10.0`. It contains the package-test setup only; the
generator snapshot helpers remain in the `SourceGeneration.Testing` project.
