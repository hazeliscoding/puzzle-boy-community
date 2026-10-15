# AGENTS.md

These are the working rules for agents in this repo. Puzzle Boy Community Edition is a standalone puzzle game on the Puzzle Boy rules, with its own stages, art and sound, that needs no copy of *Shin Megami Tensei III Nocturne*. Licensed Apache-2.0.

## Sources of truth

- `README.md`: the pitch and how to build.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Work from the next unchecked item and record new decisions there. Don't build past the current milestone without asking.
- The rules live in puzzle-boy-engine (`external/puzzle-boy-engine`); its README documents the API.

## Content rules

- **No Atlus content.** No stage files or layouts (not even redrawn as JSON), text, dialogue, audio, textures, models, UI, asset bundles or decompiler output. No Manikin Kid.
- **Every file under `content/` has a provenance entry** in `content/provenance.json`: path, creator, source, category, SPDX license, whether it was modified and whether it may be redistributed. Third-party work keeps its own license; never mark it Apache-2.0 because the code is. Credit it in `content/ASSET_CREDITS.md` too.
- **Content holds only `.json`, `.md`, `.txt`, `.ttf`, `.png`, `.wav` and `.ogg` files,** and no links. The tests and `scripts/check-content.sh` both enforce it.
- `scripts/known-atlus-layouts.txt` holds irreversible SHA-256 fingerprints of the 50 Nocturne stage layouts (`LayoutFingerprint`, a full and a terrain-only one each), used only to reject copies: the tests fail if a bundled stage matches one, even rotated, mirrored, moved or regrouped. It can't be turned back into a layout. Never change the fingerprint algorithm without regenerating it, which needs the game's files and so happens outside this repo.
- **The default game must start with nothing but this repo's build.** No Nocturne, Steam, network or dev checkout on the startup path.
- **Every package the game ships is in `THIRD_PARTY_NOTICES.txt`** with its license text; it and `LICENSE` are copied beside the game, and a test fails for a package missing from it. Add the notice with any new dependency.
- **No secrets or private paths** in committed files.

## Commands

- `git submodule update --init` fetches the engine. Until it's on GitHub, point a local clone at it first with `git config submodule.external/puzzle-boy-engine.url <path to puzzle-boy-engine>`, and run the update as `git -c protocol.file.allow=always submodule update --init`.
- `dotnet build PuzzleBoy.Community.slnx` builds everything. Warnings are errors.
- `dotnet test --solution PuzzleBoy.Community.slnx` runs the tests. They never need Nocturne.
- `dotnet format PuzzleBoy.Community.slnx --verify-no-changes` checks the style CI will enforce.
- `dotnet run --project src/PuzzleBoy.Community` starts the game. Arrows or WASD move (hold to keep walking), Q and E turn the view, V switches to overhead, C switches character on stages with more than one, Z or Backspace undoes, R restarts, Esc or P pauses. Menus take arrows, Enter or Space, Esc or Backspace for Back, and the mouse. A gamepad works too: d-pad or stick, A, B (Back in menus, undo in play), LB/RB, Y, X, Start. Stage select's Left and Right switch between the packs and My Stages.
- In the level editor: arrows move the cursor (Shift+arrows pan), Space or Enter paints (hold and move to keep painting), 1 to 9 pick wall, floor, hole, block, swivel center, swivel arm, goal, start and eraser, `[` and `]` change the group (or the start), N takes a new group, H toggles over a hole, G picks from the cell, R starts and fills a rectangle, + and − zoom, Tab jumps to the next problem, T renames, P playtests (Esc comes back), Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes, Ctrl+S saves, Esc opens the menu (Undo, Redo, Playtest, Solve, Save, Rename, Close). The mouse paints, Shift+drag fills a rectangle, right or middle drag pans and the wheel zooms. On a pad: A paints, LB/RB change tool, X jumps to a problem, Y playtests, Start or B opens the menu.
- `dotnet run --project src/PuzzleBoy.Community -- --smoke-gui <png>` opens the real window, saves the title to `<png>`, starts the stage Play picks (stage 1 on a fresh profile) through the Play menu item, takes one step and saves the stage beside it with a `-stage` suffix. It exits 0 only if both were saved and the step moved the character. Look at both pictures after a visual change.
- `dotnet run --project src/PuzzleBoy.Community -- --smoke-gui-screens <dir>` (alone or with `--smoke-gui`) also visits stage select, settings (where it turns fullscreen on and off), credits, the level editor, My Stages, pause and help, and saves `title.png`, `stage-select.png`, `settings.png`, `credits.png`, `editor-list.png`, `editor.png` (a sample with a split block group), `playtest.png`, `editor-solve.png`, `my-stages.png`, `playing.png`, `pause.png` and `help.png` in `<dir>`. In the editor it saves a bundled stage as the player's, breaks it with the keyboard, checks that it won't playtest, undoes, playtests, comes back and solves.
- `--data-dir <dir>` keeps the save in `<dir>` instead of the OS app-data folder. Smoke runs default to a fresh temp folder. Put a `save.json` there first to check a screen with progress or settings.
- `dotnet run --project src/PuzzleBoy.Community -- --smoke-test` checks, with no window, the `content/` beside the built game: provenance, manifest, packs, fonts, and a step on the first stage. It exits 0 or 1 with a short report (on Windows the report shows only when output is redirected).
- `dotnet run --project tools/PuzzleBoy.StageCheck -- <stage.json | pack folder> [--cap N] [--known <list>]` validates each stage, finds a shortest solution (at most N states, default 2,000,000; "search limit reached" never means unsolvable) and checks its fingerprints against `scripts/known-atlus-layouts.txt`, a copy of which sits beside the tool. It exits 1 for an invalid, unsolvable or known stage. Run it on every new stage.
- `scripts/check-content.sh` fails if a tracked file holds Atlus data or decompiler output. `scripts/check-content.test.sh` is its positive control: it plants each kind in a scratch repo and expects a failure. When you add a new kind, add both a check and a planted case.
- To move to a newer engine, commit in puzzle-boy-engine first, then `git -C external/puzzle-boy-engine fetch && git -C external/puzzle-boy-engine checkout <sha>` and commit `build: bump engine to <short-sha>` with the engine commit subjects in the body.

## Code layout

- `src/PuzzleBoy.Community`: the MonoGame (DesktopGL) game, assembly `PuzzleBoyCommunity`. No content pipeline.
  - `App/`: the screens (`Navigator`), menu focus (`MenuList`), the settings rows (`SettingsMenu`), input as commands (`Controls`) and the 1280×720 virtual screen. Plain classes, tested without a graphics device.
  - `Save/`: the player's settings and progress (`SaveData`) and `save.json` on disk (`SaveStore`): atomic writes, the backup, recovery and migration. `CustomStages` keeps the player's own stages in `custom/` under safe names. Nothing in it throws for a bad file.
  - `Play/`: a stage in play (`PlaySession`, around the engine's `Board`), step animations, the camera, whose quarter turns turn the controls with the view as in the original, and the capped breadth-first `Solver`.
  - `Editor/`: the level editor's stage (`StageEditor`, over the engine's `StageBuilder`: tools, groups, strokes, undo and validation) and its canvas pan and zoom (`EditorView`). Plain classes, tested without a graphics device.
  - `Graphics/` and `Audio/`: everything drawn and heard, made in code: lit boxes from one generated texture, Pixel Jack as voxels from the theme's mark, text through FontStashSharp, and chiptune sound rendered at startup. No audio device means silence.
  - `CommunityGame*.cs`: the window, split into the screen loop, the menus, play and the editor screen.
  - `Content/`: reading `content/` safely. `ContentManifest` reads `manifest.json` and its packs, `StagePack` a pack's `pack.json` and stages (through the engine's `StageDocument`), `Provenance` checks `provenance.json`, `LayoutFingerprint` and `KnownLayouts` catch copies of Nocturne layouts. Every path stays inside its folder, nothing follows a link, every file has a size cap, and bad input becomes a list of problems, never an exception.
- `content/`: the built-in content, copied beside the executable on build and publish. `manifest.json` lists the theme, the fonts and the packs; each pack is a folder with a `pack.json` and engine stage JSON files.
- `tools/PuzzleBoy.StageCheck`: the stage tool. It compiles the game's `Content/` sources and `Play/Solver.cs` in, so it carries no MonoGame. `Solver` searches breadth-first with the engine's `Board` making every move.
- `tests/PuzzleBoy.Community.Tests`: xunit.v3 on Microsoft Testing Platform. Test names read as sentences. `ContentTests` check the real `content/` and plant each kind of violation in a scratch copy.
  - `Solutions/<pack>/<stage>.txt`: one line of moves per bundled stage (`R` +x, `L` −x, `D` +y, `U` −y, `S` switch). `SolutionTests` replay each on a fresh board and fail for a stage without one. Take a new stage's line from StageCheck.
- `external/puzzle-boy-engine`: the rules, as a submodule. Change them there, never here.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(content): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Every check that tests for an absence needs a positive control.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents.
- **Documentation:** every public type and member has an XML doc comment that says what it's for and any contract a caller can't see from the signature: units, ranges, what's null, what throws. Overrides use `<inheritdoc />`. The build fails on a missing one (CS1591); tests are exempt.
- **Comments in code:** explain why, not what. Mark a known limit as `shortcut: <the limit>, <when to upgrade>`.
- **Pragmatic code:** the smallest change that fully works. No abstractions, options or layers for later, and nothing the current roadmap item doesn't need.
