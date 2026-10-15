using System.Globalization;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>One line of a demo script: keys held for some ticks, or a line of moves played on the stage.</summary>
/// <param name="Keys">The keys held together; empty for a wait. Unused by a play line.</param>
/// <param name="Ticks">How many ticks they're held, at least 1. Unused by a play line.</param>
/// <param name="Moves">For a play line, the moves in the solutions' letters (R L D U S); null otherwise.</param>
internal sealed record DemoStep(Keys[] Keys, int Ticks, string? Moves = null);

/// <summary>
/// <c>--record &lt;script&gt; &lt;out-dir&gt;</c>: plays a demo script through the real window and the same input path a
/// player's keys take, and saves every <c>every</c>th frame drawn as <c>frame-00000.png</c> onward. A recording runs one
/// update and one draw per tick as fast as the machine goes, never by the clock, so a script and data folder always give
/// the same frames, at the game's 30 ticks a second.
/// </summary>
/// <param name="steps">The parsed script (<see cref="Parse"/>).</param>
/// <param name="outDir">The folder for the frames; it's created if missing.</param>
/// <param name="every">Saves one frame in this many, at least 1: 2 gives 15 frames a second.</param>
/// <param name="size">The back buffer's size in pixels.</param>
internal sealed class DemoRecorder(IReadOnlyList<DemoStep> steps, string outDir, int every, (int Width, int Height) size) : IDriver
{
    // A step the board refuses never comes; a few seconds of holding it is enough to say so.
    private const int StepTimeoutTicks = 150;

    private readonly string _outDir = Path.GetFullPath(outDir);
    private IEnumerator<Keys[]>? _script;
    private Keys[] _held = [];
    private int _drawn;
    private int _saved;
    private bool _finished;

    /// <summary>The back buffer's size in pixels.</summary>
    public (int Width, int Height) Size => size;

    /// <inheritdoc />
    public int ExitCode { get; private set; } = 1;

    /// <inheritdoc />
    public bool Done { get; private set; }

    /// <summary>
    /// Reads a script: one command a line, blank lines and <c>#</c> comments skipped. <c>wait N</c> holds nothing for N
    /// ticks; <c>press K [K…]</c> presses each key for a tick and releases it for a tick; <c>hold K[+K…] N</c> holds the keys
    /// together for N ticks; <c>play MOVES</c> plays the solutions' letters on the stage in play (R +x, L −x, D +y, U −y,
    /// S switch), each held until the board takes it and waited out. Keys are MonoGame <see cref="Keys"/> names, in any
    /// case. Throws <see cref="FormatException"/> naming the line for anything else.
    /// </summary>
    public static List<DemoStep> Parse(string text)
    {
        var steps = new List<DemoStep>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var words = lines[i].Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            FormatException Bad(string why) => new($"line {i + 1}: {why}");
            switch (words[0].ToLowerInvariant())
            {
                case "wait" when words.Length == 2:
                    steps.Add(new([], Count(words[1]) ?? throw Bad($"'{words[1]}' isn't a tick count")));
                    break;
                case "press" when words.Length > 1:
                    foreach (var word in words[1..])
                    {
                        steps.Add(new([Key(word) ?? throw Bad($"'{word}' isn't a key")], 1));
                        steps.Add(new([], 1));
                    }

                    break;
                case "hold" when words.Length == 3:
                    var keys = words[1].Split('+').Select(Key).ToArray();
                    steps.Add(new(keys.Contains(null) ? throw Bad($"'{words[1]}' isn't a key or keys joined by +") : [.. keys.Select(k => k!.Value)],
                        Count(words[2]) ?? throw Bad($"'{words[2]}' isn't a tick count")));
                    break;
                case "play" when words.Length == 2 && words[1].All("RLDUS".Contains):
                    steps.Add(new([], 1, words[1]));
                    break;
                default:
                    throw Bad($"expected wait N, press KEY..., hold KEY[+KEY] N or play MOVES, not '{lines[i].Trim()}'");
            }
        }

        return steps;

        static int? Count(string word) => int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

        // Enum.TryParse takes numbers too; a script names its keys.
        static Keys? Key(string word) => !word.All(char.IsAsciiDigit) && Enum.TryParse<Keys>(word, ignoreCase: true, out var key) && Enum.IsDefined(key) ? key : null;
    }

    /// <inheritdoc />
    public Controls Input(CommunityGame game)
    {
        var previous = _held;
        _held = [];
        _script ??= Run(game);
        if (!Done && _script.MoveNext())
        {
            _held = _script.Current;
        }
        else if (!Done)
        {
            Done = _finished = true;
        }

        return Controls.FromKeys(_held, previous);
    }

    /// <inheritdoc />
    public void AfterDraw(CommunityGame game)
    {
        if (_drawn++ % every == 0)
        {
            SmokeGui.Save(game.GraphicsDevice, Path.Combine(_outDir, $"frame-{_saved++:D5}.png"));
        }

        if (_finished && ExitCode != 0)
        {
            ExitCode = 0;
            Console.WriteLine($"recorded {_saved} frames of {_drawn} ticks to {_outDir}");
            game.Exit();
        }
    }

    /// <summary>The script, one tick's held keys at a time.</summary>
    private IEnumerator<Keys[]> Run(CommunityGame game)
    {
        foreach (var step in steps)
        {
            if (step.Moves is null)
            {
                for (var tick = 0; tick < step.Ticks; tick++)
                {
                    yield return step.Keys;
                }

                continue;
            }

            foreach (var move in step.Moves)
            {
                if (game.CurrentScreen != Screen.Playing || game.Session is not { } session)
                {
                    Fail(game, $"play {step.Moves} found no stage in play");
                    yield break;
                }

                // Input waits out a step, the camera's turn and the start of a stage; a key pressed then would be lost.
                while (session.Busy || session.Ticks <= PlayInput.StartLock || game.Camera.IsBusy)
                {
                    yield return [];
                }

                var (moves, switched) = (session.Moves, session.Board.ActiveIndex);
                for (var held = 0; session.Moves == moves && session.Board.ActiveIndex == switched; held++)
                {
                    if (held > StepTimeoutTicks)
                    {
                        Fail(game, $"the board refused {move} in play {step.Moves}");
                        yield break;
                    }

                    yield return move == 'S' ? [Keys.C] : [ScreenKey(game.Camera, move)];
                }
            }
        }
    }

    /// <summary>The arrow that steps toward the solutions' letter <paramref name="move"/> as the camera faces now.</summary>
    private static Keys ScreenKey(CameraRig camera, char move)
    {
        var direction = move switch
        {
            'R' => Direction.PlusX,
            'L' => Direction.MinusX,
            'D' => Direction.PlusY,
            _ => Direction.MinusY,
        };
        return new[] { Keys.Up, Keys.Right, Keys.Down, Keys.Left }[Enumerable.Range(0, 4).First(s => camera.DirectionFor(s) == direction)];
    }

    private void Fail(CommunityGame game, string reason)
    {
        Done = true;
        Console.Error.WriteLine($"record failed: {reason}");
        game.Exit();
    }
}
