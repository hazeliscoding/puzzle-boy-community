using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// <c>--smoke-gui &lt;png&gt;</c>: drives the real window from the title into stage 1 through the same menu and play code a
/// player uses, and saves what it drew. It proves a build opens a window, renders and plays.
/// </summary>
/// <param name="titlePng">Where to write the title screenshot; the stage one goes beside it with a <c>-stage</c> suffix.</param>
internal sealed class SmokeGui(string titlePng)
{
    // A slow first frame or a window still settling must not end the run; a stuck one must.
    private const int TimeoutTicks = 600;

    // Frames drawn on the title before it's saved, so the backdrop and fonts have settled.
    private const int TitleFrames = 5;

    private Phase _phase;
    private int _frames;
    private int _ticks;
    private Keys[] _held = [];
    private (int X, int Y) _before;
    private int _screen = -1;

    private enum Phase
    {
        Title,
        Start,
        Step,
        Settle,
        Done,
    }

    /// <summary>The title screenshot's path.</summary>
    public string TitlePng { get; } = Path.GetFullPath(titlePng);

    /// <summary>The stage screenshot's path: the title's with <c>-stage</c> before the extension.</summary>
    public string StagePng => Path.Combine(Path.GetDirectoryName(TitlePng)!, $"{Path.GetFileNameWithoutExtension(TitlePng)}-stage{Path.GetExtension(TitlePng)}");

    /// <summary>0 once both screenshots are saved and the step changed the board; 1 until then.</summary>
    public int ExitCode { get; private set; } = 1;

    /// <summary>Why the run failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The input for this tick, in place of the real keyboard and pad.</summary>
    public Controls Input(CommunityGame game)
    {
        var previous = _held;
        _held = [];
        if (++_ticks > TimeoutTicks)
        {
            Fail(game, $"timed out in phase {_phase}");
            return Controls.FromKeys([], previous);
        }

        switch (_phase)
        {
            case Phase.Start when game.CurrentScreen == Screen.Title:
                // Play is the title's first item and focused on arrival.
                _held = [Keys.Enter];
                break;
            case Phase.Start when game.CurrentScreen == Screen.Playing && game.Session is { } session:
                _before = (session.Board.Active.X, session.Board.Active.Y);
                _phase = Phase.Step;
                break;
            case Phase.Start:
                Fail(game, $"Play opened {game.CurrentScreen}, not a stage");
                break;
            case Phase.Step when game.Session is { } session:
                // Hold a screen direction the camera maps to a step the first stage allows, until the board takes it.
                if (_screen < 0)
                {
                    _screen = Enumerable.Range(0, 4).FirstOrDefault(s => new Board(session.Stage.Stage).Step(game.Camera.DirectionFor(s)) != StepResult.Refused, -1);
                    if (_screen < 0)
                    {
                        Fail(game, "no step is open on the first stage");
                        break;
                    }
                }

                if (session.Moves > 0)
                {
                    _phase = Phase.Settle;
                    break;
                }

                _held = [new[] { Keys.Up, Keys.Right, Keys.Down, Keys.Left }[_screen]];
                break;
        }

        return Controls.FromKeys(_held, previous);
    }

    /// <summary>Called after each frame is drawn, before it's shown: saves the screenshots when their moment comes.</summary>
    public void AfterDraw(CommunityGame game)
    {
        _frames++;
        if (_phase == Phase.Title && game.CurrentScreen == Screen.Title && _frames >= TitleFrames)
        {
            Save(game.GraphicsDevice, TitlePng);
            _phase = Phase.Start;
        }
        else if (_phase == Phase.Settle && game.Session is { Animation: null } session && session.Ticks >= CommunityGame.IntroTicks)
        {
            var after = (session.Board.Active.X, session.Board.Active.Y);
            if (after == _before)
            {
                Fail(game, "the step didn't move the character");
                return;
            }

            Save(game.GraphicsDevice, StagePng);
            _phase = Phase.Done;
            ExitCode = 0;
            Console.WriteLine($"smoke gui passed: {TitlePng}, {StagePng}; {session.Stage.Id} from {_before} to {after}");
            game.Exit();
        }
    }

    private void Fail(CommunityGame game, string reason)
    {
        Failure = reason;
        _phase = Phase.Done;
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
