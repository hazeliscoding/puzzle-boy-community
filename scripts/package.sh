#!/usr/bin/env bash
# Publishes a self-contained build for one runtime and packs it as the archive players download: unpack and run, with
# no .NET, Nocturne, Steam or network. content/, LICENSE, THIRD_PARTY_NOTICES.txt and known-atlus-layouts.txt come
# with the publish. Adds the archive's SHA-256 to <out-dir>/SHA256SUMS, replacing an older line for the same name.
# Usage: scripts/package.sh <win-x64|linux-x64|osx-arm64|osx-x64> <out-dir>
# Needs dotnet, GNU tar and python3 (for the zip, since Git Bash has no zip). Check the result with check-archive.sh.
set -euo pipefail
rid=${1:?usage: scripts/package.sh <rid> <out-dir>}
out=${2:?usage: scripts/package.sh <rid> <out-dir>}
repo=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

dotnet publish "$repo/src/PuzzleBoy.Community" --configuration Release --runtime "$rid" --self-contained --output "$work/app"

# Debug symbols and API docs are for developers; the game needs neither.
find "$work/app" -maxdepth 1 \( -name '*.pdb' -o -name '*.xml' \) -delete

case "$rid" in
    win-*)
        name=puzzle-boy-community-$rid.zip
        rm -f "$out/$name"
        python3 -I - "$work/app" "$out/$name" <<'EOF'
import os, sys, zipfile
root, target = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for folder, dirs, files in os.walk(root):
        dirs.sort()
        for file in sorted(files):
            path = os.path.join(folder, file)
            archive.write(path, os.path.relpath(path, root).replace(os.sep, "/"))
EOF
        ;;
    *)
        name=puzzle-boy-community-$rid.tar.gz
        exe=PuzzleBoyCommunity
        # Windows has no executable bit to carry over, so the modes are set here: the game is 755, the rest readable.
        owner=(--owner=0 --group=0 --numeric-owner)
        tar -cf "$work/app.tar" "${owner[@]}" --mode=u=rwX,go=rX --anchored --exclude="./$exe" -C "$work/app" .
        tar -rf "$work/app.tar" "${owner[@]}" --mode=755 -C "$work/app" "./$exe"
        gzip -9 -n < "$work/app.tar" > "$out/$name"
        ;;
esac

# Written as "<hash>  <name>", the form sha256sum -c and shasum -a 256 -c read on every system.
sum=$(sha256sum "$out/$name" | cut -c1-64)
sums=$out/SHA256SUMS
touch "$sums"
grep -v "  $name\$" "$sums" > "$sums.new" || true
printf '%s  %s\n' "$sum" "$name" >> "$sums.new"
mv "$sums.new" "$sums"
echo "$out/$name"
