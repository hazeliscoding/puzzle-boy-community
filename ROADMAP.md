# Roadmap

Puzzle Boy Community Edition is a standalone puzzle game on the Puzzle Boy rules, with its own stages, art and sound. This file tracks the decisions already made, what gets built and in what order.

## Decisions (2026.10.14)

- **Instant play comes first.** An unpacked release starts on a machine with no Nocturne, no Steam, no .NET, no network and no dev checkout, and reaches a playable stage. It never opens on a missing-assets screen.
- **No Atlus content.** No art, audio, text, models or UI from Nocturne, no Manikin Kid, no Nocturne dialogue and no original stage layout, not even converted to JSON. Importing from the player's own install is optional and comes last (C4).
- **The rules come from puzzle-boy-engine,** pinned as a git submodule at `external/puzzle-boy-engine`. `.gitmodules` names the GitHub URL; until the engine is published, a local clone overrides it in `.git/config` (AGENTS.md, Commands), and CI can't check out.
- **Content is data beside the executable.** `content/` is copied next to the game on build and publish. Every file in it has an entry in `content/provenance.json` with its creator, source, license and redistribution status, and `content/ASSET_CREDITS.md` credits it for people.
- **C# on .NET 10 with MonoGame (DesktopGL),** like the preservation port. Text is drawn with FontStashSharp (MIT) from TTFs read at runtime: Atkinson Hyperlegible for body text and Silkscreen for titles, both SIL OFL 1.1. Graphics are drawn in code (lit boxes, outlines, and a pattern per group so color is never the only cue), and sound is synthesized at startup.
- **Pixel Jack is the player character,** grown from the 16×16 brand mark (puzzle-boy's `docs/brand/`). It must not resemble Nocturne's Jack-o'-Lantern.
- **Stage ids belong to a pack** (`first-steps/first-push`), never to a Nocturne file number.
- **Every bundled stage has a recorded solution** in the tests, replayed against the engine's `Board`.

## Decisions (2026.10.15)

- **Original content is Apache-2.0, like the code,** credited to the Puzzle Boy Community authors. That covers the stages, the theme and Pixel Jack, which puzzle-boy's `docs/brand/` already ships under Apache-2.0. Apache-2.0 grants no trademark rights (section 6), which suits a brand mark.
- **Provenance.** `content/provenance.json` has one entry per file or glob (`*` within a folder, `**` across): `path`, `creator`, `source`, `category`, `license` (SPDX), `modified` and `redistribution`, which must be `allowed` to ship. A file from elsewhere needs its own entry with a `sha256`, so a changed font fails the tests. Every file must be covered and every entry must match a file.
- **Pack format, schema version 1.** `pack.json` has `format` `puzzle-boy-pack`, `schemaVersion`, `packId`, `title`, `creator`, `license` and up to 200 `stages`, each with an `id`, a `title` and a `file` relative to the pack folder. Ids are 1 to 48 of `a-z`, `0-9` and `-`, unique in their pack; a stage's full id is `packId/id`. A higher `schemaVersion` fails with "made by a newer version". Paths are relative, use `/`, never contain `.`, `..` or a colon, and never pass through a link. `pack.json` and each stage are at most 64 KiB. A bad stage is reported and left out; the rest of the pack still loads.
- **Known layouts are caught by fingerprint.** `LayoutFingerprint` (version 1) hashes a stage's cropped tile grid in its smallest rotation or mirror, with and without the goal and starts. The 100 fingerprints of the 50 Nocturne stage files are in `scripts/known-atlus-layouts.txt`, generated outside the repo from a local install.
- **A stage tool for designers.** `tools/PuzzleBoy.StageCheck` validates, solves and fingerprints a stage or a pack. The solver is breadth-first over every input (four steps and, with two characters or more, a switch), so a solution has the fewest inputs; it reports the pushes and turns in it too. The engine's `Board` makes every move: a state is the cells and the visible characters from the active one, turned back into a stage to resume from, and a test checks that round trip against a live board on random walks. States are deduplicated by 128 bits of SHA-256. The default cap is 2,000,000 states, about 13 seconds on the owner's machine, and reaching it is reported as "search limit reached", never as unsolvable.
- **`--smoke-test` needs no window.** It runs the same checks as the content tests on the `content/` beside the executable, measures text with each font through FontStashSharp (which needs no graphics device for that), and takes one step on the first stage. CI runs it on the built game. A WinExe has no console on Windows, so there the report shows only when output is redirected; the exit code always works.
- **A stage may carry a hint.** A pack's stage entry can add a `hint` of up to 200 characters, which the help screen in play shows. A longer one leaves the stage out with a problem, like any other bad entry.
- **Screens are a stack.** Title is always at the bottom, so Back never quits; Quit to Title clears the stack, and finishing a pack returns to stage select. Screen logic, menu focus and input mapping are plain classes with tests; drawing isn't tested.
- **Input follows the view,** as in the original: direction = (screen direction - view turns) mod 4, the preservation port's formula. A step waits for the last one's animation (4 ticks a walk, 5 a push or turn, 9 with a fall) and for a turn of the view to land.
- **`--smoke-gui` runs by hand for now.** It needs a display, so CI doesn't run it until C5 adds one. Run it after a visual change and look at both pictures.
- **The starter pack is a placeholder.** `first-steps` has three tiny stages (walk, first push, first hole) so the loader and the tests have real content. C2 replaces it with the campaign.

## C1: Instant play

The handoff's M3.

- [x] Repo, engine pin, CI and the content guard with its positive control.
- [x] A default content pack with provenance and credits, a safe loader for it, and tests that reject unlisted files, denied formats and copies of known Nocturne layouts.
- [x] A stage tool that validates, solves (within a cap) and fingerprints a stage or a pack.
- [x] Starter stages, each with a solution the tests replay, and a headless `--smoke-test`.
- [x] A title screen and play of a bundled stage, with Pixel Jack, drawn graphics and synthesized sound; a `--smoke-gui` check that renders both. A self-contained win-x64 Release publish, run from a folder outside the repo, passed `--smoke-test` and `--smoke-gui` (2026.10.15).

**Done when** a local release build, unpacked on a machine with no Nocturne, no Steam and no network, reaches the title and plays an included stage.

## C2: Campaign and main UI

The handoff's M4.

- [ ] At least 12 original stages (aim for 20), covering walking, blocks, holes, swivels, combinations and a character-switch tutorial.
- [ ] Stage select with locked and completed states, best moves and time.
- [ ] Progress and settings saved locally and atomically, with recovery from a damaged file.
- [ ] Pause, help and settings; keyboard, mouse and controller.
- [ ] Graphics and sound polish.

**Done when** every bundled stage has a replay that clears it, a fresh profile works, saving and relaunching resumes progress, and every title option works.

## C3: Level editor and packs

The handoff's M5.

- [ ] Paint tiles, groups, the goal and starts; validate with messages that name the cell.
- [ ] Editor undo and redo, and playtest without changing the saved stage.
- [ ] Save, load, export and import portable packs, with safe file handling.

**Done when** a stage can be created, validated, playtested, saved, loaded after a restart, exported, imported on a fresh profile and played.

## C4: Optional classic import

The handoff's M6.

- [ ] Optional import of the classic look from the player's own Nocturne install.
- [ ] The solved unused stages as an Imported Bonus list, read from the player's install in the documented 23-file order.
- [ ] Missing or damaged imports fall back to the default content with a message.

**Done when** the default game still starts with no import, and a player's own files add the classic look and bonus stages without touching the built-in campaign.

## C5: Releases

The handoff's M7.

- [ ] Self-contained archives for win-x64, linux-x64, osx-arm64 and osx-x64, with `content/` inside.
- [ ] CI unpacks each archive and runs the smoke checks; Windows and Linux start the real window.
- [ ] Changelog, credits, troubleshooting and the signing status.

**Done when** a tagged release publishes only after the 12-stage minimum, the editor, provenance, smoke tests and archive checks pass.

## Later

- Controller remapping.
- Hints built on the stage tool's solver.
- More built-in packs.

## Not planned

- Shipping or downloading any Atlus asset or original stage layout.
- A startup that needs a game install, an account or the network. No telemetry.
- Scripts or code in stage packs.
