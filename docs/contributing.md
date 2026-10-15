# Contributing

## Build

You need the .NET 10 SDK (`global.json` pins it) and git.

```bash
git clone --recurse-submodules https://github.com/hazeliscoding/puzzle-boy-community.git
cd puzzle-boy-community
dotnet build PuzzleBoy.Community.slnx
dotnet test --solution PuzzleBoy.Community.slnx
dotnet format PuzzleBoy.Community.slnx --verify-no-changes
bash scripts/check-content.test.sh && bash scripts/check-content.sh
bash scripts/check-archive.test.sh
dotnet run --project src/PuzzleBoy.Community
```

A clone made without `--recurse-submodules` needs `git submodule update --init`. Warnings are errors, and every public member needs an XML doc comment. The tests never need Nocturne. [AGENTS.md](../AGENTS.md) lists every command and flag.

## The engine pin

The rules and the Classic theme's readers live in [puzzle-boy-engine](https://github.com/hazeliscoding/puzzle-boy-engine), pinned at `external/puzzle-boy-engine`. A rule change and its test go there first. Then both games move to the same engine commit:

```bash
git -C external/puzzle-boy-engine fetch
git -C external/puzzle-boy-engine checkout <sha>
git add external/puzzle-boy-engine
git commit -m "build: bump engine to <short-sha>"
```

List the engine commit subjects in the body, and bump [puzzle-boy](https://github.com/hazeliscoding/puzzle-boy) to the same SHA. To try unpushed engine work, point the submodule at a local clone (AGENTS.md, Commands).

## Adding a stage

1. **Author it** in the game's level editor, which saves it to `custom/<name>.json` in your data folder, or by hand in the engine's [stage JSON](https://github.com/hazeliscoding/puzzle-boy-engine/blob/main/docs/stage-json.md).
2. **Check it:** `dotnet run --project tools/PuzzleBoy.StageCheck -- <stage.json>`. It must be valid, solved ("search limit reached" isn't a proof either way) and not a known Nocturne layout. It prints a shortest solution.
3. **Add it to a pack:** copy it into `content/stages/<pack>/` and add an entry to that pack's `pack.json`: `id`, `title`, `file`, and an optional `hint` of up to 200 characters.
4. **Record a solution** in `tests/PuzzleBoy.Community.Tests/Solutions/<pack>/<id>.txt`: one line of `R`, `L`, `D` (down the drawing, +y), `U` and `S` (switch). StageCheck's solution works.
5. **Provenance:** your own stage is covered by the `stages/**/*.json` entry in `content/provenance.json`, as Apache-2.0 work of the Puzzle Boy Community authors. A stage by someone else needs its own entry with their name, the source, its license and its `sha256`, and a line in `content/ASSET_CREDITS.md`.
6. **Run the tests.** `SolutionTests` replay the solution, `ContentTests` check provenance and fingerprints, and `ThemeSwitchTests` play it in both themes. Run StageCheck on the whole pack folder too.

## Asset rules

- Every file under `content/` has a provenance entry: creator, source, category, SPDX license, whether it was modified, and whether it may be redistributed. Content holds only `.json`, `.md`, `.txt`, `.ttf`, `.png`, `.wav` and `.ogg`.
- Assets are original to this repo (Apache-2.0), or someone else's under an explicit CC0-1.0, CC-BY-4.0 or OFL-1.1 license, with the evidence: where it came from, its license text, and its `sha256` in the provenance entry. Credit it in `content/ASSET_CREDITS.md`.
- Nothing derived from Nocturne or any Atlus game: no art, sound, text or stage layout, traced, redrawn, re-recorded or converted. The fingerprint check catches copied layouts, even rotated or mirrored.
- A classic pack, a Unity bundle or anything read from your install never enters the repo, `content/` or a release. The guards and their planted positive controls enforce it.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/), one logical change each, such as `feat(editor): …`, `fix(play): …`, `docs: …` or `build: bump engine to <sha>`. Record new decisions in `ROADMAP.md`.
