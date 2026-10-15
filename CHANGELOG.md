# Changelog

Notable changes to Puzzle Boy Community Edition. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- A Nocturne-styled interface for the Classic theme. In play: the original's stage counter, COMPLETE!, UNDO, scroll arrows and controls bar on its timing, its menu (with Settings) and its rules page, then a page of every control. Community's screens keep their layout and words in the original's font, windows, cursor, key prompts and backdrop, and the title shows the Jack-o'-Lantern. The Default theme's interface is unchanged.
- F opens the menu, as in the original, in both themes.
- A classic pack in a `classic-pack` folder beside the game is used, read-only, when your data folder has none, for a build you carry to your own computers.

### Changed

- Classic packs now hold the original's UI and text bundles too (schema 2, about 120 MB). A pack imported by 0.1.0 still plays with Community's menus and says once a session to import again.

## [0.1.0] - 2026-10-15

The first release.

### Added

- First Steps, a campaign of 20 new stages: walking, blocks, holes, swivels, a switch tutorial and a long finale. Each has a recorded solution the tests replay.
- Title, stage select (locked, new and cleared stages, best moves and time, a preview of each), pause, help, settings and credits. Keyboard, mouse and controller.
- Progress and settings saved on your computer, with a backup and recovery from a damaged save.
- A level editor: paint, validate with messages that name the cell, undo and redo, playtest, and a solver that gives up honestly at its limit. Your stages go under My Stages.
- Level packs: export your stages as a zip and import other players' packs, each checked before it's added.
- The Classic Nocturne theme, from your own copy of Shin Megami Tensei III Nocturne HD Remaster: import it once and every stage plays in the original's look and sound. Export the pack to carry it to your other computers. Classic: Original 20 and Classic: Bonus (23 unused stages) come with it.
- The Default theme, drawn and synthesized in code, so the game plays out of the box with no copy of Nocturne.
- The original's input timing and its Tab scroll mode, the same in both themes.
- Self-contained archives for Windows x64, Linux x64 and macOS (Apple Silicon and Intel), each unpacked and smoke-tested in CI before a release can publish.

[Unreleased]: https://github.com/hazeliscoding/puzzle-boy-community/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/hazeliscoding/puzzle-boy-community/releases/tag/v0.1.0
