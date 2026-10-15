# Roadmap

Puzzle Boy Community Edition is a standalone puzzle game on the Puzzle Boy rules, with its own stages, art and sound. This file tracks the decisions already made, what gets built and in what order.

## Decisions (2026.10.14)

- **Instant play comes first.** An unpacked release starts on a machine with no Nocturne, no Steam, no .NET, no network and no dev checkout, and reaches a playable stage. It never opens on a missing-assets screen.
- **No Atlus content.** No art, audio, text, models or UI from Nocturne, no Manikin Kid, no Nocturne dialogue and no original stage layout, not even converted to JSON. Importing from the player's own install is optional and comes last (C4).
- **The rules come from puzzle-boy-engine,** pinned as a git submodule at `external/puzzle-boy-engine`. `.gitmodules` names the GitHub URL; until the engine is published, a local clone overrides it in `.git/config` (AGENTS.md, Commands), and CI can't check out.
- **Content is data beside the executable.** `content/` is copied next to the game on build and publish. Every file in it has an entry in `content/provenance.json` with its creator, source, license and redistribution status, and `content/ASSET_CREDITS.md` credits it for people.
- **C# on .NET 10 with MonoGame (DesktopGL),** like the preservation port. Text is drawn with FontStashSharp (MIT) from TTFs read at runtime: Atkinson Hyperlegible for body text and Silkscreen for titles, both SIL OFL 1.1. Graphics are drawn in code (lit boxes, outlines, and a pattern per group so color is never the only cue), and sound is synthesized at startup.
- **Pixel Jack is the player character,** grown from the 16×16 brand mark (puzzle-boy's `docs/brand/`). It must not resemble Nocturne's Jack-o'-Lantern.
- **Stage ids belong to a pack** (`first-steps/nudge`), never to a Nocturne file number.
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
- **The campaign is `first-steps`: 20 original stages,** ordered walk, push, holes, multi-cell blocks, swivels, the switch tutorial (Make Way), combinations, and the finale (Long Way Round). They replace the three placeholder starters, which taught the same first ideas with less to solve. Candidates that repeated another stage (a second relay shaft, a second ordering puzzle, a turn cycle run twice, a swivel stage that was mostly pushing) were left out. The stages stay Apache-2.0 under the decision above, which already lets players remix them.
- **Saves are one JSON file, `save.json`,** in `%APPDATA%\PuzzleBoyCommunity`, `~/Library/Application Support/PuzzleBoyCommunity` or `$XDG_DATA_HOME/puzzle-boy-community` (`~/.local/share/puzzle-boy-community` without it), or in the folder `--data-dir` names. Smoke runs use a fresh temp folder, never the player's. A write goes to `save.json.tmp`, is flushed, then replaces `save.json` with `File.Replace`, which keeps the old one as `save.json.bak`. A damaged save is copied to `save.json.damaged` and the backup loads, else defaults; either way a notice shows over the game for a few seconds and play goes on. A save from a newer schema, or one that can't be opened at all (another program holding it), is left untouched and this run doesn't write; the second loads the backup or defaults.
- **Save schema 1** holds `settings` and `stages`, a map from stage id to `completed`, `bestMoves` and `bestTime` (seconds, from the stage starting to the clear, pauses excluded). Schema 0 is the legacy shape with no `schemaVersion`, `{ "cleared": [ids], "volume": 0..1 }`; it migrates to completed stages with no best, and one volume for music and sound. Values out of range are brought back in on load.
- **A pack unlocks in order:** its first stage, every cleared stage and the stage after each cleared one. Play starts the first unlocked stage not yet cleared, in pack order, or the last stage once all are cleared.
- **Settings apply at once and save on each change.** Fullscreen is borderless at the desktop's size. Window size is 75% to 200% of 1280×720, shrunk to fit the display. UI scale (90% to 120%) enlarges body text; the titles and the layout stay put. Reduce motion stops the drifting blocks, the title bob, the stars, the menu glow, the confetti and camera easing; steps still slide. High contrast draws text and panels opaque, hides the drifting blocks, and gives the board a flat lighter floor, ink walls and black pits.
- **Antialiasing is optional.** Some GL drivers, such as Mesa under WSLg, offer no multisampled context, and MonoGame then can't create a device. The game retries once without multisampling and says so on stderr; if that fails too, it explains on stderr and exits 1. A self-contained linux-x64 publish passed `--smoke-gui` under WSLg (Ubuntu 24.04, Mesa 25.2.8) this way, so WSLg is a tested configuration (2026.10.15).
- **`--smoke-gui-screens <dir>`** walks the title, stage select, settings, play, pause and help through the real menus and saves each as a PNG, for checking a visual change and for the README.

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

- [x] At least 12 original stages (aim for 20), covering walking, blocks, holes, swivels, combinations and a character-switch tutorial. 20 stages: StageCheck found every one valid, solved (7 to 161 inputs, no search limit reached) and not a known layout; `SolutionTests` replay all 20; `--smoke-gui` played the first three and Make Way (2026.10.15).
- [x] Stage select with locked and completed states, best moves and time. Each state has an icon and a word, and the focused stage shows a preview of how it starts. `--smoke-gui-screens` on a fresh profile and on one with stage 1 cleared showed both, and Play resumed at stage 2 (2026.10.15).
- [x] Progress and settings saved locally and atomically, with recovery from a damaged file. `SaveTests` cover a relaunch, the backup, a crash before the replace, a stale temp file, a corrupted save, a schema 0 migration, a newer schema and an unwritable folder.
- [x] Pause, help and settings; keyboard, mouse and controller. Menus read the same commands from keys, the d-pad and stick, and the mouse and wheel. Only the keyboard path has run in the real window (the smoke tour); the mouse and a real gamepad haven't been tried by hand.
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

- Key and controller remapping.
- Hints built on the stage tool's solver.
- More built-in packs.

## Not planned

- Shipping or downloading any Atlus asset or original stage layout.
- A startup that needs a game install, an account or the network. No telemetry.
- Scripts or code in stage packs.
