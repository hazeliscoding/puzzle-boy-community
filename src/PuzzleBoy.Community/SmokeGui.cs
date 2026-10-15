using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// <c>--smoke-gui &lt;png&gt;</c>: drives the real window from the title into stage 1 through the same menu and play code a
/// player uses, and saves what it drew. It proves a build opens a window, renders and plays. With a screens folder
/// (<c>--smoke-gui-screens</c>) it also visits stage select, settings (turning fullscreen on and off there), credits, the
/// pause menu and help on the way and saves each.
/// </summary>
/// <param name="titlePng">Where to write the title screenshot; the stage one goes beside it with a <c>-stage</c> suffix.</param>
/// <param name="screensDir">
/// A folder for <c>title.png</c>, <c>stage-select.png</c>, <c>settings.png</c>, <c>credits.png</c>, <c>playing.png</c>,
/// <c>pause.png</c> and <c>help.png</c>, or
/// null to save only the two screenshots.
/// </param>
internal sealed class SmokeGui(string titlePng, string? screensDir = null)
{
    // A slow first frame or a window still settling must not end the run; a stuck one must.
    private const int TimeoutTicks = 900;

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

            foreach (var tick in Visit(game, [Keys.Down, Keys.Down], Screen.Settings, "settings.png", ToggleFullscreen(game)))
            {
                yield return tick;
            }

            foreach (var tick in Visit(game, [Keys.Down], Screen.Credits, "credits.png"))
            {
                yield return tick;
            }

            foreach (var tick in Press(Keys.Up, Keys.Up, Keys.Up, Keys.Up))
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

            // Help is the pause menu's fourth item.
            foreach (var tick in Press(Keys.Down, Keys.Down, Keys.Down, Keys.Enter))
            {
                yield return tick;
            }

            foreach (var tick in Shot(null, "help.png"))
            {
                yield return tick;
            }
        }

        _done = true;
        _passed = $"{TitlePng}, {StagePng}{(ScreensDir is null ? "" : $", screens in {ScreensDir}")}; {session.Stage.Id} from {before} to {after}";
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
