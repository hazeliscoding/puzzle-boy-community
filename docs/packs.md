# Level packs

A level pack is a `.zip` of stages that one player exports and another imports. It uses the same format as the packs built into the game.

## Share your stages

1. In the Level Editor, choose one of your stages, then **Export as Pack**.
2. Tick the stages to include, and set the pack's title, creator and license (CC0-1.0 by default; CC-BY-4.0, CC-BY-SA-4.0 or Apache-2.0).
3. Choose **Export**. The screen shows the full path of the `.zip`, in the `exports` folder of the game's data folder. An export with the same title replaces the last one.

A stage that matches a layout from the original game can't be exported, and neither can a pack whose title gives the id of the game's own stages (`first-steps`, `custom`, `playtest`).

## Import a pack

1. Put the `.zip` in the `imports` folder of the game's data folder. The **+ Import** tab in Stage Select shows where that folder is.
2. In that tab, choose the pack. If a pack with the same id is already imported, you can replace it (progress on its stages stays) or keep both (the new one gets `-2`).

The pack gets its own tab in Stage Select, marked as imported with its creator and license. Its stages unlock in order like a built-in pack's, and your progress saves per stage.

From a command line, `PuzzleBoyCommunity --import-pack <zip> [--replace | --keep-both] [--data-dir <dir>]` imports a pack, prints what happened and exits 0 on success. On Windows the message shows only when the output is redirected, such as `> import.txt`.

The data folder is `%APPDATA%\PuzzleBoyCommunity` on Windows, `~/Library/Application Support/PuzzleBoyCommunity` on macOS and `$XDG_DATA_HOME/puzzle-boy-community` (or `~/.local/share/puzzle-boy-community`) on Linux, unless `--data-dir` names another.

## Format

```text
pack.json
stages/first-push.json
stages/...
```

`pack.json` is the built-in pack format, schema version 1:

```json
{
  "format": "puzzle-boy-pack",
  "schemaVersion": 1,
  "packId": "shared-stages",
  "title": "Shared Stages",
  "creator": "Anonymous",
  "license": "CC0-1.0",
  "stages": [
    { "id": "first-push", "title": "First Push", "file": "stages/first-push.json" }
  ]
}
```

Ids are 1 to 48 of `a-z`, `0-9` and `-`. Each stage file is engine stage JSON (puzzle-boy-engine's `docs/stage-json.md`). A stage's progress is saved under `packId/id`.

## What import refuses

Everything is checked before anything is written to the data folder, in a scratch folder elsewhere; the pack then moves into `packs/<packId>` in one rename. Nothing in a pack is ever run.

- An archive over 2 MiB, with more than 256 entries, an entry that unpacks to more than 64 KiB, or more than 4 MiB in all.
- Absolute paths, drive letters, network (UNC) paths, backslashes, colons, and `.`, `..` or empty parts in a path.
- Links, entries named twice (ignoring case), other archives, and any file but `pack.json` and `stages/*.json`. A `.md` or `.txt` file is left out with a warning.
- A `pack.json` or stage that isn't valid, comes from a newer version of the game, lists a file outside `stages/`, or leaves a stage file unlisted.
- An id taken by a built-in pack, and any stage whose layout matches the original game's: "This layout matches a stage from the original game, so it can't be imported as a shared pack."
