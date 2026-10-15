using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Play;

/// <summary>What a tick of play did that the game should show or play a sound for.</summary>
[Flags]
internal enum PlayEvents
{
    /// <summary>Nothing worth a sound.</summary>
    None = 0,

    /// <summary>A pushed block started to sink into the holes under it.</summary>
    FallStarted = 1,

    /// <summary>A character landed on the goal.</summary>
    ReachedGoal = 2,

    /// <summary>The stage was cleared and its celebration starts.</summary>
    Cleared = 4,
}

/// <summary>
/// A stage of a pack in play: the engine's board plus what the game adds around it, the step animation, the move
/// count, the undo count and the clear celebration. Every rule is the engine's.
/// </summary>
internal sealed class PlaySession
{
    /// <summary>Ticks the clear celebration lasts before the game moves on.</summary>
    public const int CelebrationTicks = 66;

    // The engine keeps the last 30 pushes and turns.
    private const int UndoLimit = 30;

    private readonly IReadOnlyList<PackStage> _stages;

    // The move count at each push or turn the board can still undo, oldest first, mirroring the engine's history.
    private readonly List<int> _undoMoves = [];

    // The play a restart threw away, which an undo with nothing left since brings back; null when there's none.
    private (Board Board, int Moves, int[] UndoMoves, int ElapsedTicks, Direction?[] Facings)? _beforeRestart;

    /// <summary>Starts stage <paramref name="index"/> of <paramref name="stages"/>.</summary>
    public PlaySession(IReadOnlyList<PackStage> stages, int index)
    {
        _stages = stages;
        Index = index;
        Board = new Board(Stage.Stage);
        Facings = new Direction?[Board.Characters.Count];
    }

    /// <summary>The stage's place in its list.</summary>
    public int Index { get; private set; }

    /// <summary>How many stages the list holds.</summary>
    public int Count => _stages.Count;

    /// <summary>The stage in play.</summary>
    public PackStage Stage => _stages[Index];

    /// <summary>Stage <paramref name="index"/> of the list.</summary>
    public PackStage StageAt(int index) => _stages[index];

    /// <summary>The board, which applies every rule.</summary>
    public Board Board { get; private set; }

    /// <summary>The step being drawn, or null when the characters stand still.</summary>
    public MoveAnimation? Animation { get; private set; }

    /// <summary>Steps taken since the stage started; an undo returns it to what it was before the undone push or turn.</summary>
    public int Moves { get; private set; }

    /// <summary>How many pushes and turns can still be undone, at most 30, plus one for a restart that can be.</summary>
    public int UndoAvailable => _undoMoves.Count + (_beforeRestart is null ? 0 : 1);

    /// <summary>Ticks since the stage started or restarted.</summary>
    public int Ticks { get; private set; }

    /// <summary>Ticks of play since the stage started or restarted, stopped at the clear: the stage's time, at 30 a second.</summary>
    public int ElapsedTicks { get; private set; }

    /// <summary>Ticks since the stage was cleared, or 0 before.</summary>
    public int ClearTicks { get; private set; }

    /// <summary>The way each character last tried to step, or null before its first input.</summary>
    public Direction?[] Facings { get; private set; }

    /// <summary>True when a switch would hand control to another character.</summary>
    public bool CanSwitch => !Board.IsCleared && Board.Characters.Count(c => c.Visible) > 1;

    /// <summary>True when another stage follows this one in the list.</summary>
    public bool HasNext => Index + 1 < _stages.Count;

    /// <summary>True once the celebration has run and the game should move on.</summary>
    public bool ReadyToContinue => Board.IsCleared && ClearTicks >= CelebrationTicks;

    /// <summary>True while a step plays or the stage is cleared, when steps wait.</summary>
    public bool Busy => Animation is not null || Board.IsCleared;

    /// <summary>
    /// Steps the active character in <paramref name="direction"/> unless <see cref="Busy"/>. The character turns to
    /// face the input even when the step is refused.
    /// </summary>
    public StepResult Step(Direction direction)
    {
        if (Busy)
        {
            return StepResult.Refused;
        }

        var me = Board.Active;
        Facings[Board.ActiveIndex] = direction;
        var result = Board.Step(direction);
        if (result == StepResult.Refused)
        {
            return result;
        }

        if (result != StepResult.Walked)
        {
            _undoMoves.Add(Moves);
            if (_undoMoves.Count > UndoLimit)
            {
                _undoMoves.RemoveAt(0);
            }
        }

        Moves++;
        Animation = new MoveAnimation(Board.LastMove!, (me.X, me.Y));
        if (Board.IsCleared)
        {
            // The engine forgets its history on a clear.
            _undoMoves.Clear();
        }

        return result;
    }

    /// <summary>
    /// Takes back the last push or turn and any walking since, or, with none left since a restart, the restart itself.
    /// False when there's nothing to undo.
    /// </summary>
    public bool Undo()
    {
        if (Board.IsCleared)
        {
            return false;
        }

        if (Board.Undo())
        {
            Moves = _undoMoves[^1];
            _undoMoves.RemoveAt(_undoMoves.Count - 1);
        }
        else if (_beforeRestart is { } before)
        {
            // The old board keeps its own history, so undo goes on from there. The clock counts both stretches.
            (Board, Moves, ElapsedTicks, Facings) = (before.Board, before.Moves, before.ElapsedTicks + ElapsedTicks, before.Facings);
            _undoMoves.Clear();
            _undoMoves.AddRange(before.UndoMoves);
            _beforeRestart = null;
        }
        else
        {
            return false;
        }

        Animation = null;
        return true;
    }

    /// <summary>Hands control to the next visible character. False, changing nothing, unless <see cref="CanSwitch"/>.</summary>
    public bool Switch() => CanSwitch && Animation is null && Board.Switch();

    /// <summary>
    /// Starts the stage again. Restart sits a key away from the turn keys, so <see cref="Undo"/> can take it back; a
    /// restart with no step since keeps the earlier one undoable.
    /// </summary>
    public void Restart()
    {
        if (Moves > 0)
        {
            _beforeRestart = (Board, Moves, [.. _undoMoves], ElapsedTicks, Facings);
        }

        Board = new Board(Stage.Stage);
        Reset();
    }

    /// <summary>Moves to the next stage in the list. False, changing nothing, on the last.</summary>
    public bool Next()
    {
        if (!HasNext)
        {
            return false;
        }

        Index++;
        Board = new Board(Stage.Stage);
        _beforeRestart = null;
        Reset();
        return true;
    }

    /// <summary>Advances the step animation and the celebration by one tick.</summary>
    public PlayEvents Tick()
    {
        var events = PlayEvents.None;
        Ticks++;
        if (ClearTicks == 0)
        {
            ElapsedTicks++;
        }

        if (Animation is { } animation)
        {
            animation.Tick();
            if (animation.FallStarts)
            {
                events |= PlayEvents.FallStarted;
            }

            if (animation.Lands && animation.Destination == Board.Goal)
            {
                events |= PlayEvents.ReachedGoal;
            }

            if (animation.Done)
            {
                Animation = null;
            }
        }

        // The celebration waits for the last step to land.
        if (Board.IsCleared && Animation is null && ++ClearTicks == 1)
        {
            events |= PlayEvents.Cleared;
        }

        return events;
    }

    /// <summary>Where character <paramref name="index"/> is drawn, in cells, and how high it hops: it slides while its step plays.</summary>
    public (float X, float Y, float Hop) Where(int index)
    {
        var c = Board.Characters[index];
        if (Animation is not { } a || a.Move.Mover != index)
        {
            return (c.X, c.Y, 0);
        }

        var slide = a.Slide;
        var (tx, ty) = a.Destination;
        var hop = MathF.Sin(slide * MathF.PI) * (a.Move.Cells > 1 ? 0.3f : 0.18f);
        return (a.From.X + ((tx - a.From.X) * slide), a.From.Y + ((ty - a.From.Y) * slide), hop);
    }

    private void Reset()
    {
        _undoMoves.Clear();
        Animation = null;
        Moves = 0;
        Ticks = 0;
        ElapsedTicks = 0;
        ClearTicks = 0;
        Facings = new Direction?[Board.Characters.Count];
    }
}
