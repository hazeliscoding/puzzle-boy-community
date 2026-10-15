#!/usr/bin/env bash
# Positive controls for check-content.sh: every planted violation must fail it, and a clean repo must pass.
set -euo pipefail
check="$(cd "$(dirname "$0")" && pwd)/check-content.sh"
failures=0

# Plants one file in a fresh repo and reports whether the check rejected it.
expect() {
    local outcome=$1 name=$2 path=$3 content=$4
    local repo
    repo=$(mktemp -d)
    git -C "$repo" init -q
    mkdir -p "$repo/$(dirname "$path")"
    printf '%b' "$content" > "$repo/$path"
    git -C "$repo" add -A
    # Every scratch repo also holds a submodule, as the real one does; the check must skip it, not stop on it.
    mkdir -p "$repo/external/engine"
    git -C "$repo" update-index --add --cacheinfo "160000,$(printf '%040d' 1),external/engine"
    if "$check" "$repo" > /dev/null; then actual=pass; else actual=fail; fi
    rm -rf "$repo"
    if [ "$actual" = "$outcome" ]; then
        echo "ok: $name"
    else
        echo "WRONG: $name should $outcome but did $actual"
        failures=1
    fi
}

expect fail "Unity bundle" assets/pazzleboy 'UnityFS\0\0\0\0\x06'
expect fail "FMOD sound bank" audio/se.fsb 'FSB5\x01\0\0\0'
expect fail "IL2CPP metadata" data/global-metadata.dat '\xaf\x1b\xb1\xfa\x18\0\0\0'
expect fail "Atlus flow script" data/pbmsg.bf '\0\0\0\0\xfe\x03\0\0FLW0'
expect fail "stage file outside tests" data/pbMap00.map '\x01\0\0\0'
expect fail "classic pack manifest" content/classic-pack.json '{"format": "puzzle-boy-classic-pack", "origin": "local-import"}'
expect fail "classic pack manifest anywhere" exports/Classic-Pack.json '{}'
expect fail "classic pack layout" classic/smt3hd_Data/StreamingAssets/PC/notes.txt 'x'
# Escaped (\x5f is an underscore, \x38 is 8, \x3a is a colon) so this file doesn't trip the check on the real repo.
expect fail "Ghidra function name" src/Rules.cs 'void FUN\x5f180a3b2c0(void);'
expect fail "Ghidra type name" src/Rules.cs 'undefined\x38 local_10;'
expect fail "Il2CppDumper address comment" notes.txt '// RVA\x3a 0x24D2360 Offset\x3a 0x24D1760'
expect fail "library in content" content/stages/helper.dll 'MZ'
expect fail "asset bundle in content" content/themes/pazzleboy.assets 'x'
expect fail "upper-case type in content" content/run.SH 'echo'
expect pass "stage in content" content/stages/first-steps/first-push.json '{}'
expect pass "font in content" content/fonts/Silkscreen-Regular.TTF '\0\x01\0\0'
expect pass "handmade test stage" tests/Fixtures/handmade.map '\x01\0\0\0'
expect pass "ordinary code" src/Stage.cs 'public sealed class Stage { }'

exit "$failures"
