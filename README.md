# Puzzle Boy Community Edition

**A standalone puzzle game inspired by Puzzle Boy. Download and play immediately: no Nocturne required.**

Guide Pixel Jack, a pumpkin in a witch's hat, to the goal by pushing blocks, turning swivels and filling holes. The stages, art, sound and text are all made for this game. It needs no copy of *Shin Megami Tensei III Nocturne*, no Steam and no network.

> **Status:** early development, with nothing to download yet. See [ROADMAP.md](ROADMAP.md).

It plays on [puzzle-boy-engine](https://github.com/hazeliscoding/puzzle-boy-engine), the rules shared with [puzzle-boy](https://github.com/hazeliscoding/puzzle-boy), a preservation port that reads the original stages from your own copy of Nocturne. The engine isn't published yet.

## Build

```bash
git clone --recurse-submodules https://github.com/hazeliscoding/puzzle-boy-community.git
cd puzzle-boy-community
dotnet run --project src/PuzzleBoy.Community
```

## License

The code is licensed under [Apache-2.0](LICENSE).

*Shin Megami Tensei*, *Nocturne* and *Puzzle Boy* are trademarks of ATLUS and SEGA. This is an independent fan project. It is not affiliated with or endorsed by either company, and it contains no ATLUS or SEGA assets.
