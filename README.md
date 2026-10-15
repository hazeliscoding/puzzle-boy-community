# Puzzle Boy Community Edition

**A standalone puzzle game inspired by Puzzle Boy. Download and play immediately: no Nocturne required.**

Guide Pixel Jack, a pumpkin in a witch's hat, to the goal by pushing blocks, turning swivels and filling holes. The stages, art, sound and text are all made for this game. It needs no copy of *Shin Megami Tensei III Nocturne*, no Steam and no network.

> **Status:** early development, with nothing to download yet. See [ROADMAP.md](ROADMAP.md).

## Features

- Twenty original stages, from a first walk to the goal through blocks, holes, swivels and a team of up to three Jacks, each with a hint on the help screen.
- Push blocks, turn swivels and fill holes with the same rules as the original, from puzzle-boy-engine.
- Stage select with cleared, new and locked stages, your best moves and time, and a preview of each stage.
- A level editor: paint a stage, see every problem by its cell, playtest it, ask the solver, and keep it under My Stages.
- Progress and settings saved on your computer, with a backup if the file is ever damaged. No account, no network.
- Settings for fullscreen, window size, UI scale, music and sound volume, mute, reduced motion and high contrast.
- Keyboard, mouse and controller. Undo, restart, a turning camera and an overhead view.
- Graphics drawn and music synthesized in code: nothing to download after you unpack it.

It plays on [puzzle-boy-engine](https://github.com/hazeliscoding/puzzle-boy-engine), the rules shared with [puzzle-boy](https://github.com/hazeliscoding/puzzle-boy), a preservation port that reads the original stages from your own copy of Nocturne. The engine isn't published yet.

## Build

```bash
git clone --recurse-submodules https://github.com/hazeliscoding/puzzle-boy-community.git
cd puzzle-boy-community
dotnet run --project src/PuzzleBoy.Community
```

## License

The code is licensed under [Apache-2.0](LICENSE). The fonts are under the SIL Open Font License 1.1; [content/ASSET_CREDITS.md](content/ASSET_CREDITS.md) credits every file in `content/`. The libraries every build ships (MonoGame, SDL2, OpenAL Soft, FontStashSharp and others) keep their own licenses, collected in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt), which is copied beside the game.

*Shin Megami Tensei*, *Nocturne* and *Puzzle Boy* are trademarks of ATLUS and SEGA. This is an independent fan project. It is not affiliated with or endorsed by either company, and it contains no ATLUS or SEGA assets.
