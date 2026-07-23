#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/.." && pwd)"

source_project_paths() {
    git -C "$repository_root" ls-files \
        --cached \
        --others \
        --exclude-standard \
        -z \
        -- 'source/**/*.csproj'
}

regenerate_solution() {
    local solution_path="$1"
    shift

    local solution_directory="${solution_path%/*}"
    local solution_file="${solution_path##*/}"
    local solution_name="${solution_file%.slnx}"

    if (( $# == 0 )); then
        echo "No projects found for $solution_file." >&2
        return 1
    fi

    dotnet new sln \
        --name "$solution_name" \
        --output "$solution_directory" \
        --format slnx \
        --force \
        --no-update-check >/dev/null

    dotnet sln "$solution_path" add \
        "$@" \
        --in-root \
        --include-references false >/dev/null

    echo "Regenerated ${solution_path#"$repository_root"/}"
}

regenerate_product_solution() {
    local product_name="$1"
    local relative_project
    local has_tests=false
    local -a product_projects=()

    while IFS= read -r -d '' relative_project; do
        if [[ "$relative_project" != "source/$product_name/"* ]]; then
            continue
        fi

        product_projects+=("$repository_root/$relative_project")

        if [[ "$relative_project" == *.Tests.csproj ]]; then
            has_tests=true
        fi
    done < <(source_project_paths)

    product_projects+=(
        "$repository_root/source/SourceGeneration/SourceGeneration.csproj"
        "$repository_root/source/Utils.Shared/Utils.Shared.csproj"
    )

    if [[ "$has_tests" == true ]]; then
        product_projects+=(
            "$repository_root/source/SourceGeneration.Testing/SourceGeneration.Testing.csproj"
        )
    fi

    regenerate_solution \
        "$repository_root/source/$product_name/$product_name.slnx" \
        "${product_projects[@]}"
}

main() {
    local product_name
    local relative_project
    local -a master_projects=()

    for product_name in \
        AutoConstructor \
        AutoImplementedProperties \
        EntityOwnership \
        PropertyCacheHelper; do
        regenerate_product_solution "$product_name"
    done

    while IFS= read -r -d '' relative_project; do
        if [[ "$relative_project" == source/PackageIntegration.Tests/Consumers/* ]]; then
            continue
        fi

        master_projects+=("$repository_root/$relative_project")
    done < <(source_project_paths)

    regenerate_solution \
        "$repository_root/SourceGenerators.slnx" \
        "${master_projects[@]}"
}

main "$@"
