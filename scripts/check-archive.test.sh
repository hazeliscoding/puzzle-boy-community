#!/usr/bin/env bash
# Positive controls for check-archive.sh: a small archive shaped like a release must pass, and each planted fault must
# fail it.
set -euo pipefail
check="$(cd "$(dirname "$0")" && pwd)/check-archive.sh"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
failures=0

# Writes a release-shaped archive (.zip or .tar.gz, by the name) with one fault planted, or none for "good".
make() {
    python3 -I - "$1" "$2" <<'EOF'
import io, json, sys, tarfile, zipfile

path, fault = sys.argv[1], sys.argv[2]
win = path.endswith(".zip")
exe = "PuzzleBoyCommunity.exe" if win else "PuzzleBoyCommunity"
manifest = {"theme": "themes/default/theme.json", "packs": ["stages/demo"],
            "fonts": {"body": "fonts/Body-Regular.ttf", "title": "fonts/Title-Regular.ttf"}}
files = {
    exe: b"MZ" if win else b"\x7fELF",
    "LICENSE": b"Apache License", "THIRD_PARTY_NOTICES.txt": b"notices", "known-atlus-layouts.txt": b"",
    "content/manifest.json": json.dumps(manifest).encode(), "content/provenance.json": b"{}",
    "content/ASSET_CREDITS.md": b"# Credits", "content/themes/default/theme.json": b"{}",
    "content/fonts/Body-Regular.ttf": b"\0\1\0\0", "content/fonts/Body-OFL.txt": b"OFL",
    "content/fonts/Title-Regular.ttf": b"\0\1\0\0", "content/fonts/Title-OFL.txt": b"OFL",
    "content/stages/demo/pack.json": json.dumps({"stages": [{"id": "a", "file": "a.json"}]}).encode(),
    "content/stages/demo/a.json": b"{}",
}
if fault == "missing font":
    del files["content/fonts/Title-Regular.ttf"]
if fault == "missing OFL text":
    del files["content/fonts/Body-OFL.txt"]
if fault == "missing stage":
    del files["content/stages/demo/a.json"]
if fault == "missing notices":
    del files["THIRD_PARTY_NOTICES.txt"]
if fault == "planted .map":
    files["content/stages/demo/pbMap00.map"] = b"\1\0\0\0"
if fault == "Unity bundle":
    files["content/themes/default/sky.png"] = b"UnityFS\0\0\0\0\6"
if fault == "FLW0 script":
    files["pbmsg.bf"] = b"\0\0\0\0\xfe\3\0\0FLW0"
if fault == "classic pack manifest":
    files["content/classic-pack.json"] = json.dumps({"format": "puzzle-boy-classic-pack", "schemaVersion": 1, "origin": "local-import"}).encode()
if fault == "classic pack bundle":
    files["classic/smt3hd_Data/StreamingAssets/PC/common"] = b"UnityFS\0\0\0\0\6"
if fault == "classic pack layout":
    files["classic/smt3hd_Data/StreamingAssets/PC/notes.txt"] = b"x"
if fault == "parent path":
    files["../evil.txt"] = b"x"
if fault == "absolute path":
    files["/tmp/evil.txt"] = b"x"

if win:
    with zipfile.ZipFile(path, "w") as archive:
        for name, data in files.items():
            archive.writestr(name, data)
else:
    with tarfile.open(path, "w:gz") as archive:
        for name, data in files.items():
            info = tarfile.TarInfo("./" + name if not name.startswith(("/", "..")) else name)
            info.size, info.mode = len(data), 0o755 if name == exe and fault != "no executable bit" else 0o644
            archive.addfile(info, io.BytesIO(data))
        if fault == "link":
            info = tarfile.TarInfo("./content/stages/demo/b.json")
            info.type, info.linkname = tarfile.SYMTYPE, "../../../../etc/passwd"
            archive.addfile(info)
EOF
}

expect() {
    local outcome=$1 fault=$2 kind=$3
    local archive="$scratch/puzzle-boy-community-test.$kind"
    rm -f "$archive"
    make "$archive" "$fault"
    if "$check" "$archive" > /dev/null; then actual=pass; else actual=fail; fi
    if [ "$actual" = "$outcome" ]; then
        echo "ok: $fault ($kind)"
    else
        echo "WRONG: $fault ($kind) should $outcome but did $actual"
        failures=1
    fi
}

expect pass good zip
expect pass good tar.gz
expect fail "missing font" zip
expect fail "missing font" tar.gz
expect fail "missing OFL text" tar.gz
expect fail "missing stage" zip
expect fail "missing notices" tar.gz
expect fail "planted .map" zip
expect fail "planted .map" tar.gz
expect fail "Unity bundle" tar.gz
expect fail "FLW0 script" zip
expect fail "classic pack manifest" zip
expect fail "classic pack manifest" tar.gz
expect fail "classic pack bundle" zip
expect fail "classic pack bundle" tar.gz
expect fail "classic pack layout" tar.gz
expect fail "parent path" zip
expect fail "parent path" tar.gz
expect fail "absolute path" tar.gz
expect fail link tar.gz
expect fail "no executable bit" tar.gz

exit "$failures"
