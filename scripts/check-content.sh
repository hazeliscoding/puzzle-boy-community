#!/usr/bin/env bash
# Fails when a tracked file holds Atlus data or decompiler output (see AGENTS.md, Content rules).
# Usage: scripts/check-content.sh [repo-dir]
set -euo pipefail
cd "${1:-.}"
found=0

report() {
    echo "$1"
    found=1
}

# Asset formats, recognized by their first bytes rather than by name.
while IFS= read -r -d '' file; do
    # A submodule is listed as its directory; its own repo runs this check on its files.
    [ -f "$file" ] || continue
    magic=$(head -c 12 "$file" | od -An -tx1 | tr -d ' \n')
    case "$magic" in
        556e6974794653*) report "$file: Unity asset bundle" ;;
        46534235*) report "$file: FMOD sound bank" ;;
        af1bb1fa*) report "$file: IL2CPP metadata" ;;
        ????????????????464c5730*) report "$file: Atlus flow script (FLW0)" ;;
    esac
done < <(git ls-files -z)

# Stage files: only handmade test stages may live in the repo.
while IFS= read -r file; do
    report "$file: stage file outside tests/"
done < <(git ls-files -- '*.map' ':!tests/**')

# Decompiler output: Ghidra's generated names and Il2CppDumper's address comments.
while IFS= read -r match; do
    report "$match: looks like decompiler output"
done < <(git grep -n -I -E '\b(FUN|DAT|LAB)_[0-9a-fA-F]{6,}\b|\bundefined[1248]\b|RVA: 0x[0-9A-Fa-f]+' || true)

if [ "$found" -ne 0 ]; then
    echo "Atlus data and decompiler output stay out of the repo. Community Edition ships only content with a provenance entry."
    exit 1
fi
