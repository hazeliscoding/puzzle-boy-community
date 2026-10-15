using System.Buffers.Binary;
using System.Security.Cryptography;
using PuzzleBoy.Rules;

namespace PuzzleBoy.StageCheck;

/// <summary>How a search ended.</summary>
public enum SolveOutcome
{
    /// <summary>A shortest solution was found.</summary>
    Solved,

    /// <summary>The search visited its cap of states first. The stage may still have a solution.</summary>
    LimitReached,

    /// <summary>Every reachable state was visited and none clears the stage.</summary>
    Unsolvable,
}

/// <summary>The result of <see cref="Solver.Solve"/>.</summary>
/// <param name="Outcome">How the search ended.</param>
/// <param name="Moves">
/// For <see cref="SolveOutcome.Solved"/>, the inputs: <c>R</c> +x, <c>L</c> −x, <c>D</c> +y, <c>U</c> −y, <c>S</c> switch.
/// Null otherwise.
/// </param>
/// <param name="States">How many distinct states the search visited.</param>
public sealed record SolveResult(SolveOutcome Outcome, string? Moves, int States);

/// <summary>
/// A breadth-first search over a stage's states with the engine's <see cref="Board"/> making every move, so a solution
/// is exactly what the game accepts. Each input, a step or a switch, costs one, so a solution has the fewest inputs.
/// </summary>
public static class Solver
{
    /// <summary>The default cap on visited states.</summary>
    public const int DefaultCap = 2_000_000;

    private const string Inputs = "RLDUS";
    private static readonly Direction[] Steps = [Direction.PlusX, Direction.MinusX, Direction.PlusY, Direction.MinusY];

    /// <summary>Searches <paramref name="stage"/> for a shortest solution, visiting at most <paramref name="cap"/> states.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is less than 1.</exception>
    public static SolveResult Solve(Stage stage, int cap = DefaultCap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);

        // Walls never change, so a state only records the other cells.
        var open = new List<int>();
        for (var i = 0; i < Stage.Width * Stage.Height; i++)
        {
            if (Cell.KindOf(stage[i % Stage.Width, i / Stage.Width]) != CellKind.Wall)
            {
                open.Add(i);
            }
        }

        var start = new Board(stage);
        var startState = Encode(start, open);

        // shortcut: states are deduplicated by 128 bits of SHA-256, so a collision could hide one; the odds at the cap are
        // about 1e-26. Store whole states if a stage ever needs exact proof.
        var seen = new HashSet<UInt128> { Key(startState) };
        var parents = new List<(int Parent, byte Input)> { (-1, 0) };
        var queue = new Queue<(int Index, byte[] State)>();
        queue.Enqueue((0, startState));
        while (queue.TryDequeue(out var node))
        {
            var board = new Board(Decode(stage, open, node.State));
            for (byte input = 0; input < Inputs.Length; input++)
            {
                board.Retry();
                var moved = input < Steps.Length ? board.Step(Steps[input]) != StepResult.Refused : board.Switch();
                if (!moved)
                {
                    continue;
                }

                if (board.IsCleared)
                {
                    parents.Add((node.Index, input));
                    return new SolveResult(SolveOutcome.Solved, Path(parents, parents.Count - 1), seen.Count);
                }

                var state = Encode(board, open);
                if (!seen.Add(Key(state)))
                {
                    continue;
                }

                if (seen.Count > cap)
                {
                    return new SolveResult(SolveOutcome.LimitReached, null, cap);
                }

                parents.Add((node.Index, input));
                queue.Enqueue((parents.Count - 1, state));
            }
        }

        return new SolveResult(SolveOutcome.Unsolvable, null, seen.Count);
    }

    /// <summary>
    /// A stage that starts where <paramref name="board"/> is now: the same cells, and the visible characters with the
    /// active one first, so a new <see cref="Board"/> on it plays on exactly as <paramref name="board"/> would. The undo
    /// history isn't kept.
    /// </summary>
    /// <param name="stage">The stage <paramref name="board"/> was started from.</param>
    /// <param name="board">A board that isn't cleared.</param>
    public static Stage Freeze(Stage stage, Board board)
    {
        var open = Enumerable.Range(0, Stage.Width * Stage.Height).ToList();
        return Decode(stage, open, Encode(board, open));
    }

    /// <summary>The step a move letter stands for: <c>R</c>, <c>L</c>, <c>D</c> or <c>U</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="move"/> is another letter, such as <c>S</c>.</exception>
    public static Direction DirectionOf(char move) => Inputs.IndexOf(move) is >= 0 and < 4 and var i
        ? Steps[i]
        : throw new ArgumentOutOfRangeException(nameof(move), move, "A step is R, L, D or U.");

    private static string Path(List<(int Parent, byte Input)> parents, int index)
    {
        var inputs = new List<char>();
        for (; parents[index].Parent >= 0; index = parents[index].Parent)
        {
            inputs.Add(Inputs[parents[index].Input]);
        }

        inputs.Reverse();
        return new string([.. inputs]);
    }

    private static UInt128 Key(byte[] state) => BinaryPrimitives.ReadUInt128LittleEndian(SHA256.HashData(state));

    /// <summary>
    /// The open cells, then the visible characters starting with the active one. Control passes in that cyclic order,
    /// so it's all a board needs, and characters that left at the goal drop out.
    /// </summary>
    private static byte[] Encode(Board board, List<int> open)
    {
        var visible = Enumerable.Range(0, board.Characters.Count)
            .Select(i => board.Characters[(board.ActiveIndex + i) % board.Characters.Count])
            .Where(c => c.Visible).ToList();
        var state = new byte[(open.Count * 2) + (visible.Count * 2)];
        for (var i = 0; i < open.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(i * 2), board[open[i] % Stage.Width, open[i] / Stage.Width]);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            state[(open.Count * 2) + (i * 2)] = (byte)visible[i].X;
            state[(open.Count * 2) + (i * 2) + 1] = (byte)visible[i].Y;
        }

        return state;
    }

    /// <summary>A stage whose start is <paramref name="state"/>, so a new <see cref="Board"/> resumes from it.</summary>
    private static Stage Decode(Stage stage, List<int> open, byte[] state)
    {
        var file = stage.ToFile();
        for (var i = 0; i < open.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28 + (open[i] * 2)), BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(i * 2)));
        }

        // The 828-byte layout Stage.ToFile writes: count, enable bytes and starts at 0, the goal at 24, the grid at 28.
        // The active character goes in slot 0.
        var characters = (state.Length - (open.Count * 2)) / 2;
        file.AsSpan(0, 24).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(file, characters);
        for (var i = 0; i < characters; i++)
        {
            file[4 + i] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(8 + (i * 4)), state[(open.Count * 2) + (i * 2)]);
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(10 + (i * 4)), state[(open.Count * 2) + (i * 2) + 1]);
        }

        return Stage.Parse(file);
    }
}
