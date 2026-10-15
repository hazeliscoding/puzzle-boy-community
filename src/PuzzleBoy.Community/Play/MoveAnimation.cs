using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Play;

/// <summary>
/// Plays one step. The board has already changed; this only says how far along the picture is. Age 0 is the tick that
/// took the input. A walk takes 4 ticks, a push or turn 5, and a push that drops a block 9.
/// </summary>
/// <param name="move">The step to play.</param>
/// <param name="from">Where the moving character started.</param>
internal sealed class MoveAnimation(Move move, (int X, int Y) from)
{
    private const int SinkTicks = 4;

    /// <summary>The step being played.</summary>
    public Move Move { get; } = move;

    /// <summary>Where the moving character started.</summary>
    public (int X, int Y) From { get; } = from;

    /// <summary>Ticks since the input tick.</summary>
    public int Age { get; private set; }

    /// <summary>How many ticks the slide takes: 4 for a walk, 5 for a push or turn.</summary>
    public int SlideTicks => Move.Result == StepResult.Walked ? 4 : 5;

    /// <summary>How much of the slide shows, from 1/<see cref="SlideTicks"/> on the input tick to 1.</summary>
    public float Slide => Math.Min(Age + 1, SlideTicks) / (float)SlideTicks;

    /// <summary>How far a dropped block has sunk, 0 to 1 cell, over the ticks after the slide.</summary>
    public float Sink => Move.Fell ? Math.Clamp((Age + 1 - SlideTicks) / (float)SinkTicks, 0, 1) : 0;

    /// <summary>True while a dropped block still shows, so the holes under it still look open.</summary>
    public bool Falling => Move.Fell && !Done;

    /// <summary>True on the tick a dropped block starts to sink.</summary>
    public bool FallStarts => Move.Fell && Age == SlideTicks;

    /// <summary>True on the last tick of the slide, when the character lands.</summary>
    public bool Lands => Age == SlideTicks - 1;

    /// <summary>True once the next input may be read.</summary>
    public bool Done => Age >= SlideTicks + (Move.Fell ? SinkTicks : 0);

    /// <summary>The cell the moving character ends on.</summary>
    public (int X, int Y) Destination
    {
        get
        {
            var (dx, dy) = Move.Direction.Offset();
            return (From.X + (dx * Move.Cells), From.Y + (dy * Move.Cells));
        }
    }

    /// <summary>Advances one tick.</summary>
    public void Tick() => Age++;

    /// <summary>
    /// Where a moving group cell drawn at <paramref name="landing"/> is now, in cells, and its turn so far in quarter
    /// turns (0 for a push): it slides or swings from where it was into its landing.
    /// </summary>
    public (float X, float Y, float Turn) PartAt((int X, int Y) landing)
    {
        var left = 1 - Slide;
        if (Move.Result == StepResult.Pushed)
        {
            var (dx, dy) = Move.Direction.Offset();
            return (landing.X - (dx * left), landing.Y - (dy * left), 0);
        }

        // The engine's turn of +1 is a quarter turn counterclockwise with y up; swing back by the part not yet shown.
        var angle = -Move.Turn * left * MathF.PI / 2;
        var (ox, oy) = (landing.X - Move.Center.X, landing.Y - Move.Center.Y);
        var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
        return (Move.Center.X + (ox * cos) - (oy * sin), Move.Center.Y + (ox * sin) + (oy * cos), -Move.Turn * left);
    }
}
