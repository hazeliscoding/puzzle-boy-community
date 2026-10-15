using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Editor;

/// <summary>What painting a cell does in the editor.</summary>
internal enum EditorTool
{
    /// <summary>Paints wall, the editor's empty space.</summary>
    Wall,

    /// <summary>Paints floor.</summary>
    Floor,

    /// <summary>Paints a hole.</summary>
    Hole,

    /// <summary>Paints a block cell of the current group.</summary>
    Block,

    /// <summary>Paints the center of the current swivel group.</summary>
    SwivelCenter,

    /// <summary>Paints an arm cell of the current swivel group.</summary>
    SwivelArm,

    /// <summary>Places the goal.</summary>
    Goal,

    /// <summary>Places the current character's start.</summary>
    Start,

    /// <summary>Takes away the top layer of a cell: a start or the goal, then a block or swivel part, then floor or hole.</summary>
    Eraser,
}

/// <summary>
/// A stage being made in the level editor, over the engine's <see cref="StageBuilder"/>: the tools, the current group
/// and start, the cursor, strokes and rectangles, and the engine's validation. Plain data with no drawing, so tests
/// drive it the way the editor screen does. Coordinates are the engine's: x is the column, y the row, both 0 to 19.
/// </summary>
internal sealed class StageEditor
{
    /// <summary>The highest group the editor gives out: the stage JSON format writes groups 1 to 35.</summary>
    public const int MaxGroup = 35;

    /// <summary>The longest title, in characters.</summary>
    public const int MaxTitle = 40;

    /// <summary>The most characters a stage can have.</summary>
    public const int MaxStarts = 4;

    /// <summary>The tools in the order of their number keys, 1 to 9.</summary>
    public static readonly EditorTool[] Tools = Enum.GetValues<EditorTool>();

    // Undo keeps this many changes; an old one falls off the end.
    private const int UndoLimit = 200;

    private readonly HashSet<(int X, int Y)> _strokeCells = [];
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private StageBuilder _stage;
    private IReadOnlyList<StageProblem>? _problems;
    private (int X, int Y) _cursor = (Stage.Width / 2, Stage.Height / 2);
    private Snapshot? _strokeBefore;
    private bool _strokeChanged;

    // Every state the stage has been in has its own number, so undoing back to the saved one makes it clean again.
    private int _change;
    private int _lastChange;
    private int _savedChange;

    /// <summary>Opens <paramref name="stage"/> titled <paramref name="title"/>, or a new stage of all wall.</summary>
    /// <param name="stage">The stage to edit, or null for a new one.</param>
    /// <param name="title">The title, cut to <see cref="MaxTitle"/>.</param>
    /// <param name="name">The custom file it was saved as, or null when it hasn't been.</param>
    public StageEditor(Stage? stage = null, string title = "New Stage", string? name = null)
    {
        _stage = stage is null ? new StageBuilder() : StageBuilder.From(stage);
        Title = Trim(title);
        Name = name;
    }

    /// <summary>The stage's title, at most <see cref="MaxTitle"/> characters and never blank.</summary>
    public string Title { get; private set; }

    /// <summary>The custom file name the stage was last saved as, or null before its first save.</summary>
    public string? Name { get; private set; }

    /// <summary>The tool painting uses.</summary>
    public EditorTool Tool { get; set; } = EditorTool.Floor;

    /// <summary>The group block and swivel tools paint, 1 to <see cref="MaxGroup"/>.</summary>
    public int Group { get; private set; } = 1;

    /// <summary>The character the start tool places, 1 to <see cref="MaxStarts"/>.</summary>
    public int Start { get; private set; } = 1;

    /// <summary>True when block and swivel tools paint parts resting on a hole.</summary>
    public bool OverHole { get; set; }

    /// <summary>The cell the keyboard paints; kept on the grid.</summary>
    public (int X, int Y) Cursor
    {
        get => _cursor;
        set => _cursor = (Math.Clamp(value.X, 0, Stage.Width - 1), Math.Clamp(value.Y, 0, Stage.Height - 1));
    }

    /// <summary>What the last refused action or notable change was about, for the status line; null when there's nothing to say.</summary>
    public string? Message { get; set; }

    /// <summary>True when the stage differs from when it was opened or last saved.</summary>
    public bool Dirty => _change != _savedChange;

    /// <summary>A number for the stage's current state: any change, undo or redo gives another.</summary>
    public int Version => _change;

    /// <summary>True when there's a change to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there's an undone change to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The problem the editor last jumped to, an index into <see cref="Problems"/>, or -1.</summary>
    public int ProblemFocus { get; private set; } = -1;

    /// <summary>The tile at column <paramref name="x"/> and row <paramref name="y"/>.</summary>
    public Tile this[int x, int y] => _stage[x, y];

    /// <summary>Where the goal is, or null before one is placed.</summary>
    public (int X, int Y)? Goal => _stage.Goal;

    /// <summary>The characters' starts; the index is the character, the first starts in control.</summary>
    public IReadOnlyList<(int X, int Y)> Starts => _stage.Characters;

    /// <summary>Everything stopping the stage from being played or saved, from the engine's validation.</summary>
    public IReadOnlyList<StageProblem> Problems => _problems ??= _stage.Validate();

    /// <summary>True when the tool paints a rectangle with <see cref="FillRect"/>: wall, floor and hole.</summary>
    public static bool Fills(EditorTool tool) => tool is EditorTool.Wall or EditorTool.Floor or EditorTool.Hole;

    /// <summary>True when the tool paints a block or swivel part, which has a group.</summary>
    public static bool IsPart(EditorTool tool) => tool is EditorTool.Block or EditorTool.SwivelCenter or EditorTool.SwivelArm;

    /// <summary>The tool's name as the editor shows it.</summary>
    public static string NameOf(EditorTool tool) => tool switch
    {
        EditorTool.SwivelCenter => "Swivel center",
        EditorTool.SwivelArm => "Swivel arm",
        _ => tool.ToString(),
    };

    /// <summary>
    /// What a capped search found, for the status line. Reaching the cap never reads as unsolvable: the stage may still
    /// have a solution the search didn't reach.
    /// </summary>
    /// <param name="result">The search's result.</param>
    /// <param name="cap">The most positions it was allowed to visit.</param>
    public static string SolveMessage(SolveResult result, int cap)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return result.Outcome switch
        {
            SolveOutcome.Solved => string.Create(culture, $"Solved: it clears in {result.Moves!.Length} {(result.Moves.Length == 1 ? "move" : "moves")}."),
            SolveOutcome.LimitReached => string.Create(culture, $"Search limit reached (not proven unsolvable): no solution in the first {cap:N0} positions."),
            _ => string.Create(culture, $"Unsolvable: none of its {result.States:N0} positions clears it."),
        };
    }

    /// <summary>The stage, or null while it has <see cref="Problems"/>. Each call builds a new one, so playing it never changes the editor.</summary>
    public Stage? TryBuild() => Problems.Count == 0 ? _stage.Build() : null;

    /// <summary>The stage as a stage document with its title.</summary>
    /// <exception cref="StageFormatException">The stage has <see cref="Problems"/>.</exception>
    public StageDocument ToDocument() => new(_stage.Build(), Title);

    /// <summary>Records that the stage was saved as <paramref name="name"/>, so it's no longer <see cref="Dirty"/>.</summary>
    public void MarkSaved(string name)
    {
        Name = name;
        _savedChange = _change;
    }

    /// <summary>True when a problem points at cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public bool HasProblemAt(int x, int y) => Problems.Any(p => p.X == x && p.Y == y);

    /// <summary>
    /// Moves <see cref="ProblemFocus"/> <paramref name="by"/> problems, wrapping, and puts the cursor on it when it names a
    /// cell. False when there are no problems.
    /// </summary>
    public bool NextProblem(int by)
    {
        if (Problems.Count == 0)
        {
            ProblemFocus = -1;
            return false;
        }

        var from = ProblemFocus < 0 && by > 0 ? -1 : ProblemFocus;
        return FocusProblem((((from + by) % Problems.Count) + Problems.Count) % Problems.Count);
    }

    /// <summary>Focuses problem <paramref name="index"/> and puts the cursor on its cell, if it names one. False when it's out of range.</summary>
    public bool FocusProblem(int index)
    {
        if (index < 0 || index >= Problems.Count)
        {
            return false;
        }

        ProblemFocus = index;
        if (Problems[index] is { X: { } x, Y: { } y })
        {
            Cursor = (x, y);
        }

        return true;
    }

    /// <summary>
    /// Starts a stroke: every cell painted until <see cref="EndStroke"/> is painted at most once, and the whole stroke
    /// is one change to undo.
    /// </summary>
    public void BeginStroke()
    {
        EndStroke();
        _strokeBefore = Take();
        _strokeChanged = false;
        _strokeCells.Clear();
    }

    /// <summary>Ends the stroke begun with <see cref="BeginStroke"/>, if any.</summary>
    public void EndStroke()
    {
        if (_strokeBefore is not { } before)
        {
            return;
        }

        _strokeBefore = null;
        if (_strokeChanged)
        {
            Changed(before);
        }
    }

    /// <summary>Takes back the last change: a stroke, a rectangle or a title. False when there's none.</summary>
    public bool Undo() => Step(_undo, _redo);

    /// <summary>Puts back the last change <see cref="Undo"/> took back. False when there's none.</summary>
    public bool Redo() => Step(_redo, _undo);

    /// <summary>
    /// Paints cell (<paramref name="x"/>, <paramref name="y"/>) with the tool, once per stroke. Returns true when the cell
    /// changed; false when it didn't, when it was already painted in this stroke, or when the tool refused it, with
    /// <see cref="Message"/> saying why. Outside a stroke it's a stroke of one cell.
    /// </summary>
    public bool PaintAt(int x, int y)
    {
        if (x is < 0 or >= Stage.Width || y is < 0 or >= Stage.Height)
        {
            return false;
        }

        var single = _strokeBefore is null;
        if (single)
        {
            BeginStroke();
        }

        var changed = _strokeCells.Add((x, y)) && Apply(x, y);
        if (changed)
        {
            // The stroke counts as one change when it ends, but what's drawn meanwhile is checked as it goes.
            _strokeChanged = true;
            _problems = null;
        }

        if (single)
        {
            EndStroke();
        }

        return changed;
    }

    /// <summary>
    /// Fills the rectangle with corners <paramref name="a"/> and <paramref name="b"/> using a wall, floor or hole tool.
    /// False, changing nothing, for another tool or when every cell already holds that tile.
    /// </summary>
    public bool FillRect((int X, int Y) a, (int X, int Y) b)
    {
        if (!Fills(Tool))
        {
            Message = "Rectangles fill with wall, floor or hole.";
            return false;
        }

        var tile = new Tile(KindOf(Tool));
        var before = Take();
        var changed = false;
        for (var y = Math.Max(0, Math.Min(a.Y, b.Y)); y <= Math.Min(Stage.Height - 1, Math.Max(a.Y, b.Y)); y++)
        {
            for (var x = Math.Max(0, Math.Min(a.X, b.X)); x <= Math.Min(Stage.Width - 1, Math.Max(a.X, b.X)); x++)
            {
                changed |= _stage[x, y] != tile;
                _stage[x, y] = tile;
            }
        }

        if (changed)
        {
            Changed(before);
        }

        return changed;
    }

    /// <summary>Renames the stage; a blank title is refused. The saved file keeps its name, so its progress stays.</summary>
    public bool SetTitle(string title)
    {
        var trimmed = Trim(title);
        if (string.IsNullOrWhiteSpace(title) || trimmed == Title)
        {
            return false;
        }

        var before = Take();
        Title = trimmed;
        Changed(before);
        return true;
    }

    /// <summary>
    /// Steps the start tool's character, or otherwise the group, <paramref name="by"/>, wrapping within 1 to
    /// <see cref="MaxStarts"/> or 1 to <see cref="MaxGroup"/>.
    /// </summary>
    public void ChangeNumber(int by)
    {
        if (Tool == EditorTool.Start)
        {
            Start = Wrap(Start + by, MaxStarts);
        }
        else
        {
            Group = Wrap(Group + by, MaxGroup);
        }
    }

    /// <summary>
    /// Makes the lowest group no tile uses the current one, so a new block or swivel never joins an existing group by
    /// accident. False, with a <see cref="Message"/>, when all <see cref="MaxGroup"/> are in use.
    /// </summary>
    public bool NewGroup()
    {
        var used = UsedGroups();
        for (var group = 1; group <= MaxGroup; group++)
        {
            if (!used.Contains(group))
            {
                Group = group;
                Message = $"Group {group} is new.";
                return true;
            }
        }

        Message = $"All {MaxGroup} groups are in use.";
        return false;
    }

    /// <summary>
    /// Takes the tool, group and start from cell (<paramref name="x"/>, <paramref name="y"/>): a start picks the start
    /// tool and that character, the goal the goal tool, a part its tool, group and hole setting, any other tile its tool.
    /// </summary>
    public void Pick(int x, int y)
    {
        var start = _stage.Characters.IndexOf((x, y));
        var tile = _stage[x, y];
        if (start >= 0)
        {
            (Tool, Start) = (EditorTool.Start, start + 1);
        }
        else if (_stage.Goal == (x, y))
        {
            Tool = EditorTool.Goal;
        }
        else if (tile.Kind is CellKind.Block or CellKind.SwivelCenter or CellKind.SwivelArm)
        {
            Tool = tile.Kind == CellKind.Block ? EditorTool.Block : tile.Kind == CellKind.SwivelCenter ? EditorTool.SwivelCenter : EditorTool.SwivelArm;
            (Group, OverHole) = (Math.Clamp(tile.Group, 1, MaxGroup), tile.OverHole);
        }
        else
        {
            Tool = tile.Kind switch { CellKind.Floor => EditorTool.Floor, CellKind.Hole => EditorTool.Hole, _ => EditorTool.Wall };
        }
    }

    /// <summary>The smallest box holding every cell that isn't wall, the goal and the starts, or null when there's none.</summary>
    public (int MinX, int MinY, int MaxX, int MaxY)? Bounds()
    {
        var cells = Enumerable.Range(0, Stage.Width * Stage.Height).Select(i => (X: i % Stage.Width, Y: i / Stage.Width))
            .Where(c => _stage[c.X, c.Y].Kind != CellKind.Wall).Concat(_stage.Characters);
        if (_stage.Goal is { } goal)
        {
            cells = cells.Append(goal);
        }

        var list = cells.ToList();
        return list.Count == 0 ? null : (list.Min(c => c.X), list.Min(c => c.Y), list.Max(c => c.X), list.Max(c => c.Y));
    }

    /// <summary>The cells of group <paramref name="group"/>, row by row.</summary>
    public List<(int X, int Y)> CellsOf(int group)
    {
        var cells = new List<(int X, int Y)>();
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                if (_stage[x, y].Group == group && _stage[x, y].Kind is CellKind.Block or CellKind.SwivelCenter or CellKind.SwivelArm)
                {
                    cells.Add((x, y));
                }
            }
        }

        return cells;
    }

    /// <summary>What group <paramref name="group"/> is: "Unused", or "Block" or "Swivel" with its cell count.</summary>
    public string Describe(int group)
    {
        var cells = CellsOf(group);
        if (cells.Count == 0)
        {
            return "Unused";
        }

        var family = _stage[cells[0].X, cells[0].Y].Kind == CellKind.Block ? "Block" : "Swivel";
        return $"{family}, {cells.Count} {(cells.Count == 1 ? "cell" : "cells")}";
    }

    private static string Trim(string title)
    {
        var trimmed = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        trimmed = trimmed[..Math.Min(trimmed.Length, MaxTitle)].TrimEnd();
        return trimmed.Length == 0 ? "New Stage" : trimmed;
    }

    private static int Wrap(int value, int max) => ((((value - 1) % max) + max) % max) + 1;

    private static CellKind KindOf(EditorTool tool) => tool switch
    {
        EditorTool.Wall => CellKind.Wall,
        EditorTool.Hole => CellKind.Hole,
        EditorTool.Block => CellKind.Block,
        EditorTool.SwivelCenter => CellKind.SwivelCenter,
        EditorTool.SwivelArm => CellKind.SwivelArm,
        _ => CellKind.Floor,
    };

    private HashSet<int> UsedGroups()
    {
        var used = new HashSet<int>();
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                if (_stage[x, y].Group > 0)
                {
                    used.Add(_stage[x, y].Group);
                }
            }
        }

        return used;
    }

    /// <summary>Applies the tool to one cell; true when something changed.</summary>
    private bool Apply(int x, int y)
    {
        switch (Tool)
        {
            case EditorTool.Goal:
                if (_stage.Goal == (x, y))
                {
                    return false;
                }

                _stage.Goal = (x, y);
                return true;
            case EditorTool.Start:
                return PlaceStart(x, y);
            case EditorTool.Eraser:
                return Erase(x, y);
            case var part when IsPart(part):
                return PaintPart(x, y);
            default:
                return Set(x, y, new Tile(KindOf(Tool)));
        }
    }

    private bool PaintPart(int x, int y)
    {
        var kind = KindOf(Tool);

        // A group is all blocks or all one swivel; painting the other kind into it would only make a problem.
        var others = CellsOf(Group).Where(c => c != (x, y)).ToList();
        if (others.Count > 0)
        {
            var family = _stage[others[0].X, others[0].Y].Kind;
            if ((family == CellKind.Block) != (kind == CellKind.Block))
            {
                Message = $"Group {Group} is a {(family == CellKind.Block ? "block" : "swivel")}. Press N for a new group.";
                return false;
            }

            var center = others.FindIndex(c => _stage[c.X, c.Y].Kind == CellKind.SwivelCenter);
            if (kind == CellKind.SwivelCenter && center >= 0)
            {
                Message = $"Swivel group {Group} already has its center at ({others[center].X}, {others[center].Y}).";
                return false;
            }
        }

        return Set(x, y, new Tile(kind, Group, OverHole));
    }

    private bool PlaceStart(int x, int y)
    {
        var slot = Start - 1;
        var there = _stage.Characters.IndexOf((x, y));
        if (there == slot)
        {
            return false;
        }

        if (there >= 0)
        {
            Message = $"Start {there + 1} is already there.";
            return false;
        }

        if (slot < _stage.Characters.Count)
        {
            _stage.Characters[slot] = (x, y);
            return true;
        }

        // Characters have no gaps: start 3 can't come before start 2.
        _stage.Characters.Add((x, y));
        if (_stage.Characters.Count != Start)
        {
            Message = $"Placed as start {_stage.Characters.Count}: starts are numbered without gaps.";
            Start = _stage.Characters.Count;
        }

        return true;
    }

    private bool Erase(int x, int y)
    {
        var start = _stage.Characters.IndexOf((x, y));
        var tile = _stage[x, y];
        if (start >= 0)
        {
            _stage.Characters.RemoveAt(start);
            return true;
        }

        if (_stage.Goal == (x, y))
        {
            _stage.Goal = null;
            return true;
        }

        if (tile.Kind is CellKind.Block or CellKind.SwivelCenter or CellKind.SwivelArm)
        {
            return Set(x, y, new Tile(tile.OverHole ? CellKind.Hole : CellKind.Floor));
        }

        return Set(x, y, new Tile(CellKind.Wall));
    }

    private bool Set(int x, int y, Tile tile)
    {
        if (_stage[x, y] == tile)
        {
            return false;
        }

        _stage[x, y] = tile;
        return true;
    }

    /// <summary>Records a change made since <paramref name="before"/>, which undo returns to.</summary>
    private void Changed(Snapshot before)
    {
        _undo.Add(before);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _change = ++_lastChange;
        _problems = null;
        ProblemFocus = -1;
    }

    /// <summary>Moves the last state of <paramref name="from"/> into the stage, keeping the current one on <paramref name="to"/>.</summary>
    private bool Step(List<Snapshot> from, List<Snapshot> to)
    {
        EndStroke();
        if (from.Count == 0)
        {
            return false;
        }

        to.Add(Take());
        var snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        _stage = new StageBuilder { Goal = snapshot.Goal };
        for (var i = 0; i < snapshot.Tiles.Length; i++)
        {
            _stage[i % Stage.Width, i / Stage.Width] = snapshot.Tiles[i];
        }

        _stage.Characters.AddRange(snapshot.Starts);
        (Title, _change, _problems, ProblemFocus) = (snapshot.Title, snapshot.Change, null, -1);
        return true;
    }

    private Snapshot Take() => new(
        [.. Enumerable.Range(0, Stage.Width * Stage.Height).Select(i => _stage[i % Stage.Width, i / Stage.Width])],
        _stage.Goal,
        [.. _stage.Characters],
        Title,
        _change);

    /// <summary>The whole stage at one moment, for undo and redo.</summary>
    private sealed record Snapshot(Tile[] Tiles, (int X, int Y)? Goal, (int X, int Y)[] Starts, string Title, int Change);
}
