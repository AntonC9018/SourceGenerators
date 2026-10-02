# Allow consumers to manage SDK package references

Starting with SDK 1.1.0, `AntonSourceGenerationInjectPackages=false` disables all
SDK-injected package references while preserving project-name roles, sibling
project references, validation, and build and packing conventions. One switch
lets consumers own their dependency versions and re-add any needed subset
without a growing set of per-package settings; injection remains the default.
The package-derived diagnostic-marker using is also suppressed because it would
break compilation without the associated RoslynTesting package.
