#!/usr/bin/env bash
# Fails unless a release archive holds a game that starts on its own and nothing it mustn't: the executable (with its
# executable bit in a tarball), the licenses, content/ with its provenance, credits, theme and every font, OFL text,
# pack and stage its manifest lists; no Atlus format (by name or first bytes, as check-content.sh), no link and no
# path outside the archive's root. It reads the archive without unpacking it, so a bad path can't land anywhere.
# Usage: scripts/check-archive.sh <puzzle-boy-community-<rid>.zip | .tar.gz>
set -euo pipefail
archive=${1:?usage: scripts/check-archive.sh <archive>}
python3 -I - "$archive" <<'EOF'
import json, posixpath, stat, sys, tarfile, zipfile

archive = sys.argv[1]
problems = []
files = {}  # name -> (mode or None, member) for regular files


def bad_path(name):
    parts = name.split("/")
    return name.startswith("/") or "\\" in name or ":" in parts[0] or ".." in parts


if archive.endswith(".zip"):
    exe = "PuzzleBoyCommunity.exe"
    source = zipfile.ZipFile(archive)
    for info in source.infolist():
        name = info.filename
        if bad_path(name):
            problems.append(f"{name}: escapes the archive's root")
        elif info.create_system == 3 and stat.S_ISLNK(info.external_attr >> 16):
            problems.append(f"{name}: is a link")
        elif not info.is_dir():
            files[name] = (None, info)
    read = lambda member, size=-1: source.open(member).read(size)
elif archive.endswith(".tar.gz"):
    exe = "PuzzleBoyCommunity"
    source = tarfile.open(archive, "r:gz")
    for member in source.getmembers():
        name = member.name[2:] if member.name.startswith("./") else member.name
        if bad_path(name):
            problems.append(f"{member.name}: escapes the archive's root")
        elif member.issym() or member.islnk():
            problems.append(f"{member.name}: is a link")
        elif member.isfile():
            files[name] = (member.mode, member)
        elif not member.isdir():
            problems.append(f"{member.name}: isn't a file or a folder")
    read = lambda member, size=-1: source.extractfile(member).read(size)
else:
    sys.exit(f"{archive}: not a .zip or .tar.gz")

# The same formats check-content.sh keeps out of the repo, and the player's own classic pack.
for name, (mode, member) in files.items():
    if name.lower().endswith(".map"):
        problems.append(f"{name}: stage file")
    # A classic pack beside the executable (classic-pack/) is how a personal build carries the owner's own; never a release.
    if posixpath.basename(name).lower() == "classic-pack.json" or "smt3hd_data/" in name.lower() or "classic-pack" in name.lower().split("/")[:-1]:
        problems.append(f"{name}: part of a classic pack, which is the player's own and never ships")
    head = read(member, 12)
    if head.startswith(b"UnityFS"):
        problems.append(f"{name}: Unity asset bundle")
    elif head.startswith(b"FSB5"):
        problems.append(f"{name}: FMOD sound bank")
    elif head.startswith(b"\xaf\x1b\xb1\xfa"):
        problems.append(f"{name}: IL2CPP metadata")
    elif head[8:12] == b"FLW0":
        problems.append(f"{name}: Atlus flow script (FLW0)")

required = [exe, "LICENSE", "THIRD_PARTY_NOTICES.txt", "known-atlus-layouts.txt",
            "content/manifest.json", "content/provenance.json", "content/ASSET_CREDITS.md"]


def load(name):
    try:
        return json.loads(read(files[name][1])) if name in files else None
    except ValueError as e:
        problems.append(f"{name}: not JSON ({e})")


manifest = load("content/manifest.json")
if manifest is not None:
    required.append(posixpath.join("content", manifest.get("theme", "")))
    for font in manifest.get("fonts", {}).values():
        font = posixpath.join("content", font)
        # Each family's license sits beside it as <Family>-OFL.txt (content/provenance.json lists both).
        family = posixpath.basename(font).split("-")[0]
        required += [font, posixpath.join(posixpath.dirname(font), f"{family}-OFL.txt")]
    if not manifest.get("packs"):
        problems.append("content/manifest.json: lists no pack")
    for pack in manifest.get("packs", []):
        folder = posixpath.join("content", pack)
        required.append(posixpath.join(folder, "pack.json"))
        stages = (load(posixpath.join(folder, "pack.json")) or {}).get("stages", [])
        if not stages:
            problems.append(f"{folder}/pack.json: lists no stage")
        required += [posixpath.join(folder, stage.get("file", "")) for stage in stages]

problems += [f"{name}: missing" for name in dict.fromkeys(required) if name not in files]
if exe in files and files[exe][0] is not None and not files[exe][0] & 0o100:
    problems.append(f"{exe}: has no executable bit, so it won't start once unpacked")

for problem in problems:
    print(f"{archive}: {problem}")
if problems:
    sys.exit(1)
print(f"{archive}: ok, {len(files)} files, every one the manifest lists, nothing denied")
EOF
