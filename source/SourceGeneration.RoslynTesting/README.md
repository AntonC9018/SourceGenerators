# Anton.SourceGeneration.RoslynTesting

This library provides typed diagnostic markers and builders for Roslyn analyzer
and code fix tests. The SourceGeneration SDK references it automatically from
projects ending in `.Analyzers.Tests` or `.CodeFixes.Tests`, and supplies a
static using for `InterpolateDiagnostic`.

Version 1.0.1 requires `System.Security.Cryptography.Pkcs` 9.0.21 transitively
to repair the dependency inherited from Roslyn Testing 1.1.4. Direct package
consumers receive this dependency too; no advisory suppression is required.
See [compatible versions and adoption](https://github.com/AntonC9018/SourceGenerators/blob/master/docs/testing-dependencies.md).

```csharp
var source = TestCode.Create($$"""
    class C
    {
        bool Check() => true;
        bool M() => {{InterpolateDiagnostic("Check()", MyAnalyzer.Rule)}};
    }
    """);

await AnalyzerTestBuilder
    .For<MyAnalyzer, DefaultVerifier>()
    .WithSource(source)
    .RunAsync();
```

The `DiagnosticDescriptor` overload checks the diagnostic ID and marked span.
Use a locationless `DiagnosticExpectation` to check severity as well:

```csharp
InterpolateDiagnostic(
    "Check()",
    new DiagnosticExpectation("RULE001", DiagnosticSeverity.Warning))
```

`TestCode.Create(string)` handles source without diagnostic markers. Code fix
tests use `CodeFixTestBuilder.For<TAnalyzer, TCodeFix, TVerifier>()` and
`.WithFixedCode(string)`. Both builders provide `.ExpectDiagnostic(DiagnosticResult)`
as a direct pass-through for separate Roslyn expectations. It does not modify a
diagnostic already declared by an interpolation marker.
