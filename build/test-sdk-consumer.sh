#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/.." && pwd)"
test_root="$(mktemp -d)"
trap 'rm -rf -- "$test_root"' EXIT

feed="$test_root/feed"
consumer="$test_root/consumer"
mkdir -p "$feed" "$consumer"

dotnet pack "$repository_root/source/Utils.Shared/Utils.Shared.csproj" \
    --configuration Release --output "$feed"
dotnet pack "$repository_root/source/SourceGeneration/SourceGeneration.csproj" \
    --configuration Release --output "$feed"
dotnet pack "$repository_root/source/SourceGeneration.RoslynTesting/SourceGeneration.RoslynTesting.csproj" \
    --configuration Release --output "$feed"
dotnet pack "$repository_root/source/SourceGeneration.Sdk/SourceGeneration.Sdk.csproj" \
    --configuration Release --output "$feed"

cp -R "$repository_root/tests/SdkConsumer/." "$consumer/"
cat > "$consumer/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

# A fresh cache forces this consumer to restore the SDK and helpers from the feed.
export NUGET_PACKAGES="$test_root/packages"
cd "$consumer"

for project in \
    "$consumer/SourceGeneratorTests/Probe.SourceGenerator.Tests.csproj" \
    "$consumer/AnalyzerTests/Probe.Analyzers.Tests.csproj" \
    "$consumer/CodeFixTests/Probe.CodeFixes.Tests.csproj" \
    "$consumer/GeneralTests/Probe.Tests.csproj" \
    "$consumer/ExplicitPackages/Probe.Bare.Tests.csproj"; do
    dotnet test "$project" --configuration Release
done

dotnet pack "$consumer/SourceGenerator/Probe.SourceGenerator.csproj" \
    --configuration Release --output "$feed"
dotnet pack "$consumer/Package/Probe.Package.csproj" \
    --configuration Release --output "$feed"

python3 - "$feed" "$consumer" <<'PY'
import json
from pathlib import Path
from sys import argv
from zipfile import ZipFile

feed = Path(argv[1])
consumer = Path(argv[2])
assets = json.loads((consumer / "ExplicitPackages/obj/project.assets.json").read_text())
# Freeze the direct reference set for the opted-out .Tests role, including versions.
expected_references = {
    "Microsoft.NET.Test.Sdk": "[17.10.0, )",
    "xunit": "[2.9.0, )",
    "xunit.runner.visualstudio": "[2.8.1, )",
    "Microsoft.CodeAnalysis.CSharp": "[4.10.0, )",
}
frameworks = assets["project"]["frameworks"]
assert set(frameworks) == {"net10.0"}, frameworks
references = {name: item["version"] for name, item in frameworks["net10.0"]["dependencies"].items()}
assert references == expected_references, references
# Freeze transitives too; CSharp legitimately brings CodeAnalysis.Analyzers.
expected_packages = {
    "Microsoft.CodeAnalysis.Analyzers/3.3.4",
    "Microsoft.CodeAnalysis.CSharp/4.10.0",
    "Microsoft.CodeAnalysis.Common/4.10.0",
    "Microsoft.CodeCoverage/17.10.0",
    "Microsoft.NET.Test.Sdk/17.10.0",
    "Microsoft.TestPlatform.ObjectModel/17.10.0",
    "Microsoft.TestPlatform.TestHost/17.10.0",
    "Newtonsoft.Json/13.0.1",
    "xunit.abstractions/2.0.3",
    "xunit.analyzers/1.15.0",
    "xunit.assert/2.9.0",
    "xunit.core/2.9.0",
    "xunit.extensibility.core/2.9.0",
    "xunit.extensibility.execution/2.9.0",
    "xunit.runner.visualstudio/2.8.1",
    "xunit/2.9.0",
}
resolved_packages = {name for name, item in assets["libraries"].items() if item["type"] == "package"}
assert resolved_packages == expected_packages, {
    "unexpected": sorted(resolved_packages - expected_packages),
    "missing": sorted(expected_packages - resolved_packages),
}
packages = {name.split("/")[0] for name in resolved_packages}
forbidden = {
    "Anton.SourceGeneration", "Anton.Utils.Shared", "Anton.SourceGeneration.RoslynTesting",
    "PolySharp", "Microsoft.CodeAnalysis.CSharp.Workspaces", "coverlet.collector",
}
assert not packages & forbidden, packages & forbidden

expected = {
    "Anton.SourceGeneration.RoslynTesting.1.0.0.nupkg": {
        "lib/net10.0/Anton.SourceGeneration.RoslynTesting.dll",
    },
    "Anton.SourceGeneration.Sdk.1.1.0.nupkg": {
        "Sdk/Sdk.props",
        "Sdk/Sdk.targets",
    },
    "Probe.SourceGenerator.1.0.0.nupkg": {
        "analyzers/dotnet/cs/Probe.SourceGenerator.dll",
        "analyzers/dotnet/cs/Probe.Shared.dll",
        "analyzers/dotnet/cs/Anton.SourceGeneration.dll",
        "analyzers/dotnet/cs/Anton.Utils.Shared.dll",
    },
    "Probe.Rules.1.0.0.nupkg": {
        "analyzers/dotnet/cs/Probe.Analyzers.dll",
        "analyzers/dotnet/cs/Probe.CodeFixes.dll",
        "analyzers/dotnet/cs/Probe.Shared.dll",
        "analyzers/dotnet/cs/Anton.SourceGeneration.dll",
        "analyzers/dotnet/cs/Anton.Utils.Shared.dll",
    },
}

for package_name, required_files in expected.items():
    with ZipFile(feed / package_name) as package:
        missing = required_files.difference(package.namelist())
    if missing:
        raise SystemExit(f"{package_name} is missing: {', '.join(sorted(missing))}")
PY

dotnet build "$consumer/GeneratorConsumer/Probe.GeneratorConsumer.csproj" \
    --configuration Release

if analyzer_output="$(dotnet build \
    "$consumer/AnalyzerConsumer/Probe.AnalyzerConsumer.csproj" \
    --configuration Release 2>&1)"; then
    echo "The analyzer consumer built without the expected PROBE001 diagnostic." >&2
    exit 1
fi

if ! grep -q 'error PROBE001' <<< "$analyzer_output"; then
    echo "$analyzer_output" >&2
    exit 1
fi

echo "SDK package, all project roles, tests, and package consumers passed."
