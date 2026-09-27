# Anton.SourceGeneration.PackageTesting

This test library checks a NuGet package in a temporary consumer project.
`LocalNuGetPackageTester` packs the projects added through
`PackageConsumptionTestBuilder`, creates a local feed and fresh NuGet cache,
copies each consumer fixture, and
replaces fixture package versions with the versions it just packed.

For an analyzer with a code fix, configure a project consumer:

```csharp
var tester = await LocalNuGetPackageTester.CreateAsync();
const string diagnosticId = "RULE001";
var test = new PackageConsumptionTestBuilder(nameof(PackagedCodeFixWorks))
    .AddPackage("Package/MyRules.Package.csproj")
    .AddProjectConsumer(
        "Tests/Consumers/ReturnDecision",
        consumer => consumer.ExpectCodeFix(
            diagnosticId: diagnosticId,
            sourcePath: "Consumer.cs",
            expectedSourcePath: "Tests/Expected/ReturnDecision.Fixed.cs.txt"))
    .Build();

await tester.AssertAsync(test);
```

The consumer source path is relative to the copied consumer directory, and the
expected source path is relative to the repository root. The helper checks that
the diagnostic makes the first build fail, runs `dotnet format analyzers` for
that diagnostic, compares the fixed source, and then checks that the consumer
builds. It uses the repository's `global.json` in the temporary consumer when
one exists. `AddSingleFileConsumer` and `AddProjectConsumer` can also configure
expected exit codes and output through `ExpectRun`.

The library targets `net10.0`. It contains the package-test setup only; the
generator snapshot helpers remain in the `SourceGeneration.Testing` project.
