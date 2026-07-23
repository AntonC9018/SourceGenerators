# Source Generators

This is a collection of source generators I made while working at Flowqe.

The original repository can be found [here](https://dev.azure.com/flowqe-inc/Flowqe.Public/_git/Flowqe.SourceGenerators)

It's licensed under MIT.

## Solutions

Open `SourceGenerators.slnx` to work with the complete repository, or use the
solution in an individual generator's directory for a smaller scope.

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
