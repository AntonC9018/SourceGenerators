#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/.." && pwd)"

source_project_paths() {
    local relative_project

    while IFS= read -r -d '' relative_project; do
        if [[ ! -f "$repository_root/$relative_project" ]]; then
            continue
        fi

        if [[ "$relative_project" == source/SourceGenerators/*/Tests/Consumers/* ]]; then
            continue
        fi

        printf '%s\0' "$relative_project"
    done < <(
        git -C "$repository_root" ls-files \
            --cached \
            --others \
            --exclude-standard \
            -z \
            -- 'source/**/*.csproj'
    )
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
        if [[ "$relative_project" != "source/SourceGenerators/$product_name/"* ]]; then
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
            "$repository_root/source/SourceGeneration.PackageTesting/SourceGeneration.PackageTesting.csproj"
            "$repository_root/source/SourceGeneration.Testing/SourceGeneration.Testing.csproj"
        )
    fi

    regenerate_solution \
        "$repository_root/source/SourceGenerators/$product_name/$product_name.slnx" \
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
        master_projects+=("$repository_root/$relative_project")
    done < <(source_project_paths)

    regenerate_solution \
        "$repository_root/SourceGenerators.slnx" \
        "${master_projects[@]}"
}

main "$@"
