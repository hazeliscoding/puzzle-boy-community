# Install and troubleshooting

## Download and run

Download the archive for your system from [Releases](https://github.com/hazeliscoding/puzzle-boy-community/releases). Everything the game needs is inside: no .NET, no Steam, no network and no copy of Nocturne. `SHA256SUMS` on the release lists each archive's hash.

| System | Archive | Run |
|---|---|---|
| Windows 10 or 11, x64 | `puzzle-boy-community-win-x64.zip` | Extract all, then run `PuzzleBoyCommunity.exe`. Don't run it from inside the zip. |
| Linux x64 | `puzzle-boy-community-linux-x64.tar.gz` | `tar xzf` it, then `./PuzzleBoyCommunity`. |
| macOS on Apple Silicon | `puzzle-boy-community-osx-arm64.tar.gz` | Unpack it, then see Gatekeeper below. |
| macOS on Intel | `puzzle-boy-community-osx-x64.tar.gz` | Same. |

Keep the files together: the game reads `content/` from beside itself.

## Unsigned builds

The builds aren't signed, so each system warns the first time.

- **Windows SmartScreen** may say it protected your PC. Choose More info, then Run anyway.
- **macOS Gatekeeper** blocks apps from unidentified developers. Open Terminal in the unpacked folder and run `xattr -dr com.apple.quarantine .`, then `./PuzzleBoyCommunity`. The executables carry only the SDK's ad-hoc signature.

## Linux

- **The executable bit.** The tarball keeps it. If a tool dropped it, run `chmod +x PuzzleBoyCommunity`.
- **OpenGL.** The game needs a working OpenGL driver; on most distributions that's Mesa. `glxinfo -B` (from `mesa-utils`) shows whether you have one. If the driver offers no antialiasing, the game retries without it and says so on stderr. If no window can open at all, it explains why on stderr and exits 1: install your distribution's Mesa or GPU driver packages. It has run under WSLg on Windows and under Xvfb in CI, not yet on a Linux desktop.
- **Sound.** SDL2 and OpenAL Soft come in the archive. With no audio device the game plays silently.

## Where your saves live

| System | Folder |
|---|---|
| Windows | `%APPDATA%\PuzzleBoyCommunity` |
| macOS | `~/Library/Application Support/PuzzleBoyCommunity` |
| Linux | `$XDG_DATA_HOME/puzzle-boy-community`, or `~/.local/share/puzzle-boy-community` |

`save.json` holds your progress and settings, with `save.json.bak` beside it. `custom/` holds your stages, `packs/` the level packs you imported, `imports/` the zips waiting to be imported, `exports/` what you exported, and `classic/` your classic pack. `--data-dir <folder>` uses another folder instead.

To reset:

- **Settings:** Settings → Reset to defaults. Your progress is kept.
- **Progress:** quit, then delete `save.json` and `save.json.bak`. Your stages and packs stay.
- **Everything:** quit and delete the whole folder.

If `save.json` is ever damaged, the game copies it to `save.json.damaged`, loads the backup, and says so.

## The Classic Nocturne theme

It needs your own copy of *Shin Megami Tensei III Nocturne HD Remaster*. Nothing from Nocturne comes with this game or is downloaded.

- **Import:** Settings → Classic Nocturne → Import from My Steam Install. It looks beside the game and in your Steam libraries, and copies the eight bundles the theme reads (about 120 MB) into `classic/`. A pack imported by version 0.1.0 has only three: it plays with Community's menus until you import again. If your copy is somewhere else, choose Import from a Folder or Zip and type the folder that holds `smt3hd.exe`.
- **Export:** Export My Classic Pack writes `exports/puzzle-boy-classic-pack.zip`. Import that zip on your other computers, including a Mac, which has no Nocturne of its own. It's made from your own copy, for your own computers: don't share it.
- **Theme:** Settings → Theme. Auto plays Classic when you have a pack and Default when you don't.
- **Beside the game:** a build you put together for yourself can carry your pack in a `classic-pack` folder next to the executable (the folder an export unzips to: `classic-pack.json` and `smt3hd_Data/`). When your data folder has no pack, the game uses that one, checked like an imported pack and only ever read; it's never copied into your data folder, and nothing is written next to the game. A pack you import wins over it. It's your own copy, for your own computers: don't share a build that carries it. Release archives never hold one.
- From a command line: `--import-classic <game folder, pack folder or zip>` and `--export-classic`.

When something's wrong, nothing is imported, your earlier pack stays, and the message says what to do:

| Message starts with | What to do |
|---|---|
| Couldn't find Shin Megami Tensei III Nocturne HD Remaster | Choose Import from a Folder or Zip and type the folder that holds `smt3hd.exe`. |
| There's no folder at … | Check the path you typed. |
| … has the Puzzle Boy stages but not the … bundle / The … bundle can't be read | Your install is missing or has a damaged file. In Steam, verify the game's files (Properties, Installed Files), then try again. |
| Nothing was imported: … isn't a zip / has no classic-pack.json | Choose the zip that Export My Classic Pack made. |
| Nothing was imported: … unpacks to more than a classic pack holds | It isn't a classic pack. |
| Your classic pack can't be used, so this plays with the Default theme | A file in `classic/` changed. Import again. |
| The Classic theme needs your own classic pack | The Theme setting is Classic but there's no pack. Import one, or set Theme to Auto. |

Remove Classic Pack deletes it again. Your progress on the classic stages is kept either way.

## Report a bug

Open an issue at [github.com/hazeliscoding/puzzle-boy-community/issues](https://github.com/hazeliscoding/puzzle-boy-community/issues) with:

- the version from Credits, such as `0.1.0+0d57628`, and your system;
- what you did, what you expected and what happened;
- the output of the built-in check: `./PuzzleBoyCommunity --smoke-test` on Linux and macOS, or `PuzzleBoyCommunity.exe --smoke-test > report.txt 2>&1` on Windows, where it shows only when redirected.

A screenshot helps. Don't attach your classic pack or any file from Nocturne.
