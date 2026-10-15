using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Editor;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// <c>--smoke-gui &lt;png&gt;</c>: drives the real window from the title into the stage Play picks (stage 1 on a fresh
/// profile) through the same menu and play code a player uses, takes one step and saves what it drew. It proves a build
/// opens a window, renders and takes input; it never plays a stage through. With a screens folder
/// (<c>--smoke-gui-screens</c>) it also visits stage select, settings (turning fullscreen on and off there), credits, the
/// level editor (its list, a sample stage with a problem, a playtest, Solve and an export of the sample as a pack), stage
/// select's My Stages, its Import tab (importing that pack and starting its stage), the pause menu and help on the way and
/// saves each.
/// </summary>
/// <param name="titlePng">Where to write the title screenshot; the stage one goes beside it with a <c>-stage</c> suffix.</param>
/// <param name="screensDir">
/// A folder for <c>title.png</c>, <c>stage-select.png</c>, <c>settings.png</c>, <c>credits.png</c>,
/// <c>editor-list.png</c>, <c>editor.png</c>, <c>playtest.png</c>, <c>editor-solve.png</c>, <c>export.png</c>,
/// <c>my-stages.png</c>, <c>import.png</c> (and <c>import-conflict.png</c> when the pack is already there),
/// <c>imported.png</c>, <c>imported-play.png</c>, <c>playing.png</c>, <c>pause.png</c> and <c>help.png</c> (and
/// <c>help-controls.png</c>, help's second page, in the Classic theme's original menus), or null to save only the two
/// screenshots.
/// </param>
internal sealed class SmokeGui(string titlePng, string? screensDir = null)
{
    // A slow first frame or a window still settling must not end the run; a stuck one must.
    private const int TimeoutTicks = 2400;

    // Frames drawn on the title before it's saved, so the backdrop and fonts have settled.
    private const int TitleFrames = 5;

    private readonly List<string> _pending = [];
    private IEnumerator<Keys[]>? _script;
    private int _frames;
    private int _ticks;
    private Keys[] _held = [];
    private bool _done;
    private string? _passed;

    /// <summary>The title screenshot's path.</summary>
    public string TitlePng { get; } = Path.GetFullPath(titlePng);

    /// <summary>The stage screenshot's path: the title's with <c>-stage</c> before the extension.</summary>
    public string StagePng => Path.Combine(Path.GetDirectoryName(TitlePng)!, $"{Path.GetFileNameWithoutExtension(TitlePng)}-stage{Path.GetExtension(TitlePng)}");

    /// <summary>The folder for every screen's screenshot, or null.</summary>
    public string? ScreensDir { get; } = screensDir is null ? null : Path.GetFullPath(screensDir);

    /// <summary>0 once both screenshots are saved and the step changed the board; 1 until then.</summary>
    public int ExitCode { get; private set; } = 1;

    /// <summary>True once the run has passed or failed and is only waiting to exit.</summary>
    public bool Done => _done;

    /// <summary>Why the run failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The input for this tick, in place of the real keyboard and pad.</summary>
    public Controls Input(CommunityGame game)
    {
        var previous = _held;
        _held = [];
        if (++_ticks > TimeoutTicks && !_done)
        {
            Fail(game, "timed out");
        }

        _script ??= Script(game);
        if (!_done && _script.MoveNext())
        {
            _held = _script.Current;
        }

        return Controls.FromKeys(_held, previous);
    }

    /// <summary>Called after each frame is drawn, before it's shown: saves any screenshot asked for, and ends a finished run.</summary>
    public void AfterDraw(CommunityGame game)
    {
        _frames++;
        foreach (var path in _pending)
        {
            Save(game.GraphicsDevice, path);
        }

        _pending.Clear();
        if (_passed is not null && ExitCode != 0)
        {
            ExitCode = 0;
            Console.WriteLine($"smoke gui passed: {_passed}");
            game.Exit();
        }
    }

    /// <summary>The run, one tick's held keys at a time.</summary>
    private IEnumerator<Keys[]> Script(CommunityGame game)
    {
        while (_frames < TitleFrames)
        {
            yield return [];
        }

        foreach (var tick in Shot(TitlePng, "title.png"))
        {
            yield return tick;
        }

        if (ScreensDir is not null)
        {
            // Title focus starts on Play. Down once is Stage Select; Back keeps the focus; two more is Settings, one more Credits.
            foreach (var tick in Visit(game, [Keys.Down], Screen.StageSelect, "stage-select.png"))
            {
                yield return tick;
            }

            foreach (var tick in Visit(game, [Keys.Down, Keys.Down], Screen.Settings, "settings.png", ToggleFullscreen(game).Concat(ImportClassic(game))))
            {
                yield return tick;
            }

            foreach (var tick in Visit(game, [Keys.Down], Screen.Credits, "credits.png"))
            {
                yield return tick;
            }

            // Credits is the fifth item; two up is the Level Editor, and one more Stage Select.
            foreach (var tick in Press(Keys.Up, Keys.Up).Concat(Editor(game)))
            {
                yield return tick;
            }

            // My Stages, which holds the sample the editor visit saved, is right of the bundled and imported packs.
            foreach (var tick in Press(Keys.Up, Keys.Enter))
            {
                yield return tick;
            }

            for (var tab = 0; tab < 16 && game.SelectPack is { PackId: not "custom" }; tab++)
            {
                // Classic stages show only on a data folder that has them (--data-dir), never on a fresh profile.
                var classic = game.SelectPack.PackId is ClassicStages.OriginalPackId or ClassicStages.BonusPackId
                    ? Shot(null, $"{game.SelectPack.PackId.Replace('/', '-')}.png")
                    : [];
                foreach (var tick in classic.Concat(Press(Keys.Right)))
                {
                    yield return tick;
                }
            }

            if (game.CurrentScreen != Screen.StageSelect || game.SelectPack is not { PackId: "custom", Stages: [{ Id: "custom/sample" }] })
            {
                Fail(game, "stage select's My Stages tab didn't list the sample");
                yield break;
            }

            foreach (var tick in Shot(null, "my-stages.png").Concat(Import(game)).Concat(Press(Keys.Up)))
            {
                yield return tick;
            }
        }

        foreach (var tick in Press(Keys.Enter))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Playing || game.Session is not { } session)
        {
            Fail(game, $"Play opened {game.CurrentScreen}, not a stage");
            yield break;
        }

        // Hold a screen direction the camera maps to a step the first stage allows, until the board takes it.
        var before = (session.Board.Active.X, session.Board.Active.Y);
        var screen = Enumerable.Range(0, 4).FirstOrDefault(s => new Board(session.Stage.Stage).Step(game.Camera.DirectionFor(s)) != StepResult.Refused, -1);
        if (screen < 0)
        {
            Fail(game, "no step is open on the first stage");
            yield break;
        }

        while (session.Moves == 0)
        {
            yield return [new[] { Keys.Up, Keys.Right, Keys.Down, Keys.Left }[screen]];
        }

        while (session.Animation is not null || session.Ticks < CommunityGame.IntroTicks)
        {
            yield return [];
        }

        var after = (session.Board.Active.X, session.Board.Active.Y);
        if (after == before)
        {
            Fail(game, "the step didn't move the character");
            yield break;
        }

        _pending.Add(StagePng);
        foreach (var tick in Shot(null, "playing.png"))
        {
            yield return tick;
        }

        if (ScreensDir is not null)
        {
            foreach (var tick in Press(Keys.Escape))
            {
                yield return tick;
            }

            if (game.CurrentScreen != Screen.Paused)
            {
                Fail(game, $"Esc in play opened {game.CurrentScreen}, not the pause menu");
                yield break;
            }

            foreach (var tick in Shot(null, "pause.png"))
            {
                yield return tick;
            }

            // Help is the pause menu's fourth item, or the third of the Classic theme's original menu.
            var help = game.PauseMenuActions.ToList().IndexOf(PauseAction.Help);
            foreach (var tick in Press([.. Enumerable.Repeat(Keys.Down, help), Keys.Enter]))
            {
                yield return tick;
            }

            foreach (var tick in Shot(null, "help.png"))
            {
                yield return tick;
            }

            if (game.ClassicMenus)
            {
                // The original's rules page comes first; Community's controls are the next page, and Left goes back.
                foreach (var tick in Press(Keys.Right).Concat(Shot(null, "help-controls.png")).Concat(Press(Keys.Left)))
                {
                    yield return tick;
                }
            }
        }

        var theme = game.PlaysClassic ? "Classic" : "Default";
        if (ScreensDir is not null)
        {
            foreach (var tick in ClassicBonus(game))
            {
                yield return tick;
            }
        }

        if (game.SkinFallbacks.Count > 0)
        {
            Console.WriteLine($"smoke gui: the Classic font lacked a character of {game.SkinFallbacks.Count} string(s), drawn in Community's font: {string.Join(" | ", game.SkinFallbacks)}");
        }

        _done = true;
        _passed = $"{TitlePng}, {StagePng}{(ScreensDir is null ? "" : $", screens in {ScreensDir}")}; {session.Stage.Id} from {before} to {after} in the {theme} theme{(game.Skinned ? ", Nocturne-styled interface" : "")}";
    }

    /// <summary>
    /// From help over the pause menu: quits to the title, opens stage select's Classic: Bonus tab, starts the stage it
    /// focuses, takes a step and saves <c>classic-bonus-play.png</c>. Only on a data folder that has the classic campaigns.
    /// </summary>
    private IEnumerable<Keys[]> ClassicBonus(CommunityGame game)
    {
        // Esc closes help; Quit to Title is two below Help. The title's focus stays on Play; one down is Stage Select.
        foreach (var tick in Press(Keys.Escape, Keys.Down, Keys.Down, Keys.Enter, Keys.Down, Keys.Enter))
        {
            yield return tick;
        }

        for (var tab = 0; tab < 16 && game.CurrentScreen == Screen.StageSelect && !game.ImportTabOpen && game.SelectPack?.PackId != ClassicStages.BonusPackId; tab++)
        {
            foreach (var tick in Press(Keys.Right))
            {
                yield return tick;
            }
        }

        if (game.SelectPack?.PackId != ClassicStages.BonusPackId)
        {
            yield break;
        }

        foreach (var tick in Press(Keys.Enter))
        {
            yield return tick;
        }

        while (game.Session is { } waiting && waiting.Ticks <= CommunityGame.StartLock)
        {
            yield return [];
        }

        if (game.CurrentScreen != Screen.Playing || game.Session is not { } session || !session.Stage.Id.StartsWith(ClassicStages.BonusPackId, StringComparison.Ordinal))
        {
            Fail(game, "the Classic: Bonus tab didn't start a bonus stage");
            yield break;
        }

        var screen = Enumerable.Range(0, 4).FirstOrDefault(s => new Board(session.Stage.Stage).Step(game.Camera.DirectionFor(s)) != StepResult.Refused, -1);
        while (screen >= 0 && session.Moves == 0)
        {
            yield return [new[] { Keys.Up, Keys.Right, Keys.Down, Keys.Left }[screen]];
        }

        while (session.Animation is not null || session.Ticks < CommunityGame.IntroTicks)
        {
            yield return [];
        }

        foreach (var tick in Shot(null, "classic-bonus-play.png"))
        {
            yield return tick;
        }

        Console.WriteLine($"smoke gui: {session.Stage.Id} took {session.Moves} step(s) in the {(game.PlaysClassic ? "Classic" : "Default")} theme");
    }

    /// <summary>
    /// With the title's focus on the Level Editor: saves a sample stage (a bundled one with blocks, a swivel and holes)
    /// as the player's own, opens the editor and saves its list, opens the sample, splits one of its block groups through
    /// the keyboard, jumps to the problem that makes and saves the canvas. Then it checks that P won't play it, undoes
    /// the split, playtests and comes back with the stage, cursor and undo history kept, runs Solve, and closes it.
    /// </summary>
    private IEnumerable<Keys[]> Editor(CommunityGame game)
    {
        var sample = game.Manifest.Packs.SelectMany(p => p.Stages).First(s =>
        {
            var tiles = Enumerable.Range(0, Stage.Width * Stage.Height).Select(i => Tile.FromCell(s.Stage[i % Stage.Width, i / Stage.Width])).ToList();
            return tiles.Any(t => t.Kind == CellKind.Block) && tiles.Any(t => t.Kind == CellKind.SwivelCenter) && tiles.Any(t => t.OverHole || t.Kind == CellKind.Hole);
        });
        Community.Save.CustomStages.Write(game.Save.Folder, "sample", new StageDocument(sample.Stage, $"{sample.Title} (sample)"));

        foreach (var tick in Press(Keys.Enter))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Editor || game.CustomList.Count != 1)
        {
            Fail(game, $"the Level Editor opened {game.CurrentScreen} with {game.CustomList.Count} stages, not its list with the sample");
            yield break;
        }

        // Down to the sample; Enter, Enter is Edit.
        foreach (var tick in Press(Keys.Down).Concat(Shot(null, "editor-list.png")).Concat(Press(Keys.Enter, Keys.Enter)))
        {
            yield return tick;
        }

        if (game.Editor is not { } editor || editor.Problems.Count != 0)
        {
            Fail(game, "the sample didn't open in the editor, or opened with problems");
            yield break;
        }

        // A floor cell beside none of a block group's cells, after its first in row order so the engine names the new
        // cell as the one cut off: a block of that group there splits it.
        var group = Enumerable.Range(1, StageEditor.MaxGroup).First(g => editor.CellsOf(g) is [var first, ..] && editor[first.X, first.Y].Kind == CellKind.Block);
        var cells = editor.CellsOf(group);
        var after = (cells[0].Y * Stage.Width) + cells[0].X + 1;
        var target = Enumerable.Range(after, (Stage.Width * Stage.Height) - after).Select(i => (X: i % Stage.Width, Y: i / Stage.Width)).First(c =>
            editor[c.X, c.Y].Kind == CellKind.Floor && editor.Goal != c && !editor.Starts.Contains(c)
            && cells.All(b => Math.Abs(b.X - c.X) + Math.Abs(b.Y - c.Y) > 1));
        foreach (var tick in MoveCursor(editor, cells[0]).Concat(Press(Keys.G)).Concat(MoveCursor(editor, target)).Concat(Press(Keys.Space, Keys.Tab)))
        {
            yield return tick;
        }

        if (!editor.HasProblemAt(target.X, target.Y) || editor.Cursor != target)
        {
            Fail(game, $"painting group {group} at {target} didn't show a problem there");
            yield break;
        }

        foreach (var tick in Shot(null, "editor.png"))
        {
            yield return tick;
        }

        // What the window's close button does. The split is unsaved, so the game stays and asks; Esc keeps editing.
        game.Exit();
        yield return [];
        if (!game.AskingToQuit || game.Editor != editor)
        {
            Fail(game, "closing the window with unsaved changes didn't ask first");
            yield break;
        }

        foreach (var tick in Shot(null, "editor-quit.png").Concat(Press(Keys.Escape)))
        {
            yield return tick;
        }

        if (game.AskingToQuit || game.CurrentScreen != Screen.Editor)
        {
            Fail(game, "Keep Editing didn't return to the stage");
            yield break;
        }

        // P refuses a stage with a problem. Ctrl+Z takes the stroke back, and then P plays it.
        foreach (var tick in Press(Keys.P))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Editor)
        {
            Fail(game, "a stage with a problem was playtested");
            yield break;
        }

        foreach (var tick in Chord(Keys.LeftControl, Keys.Z).Concat(Press(Keys.P)))
        {
            yield return tick;
        }

        if (editor.Problems.Count != 0 || !editor.CanRedo || game.CurrentScreen != Screen.Playing || game.Session is not { } playtest)
        {
            Fail(game, "Ctrl+Z and P didn't playtest the sample");
            yield break;
        }

        var kept = (editor.Version, editor.Cursor);
        while (playtest.Ticks <= CommunityGame.StartLock)
        {
            yield return [];
        }

        foreach (var tick in Shot(null, "playtest.png").Concat(Press(Keys.Escape)))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Editor || game.Editor != editor || (editor.Version, editor.Cursor) != kept || !editor.CanRedo)
        {
            Fail(game, "Esc from the playtest didn't come back to the same stage, cursor and undo history");
            yield break;
        }

        // Solve is the menu's fifth item; the capped search runs in the background.
        foreach (var tick in Press(Keys.Escape, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter))
        {
            yield return tick;
        }

        while (editor.Message is null || editor.Message.StartsWith("Solving", StringComparison.Ordinal))
        {
            yield return [];
        }

        foreach (var tick in Shot(null, "editor-solve.png"))
        {
            yield return tick;
        }

        // Close Stage is the menu's last item; the undo left nothing unsaved, so it closes without asking.
        foreach (var tick in Press(Keys.Escape, Keys.Up, Keys.Enter))
        {
            yield return tick;
        }

        if (game.Editor is not null || game.CurrentScreen != Screen.Editor)
        {
            Fail(game, "closing the sample didn't return to the editor's list");
            yield break;
        }

        // The list keeps the sample focused. Export as Pack is its third action; the panel opens on Export.
        var zip = Path.Combine(Path.GetFullPath(Community.Save.PackArchive.ExportsFolder(game.Save.Folder)), "shared-stages.zip");
        foreach (var tick in Press(Keys.Enter, Keys.Down, Keys.Down, Keys.Enter, Keys.Enter))
        {
            yield return tick;
        }

        if (!File.Exists(zip))
        {
            Fail(game, $"Export as Pack didn't write {zip}");
            yield break;
        }

        foreach (var tick in Shot(null, "export.png").Concat(Press(Keys.Escape, Keys.Escape)))
        {
            yield return tick;
        }
    }

    /// <summary>
    /// On stage select's My Stages tab: puts the pack the editor visit exported in the imports folder, opens the Import
    /// tab, imports it (keeping both if a pack of that id is already there), and starts its first stage. It ends back on
    /// the title, with Stage Select focused.
    /// </summary>
    private IEnumerable<Keys[]> Import(CommunityGame game)
    {
        var imports = Community.Save.PackArchive.ImportsFolder(game.Save.Folder);
        Directory.CreateDirectory(imports);
        File.Copy(Path.Combine(Community.Save.PackArchive.ExportsFolder(game.Save.Folder), "shared-stages.zip"), Path.Combine(imports, "shared-stages.zip"), overwrite: true);
        foreach (var tick in Press(Keys.Right))
        {
            yield return tick;
        }

        if (!game.ImportTabOpen)
        {
            Fail(game, "Right from My Stages didn't open the Import tab");
            yield break;
        }

        var before = game.ImportedPacks.Count;
        foreach (var tick in Shot(null, "import.png").Concat(Press(Keys.Enter)))
        {
            yield return tick;
        }

        if (game.ImportConflict)
        {
            foreach (var tick in Shot(null, "import-conflict.png").Concat(Press(Keys.Down, Keys.Enter)))
            {
                yield return tick;
            }
        }

        if (game.ImportedPacks.Count != before + 1 || game.SelectPack is not { } pack || !game.ImportedPacks.Contains(pack))
        {
            Fail(game, "importing the exported pack didn't add its tab");
            yield break;
        }

        foreach (var tick in Shot(null, "imported.png").Concat(Press(Keys.Enter)))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Playing || game.Session is not { } session || session.Stage.Id != pack.Stages[0].Id)
        {
            Fail(game, "the imported pack's first stage didn't start");
            yield break;
        }

        while (session.Ticks <= CommunityGame.StartLock)
        {
            yield return [];
        }

        // Up from Resume wraps to Quit to Title.
        foreach (var tick in Shot(null, "imported-play.png").Concat(Press(Keys.Escape, Keys.Up, Keys.Enter)))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.Title)
        {
            Fail(game, "Quit to Title from the imported stage didn't reach the title");
        }
    }

    /// <summary>Holds <paramref name="keys"/> together for one tick, then releases them.</summary>
    private static IEnumerable<Keys[]> Chord(params Keys[] keys)
    {
        yield return keys;
        yield return [];
    }

    /// <summary>Presses the arrows that take the editor's cursor to <paramref name="to"/>; up is +y.</summary>
    private static IEnumerable<Keys[]> MoveCursor(StageEditor editor, (int X, int Y) to)
    {
        // An iterator, so the cursor is read when the presses start, not when the script is put together.
        var (dx, dy) = (to.X - editor.Cursor.X, to.Y - editor.Cursor.Y);
        foreach (var tick in Press([.. Enumerable.Repeat(dx > 0 ? Keys.Right : Keys.Left, Math.Abs(dx)), .. Enumerable.Repeat(dy > 0 ? Keys.Up : Keys.Down, Math.Abs(dy))]))
        {
            yield return tick;
        }
    }

    /// <summary>
    /// From the title, presses <paramref name="moves"/> and Enter, saves the screen it opens, runs <paramref name="there"/>
    /// if given, and goes back.
    /// </summary>
    private IEnumerable<Keys[]> Visit(CommunityGame game, Keys[] moves, Screen expected, string file, IEnumerable<Keys[]>? there = null)
    {
        foreach (var tick in Press([.. moves, Keys.Enter]))
        {
            yield return tick;
        }

        if (game.CurrentScreen != expected)
        {
            Fail(game, $"the title opened {game.CurrentScreen}, not {expected}");
            yield break;
        }

        foreach (var tick in Shot(null, file))
        {
            yield return tick;
        }

        foreach (var tick in there ?? [])
        {
            yield return tick;
        }

        foreach (var tick in Press(Keys.Escape))
        {
            yield return tick;
        }
    }

    /// <summary>
    /// On the settings screen with Display focused, turns fullscreen on and off again through the menu, letting the window
    /// settle each time, and fails unless both took.
    /// </summary>
    private IEnumerable<Keys[]> ToggleFullscreen(CommunityGame game)
    {
        foreach (var on in new[] { true, false })
        {
            foreach (var tick in Press(Keys.Enter).Concat(Enumerable.Repeat(Array.Empty<Keys>(), 15)))
            {
                yield return tick;
            }

            if (game.Save.Data.Settings.Fullscreen != on || game.CurrentScreen != Screen.Settings)
            {
                Fail(game, $"turning fullscreen {(on ? "on" : "off")} from settings didn't take");
                yield break;
            }
        }
    }

    /// <summary>
    /// On the settings screen with Display focused, opens Classic Nocturne, saves it and goes back to settings. It never
    /// chooses an import.
    /// </summary>
    private IEnumerable<Keys[]> ImportClassic(CommunityGame game)
    {
        foreach (var tick in Press([.. Enumerable.Repeat(Keys.Down, SettingsMenu.ImportRow), Keys.Enter]))
        {
            yield return tick;
        }

        if (game.CurrentScreen != Screen.ImportClassic)
        {
            Fail(game, $"settings' Classic Nocturne opened {game.CurrentScreen}");
            yield break;
        }

        foreach (var tick in Shot(null, "import-classic.png").Concat(Press(Keys.Escape)))
        {
            yield return tick;
        }
    }

    /// <summary>Presses each key for one tick, with a tick released after it so the next press counts.</summary>
    private static IEnumerable<Keys[]> Press(params Keys[] keys)
    {
        foreach (var key in keys)
        {
            yield return [key];
            yield return [];
        }
    }

    /// <summary>Waits a few frames for the screen to settle, then saves it to <paramref name="path"/> and the screens folder.</summary>
    private IEnumerable<Keys[]> Shot(string? path, string file)
    {
        var settle = _frames + 3;
        while (_frames < settle)
        {
            yield return [];
        }

        if (path is not null)
        {
            _pending.Add(path);
        }

        if (ScreensDir is not null)
        {
            _pending.Add(Path.Combine(ScreensDir, file));
        }

        // The next frame drawn saves them.
        var drawn = _frames;
        while (_frames == drawn)
        {
            yield return [];
        }
    }

    private void Fail(CommunityGame game, string reason)
    {
        if (_done)
        {
            return;
        }

        Failure = reason;
        _done = true;
        Console.Error.WriteLine($"smoke gui failed: {reason}");
        game.Exit();
    }

    private static void Save(GraphicsDevice device, string path)
    {
        var pp = device.PresentationParameters;
        var pixels = new Microsoft.Xna.Framework.Color[pp.BackBufferWidth * pp.BackBufferHeight];
        device.GetBackBufferData(pixels);

        // The back buffer's alpha is whatever blending left there; a screenshot is opaque.
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = 255;
        }

        using var texture = new Texture2D(device, pp.BackBufferWidth, pp.BackBufferHeight);
        texture.SetData(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        texture.SaveAsPng(file, pp.BackBufferWidth, pp.BackBufferHeight);
    }
}
