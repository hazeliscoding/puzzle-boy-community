# AGENTS.md

These are the working rules for agents in this repo. Puzzle Boy Community Edition is a standalone puzzle game on the Puzzle Boy rules, with its own stages, art and sound, that needs no copy of *Shin Megami Tensei III Nocturne*. Licensed Apache-2.0.

## Sources of truth

- `README.md`: the pitch and how to build.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Work from the next unchecked item and record new decisions there. Don't build past the current milestone without asking.
- The rules live in puzzle-boy-engine (`external/puzzle-boy-engine`); its README documents the API.

## Content rules

- **No Atlus content.** No stage files or layouts (not even redrawn as JSON), text, dialogue, audio, textures, models, UI, asset bundles or decompiler output. No Manikin Kid.
- **Every file under `content/` has a provenance entry** in `content/provenance.json`: path, creator, source, category, SPDX license, whether it was modified and whether it may be redistributed. Third-party work keeps its own license; never mark it Apache-2.0 because the code is. Credit it in `content/ASSET_CREDITS.md` too.
- **The default game must start with nothing but this repo's build.** No Nocturne, Steam, network or dev checkout on the startup path.
- **No secrets or private paths** in committed files.

## Commands

- `git submodule update --init` fetches the engine. Until it's on GitHub, point a local clone at it first with `git config submodule.external/puzzle-boy-engine.url <path to puzzle-boy-engine>`, and run the update as `git -c protocol.file.allow=always submodule update --init`.
- `dotnet build PuzzleBoy.Community.slnx` builds everything. Warnings are errors.
- `dotnet test --solution PuzzleBoy.Community.slnx` runs the tests. They never need Nocturne.
- `dotnet format PuzzleBoy.Community.slnx --verify-no-changes` checks the style CI will enforce.
- `dotnet run --project src/PuzzleBoy.Community` starts the game.
- `scripts/check-content.sh` fails if a tracked file holds Atlus data or decompiler output. `scripts/check-content.test.sh` is its positive control: it plants each kind in a scratch repo and expects a failure. When you add a new kind, add both a check and a planted case.
- To move to a newer engine, commit in puzzle-boy-engine first, then `git -C external/puzzle-boy-engine fetch && git -C external/puzzle-boy-engine checkout <sha>` and commit `build: bump engine to <short-sha>` with the engine commit subjects in the body.

## Code layout

- `src/PuzzleBoy.Community`: the MonoGame (DesktopGL) game, assembly `PuzzleBoyCommunity`. No content pipeline.
- `tests/PuzzleBoy.Community.Tests`: xunit.v3 on Microsoft Testing Platform. Test names read as sentences.
- `external/puzzle-boy-engine`: the rules, as a submodule. Change them there, never here.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(content): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Every check that tests for an absence needs a positive control.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents.
- **Documentation:** every public type and member has an XML doc comment that says what it's for and any contract a caller can't see from the signature: units, ranges, what's null, what throws. Overrides use `<inheritdoc />`. The build fails on a missing one (CS1591); tests are exempt.
- **Comments in code:** explain why, not what. Mark a known limit as `shortcut: <the limit>, <when to upgrade>`.
- **Pragmatic code:** the smallest change that fully works. No abstractions, options or layers for later, and nothing the current roadmap item doesn't need.
