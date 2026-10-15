using Microsoft.Xna.Framework;
using PuzzleBoy.Community.Editor;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class EditorTests
{
    /// <summary>A playable room: floor from (1, 1) to (6, 4), a start at (1, 2), a block of group 1 at (3, 2), the goal at (6, 2).</summary>
    private static StageEditor Room()
    {
        var editor = new StageEditor { Tool = EditorTool.Floor };
        editor.FillRect((1, 1), (6, 4));
        Paint(editor, EditorTool.Start, 1, 2);
        Paint(editor, EditorTool.Block, 3, 2);
        Paint(editor, EditorTool.Goal, 6, 2);
        return editor;
    }

    private static bool Paint(StageEditor editor, EditorTool tool, int x, int y)
    {
        editor.Tool = tool;
        return editor.PaintAt(x, y);
    }

    [Fact]
    public void The_tools_keep_the_order_of_their_number_keys() =>
        Assert.Equal(
            [EditorTool.Wall, EditorTool.Floor, EditorTool.Hole, EditorTool.Block, EditorTool.SwivelCenter, EditorTool.SwivelArm, EditorTool.Goal, EditorTool.Start, EditorTool.Eraser],
            StageEditor.Tools);

    [Fact]
    public void A_new_stage_is_all_wall_untouched_and_says_what_it_lacks()
    {
        var editor = new StageEditor();
        Assert.Equal(new Tile(CellKind.Wall), editor[0, 0]);
        Assert.False(editor.Dirty);
        Assert.Null(editor.TryBuild());
        Assert.Contains(editor.Problems, p => p.Message.Contains("no goal", StringComparison.Ordinal));
        Assert.Contains(editor.Problems, p => p.Message.Contains("no character", StringComparison.Ordinal));
    }

    [Fact]
    public void A_room_with_a_start_a_block_and_a_goal_has_no_problems_and_builds()
    {
        var editor = Room();
        Assert.Empty(editor.Problems);
        var stage = editor.TryBuild();
        Assert.NotNull(stage);
        Assert.Equal((6, 2), stage.Goal);
        Assert.Equal("New Stage", editor.ToDocument().Title);
        Assert.True(editor.Dirty);
    }

    [Fact]
    public void A_stroke_paints_each_cell_it_crosses_once()
    {
        var editor = Room();
        editor.Tool = EditorTool.Eraser;
        editor.BeginStroke();
        Assert.True(editor.PaintAt(3, 2));
        Assert.False(editor.PaintAt(3, 2));
        editor.EndStroke();

        // The block went; its floor stays until the next stroke.
        Assert.Equal(new Tile(CellKind.Floor), editor[3, 2]);
        Assert.True(editor.PaintAt(3, 2));
        Assert.Equal(new Tile(CellKind.Wall), editor[3, 2]);
    }

    [Fact]
    public void The_eraser_takes_the_top_layer_first()
    {
        var editor = Room();
        editor.OverHole = true;
        Paint(editor, EditorTool.Block, 4, 3);
        editor.Tool = EditorTool.Eraser;

        Assert.True(editor.PaintAt(1, 2));
        Assert.Empty(editor.Starts);
        Assert.Equal(new Tile(CellKind.Floor), editor[1, 2]);

        Assert.True(editor.PaintAt(6, 2));
        Assert.Null(editor.Goal);
        Assert.Equal(new Tile(CellKind.Floor), editor[6, 2]);

        // A part over a hole leaves the hole.
        Assert.True(editor.PaintAt(4, 3));
        Assert.Equal(new Tile(CellKind.Hole), editor[4, 3]);
        Assert.True(editor.PaintAt(4, 3));
        Assert.Equal(new Tile(CellKind.Wall), editor[4, 3]);
        Assert.False(editor.PaintAt(4, 3));
    }

    [Fact]
    public void A_rectangle_fills_with_wall_floor_or_hole_only()
    {
        var editor = new StageEditor { Tool = EditorTool.Hole };
        Assert.True(editor.FillRect((5, 6), (2, 4)));
        for (var y = 4; y <= 6; y++)
        {
            for (var x = 2; x <= 5; x++)
            {
                Assert.Equal(new Tile(CellKind.Hole), editor[x, y]);
            }
        }

        Assert.Equal(new Tile(CellKind.Wall), editor[6, 4]);
        Assert.False(editor.FillRect((2, 4), (5, 6)));

        editor.Tool = EditorTool.Block;
        Assert.False(editor.FillRect((0, 0), (3, 3)));
        Assert.Equal("Rectangles fill with wall, floor or hole.", editor.Message);
        Assert.Equal(new Tile(CellKind.Wall), editor[0, 0]);
    }

    [Fact]
    public void A_new_group_is_the_lowest_id_no_tile_uses()
    {
        var editor = Room();
        editor.ChangeNumber(2);
        Assert.Equal(3, editor.Group);
        Paint(editor, EditorTool.Block, 5, 4);

        Assert.True(editor.NewGroup());
        Assert.Equal(2, editor.Group);
        Paint(editor, EditorTool.Block, 2, 4);
        Assert.True(editor.NewGroup());
        Assert.Equal(4, editor.Group);
    }

    [Fact]
    public void With_every_group_in_use_there_is_no_new_one()
    {
        var editor = new StageEditor();
        editor.Tool = EditorTool.Block;
        for (var group = 1; group <= StageEditor.MaxGroup; group++)
        {
            Assert.True(editor.NewGroup());
            editor.PaintAt(group % 20, group / 20);
        }

        Assert.False(editor.NewGroup());
        Assert.Equal("All 35 groups are in use.", editor.Message);
    }

    [Fact]
    public void Painting_a_block_beside_another_group_keeps_both_ids()
    {
        var editor = Room();
        editor.NewGroup();
        Paint(editor, EditorTool.Block, 4, 2);
        Assert.Equal(new Tile(CellKind.Block, 1), editor[3, 2]);
        Assert.Equal(new Tile(CellKind.Block, 2), editor[4, 2]);
        Assert.Equal("Block, 1 cell", editor.Describe(1));
        Assert.Equal("Block, 1 cell", editor.Describe(2));
        Assert.Empty(editor.Problems);
    }

    [Fact]
    public void A_group_stays_one_kind_and_a_swivel_keeps_one_center()
    {
        var editor = Room();
        Assert.False(Paint(editor, EditorTool.SwivelCenter, 4, 3));
        Assert.Equal("Group 1 is a block. Press N for a new group.", editor.Message);

        editor.NewGroup();
        Assert.True(Paint(editor, EditorTool.SwivelCenter, 4, 3));
        Assert.True(Paint(editor, EditorTool.SwivelArm, 4, 4));
        Assert.False(Paint(editor, EditorTool.SwivelCenter, 2, 3));
        Assert.Equal("Swivel group 2 already has its center at (4, 3).", editor.Message);
        Assert.False(Paint(editor, EditorTool.Block, 2, 3));
        Assert.Equal("Swivel, 2 cells", editor.Describe(2));

        // Repainting the center where it is stays allowed.
        editor.OverHole = true;
        Assert.True(Paint(editor, EditorTool.SwivelCenter, 4, 3));
        Assert.Equal(new Tile(CellKind.SwivelCenter, 2, OverHole: true), editor[4, 3]);
    }

    [Fact]
    public void Starts_are_numbered_without_gaps_and_never_share_a_cell()
    {
        var editor = Room();
        editor.Tool = EditorTool.Start;
        editor.ChangeNumber(2);
        Assert.Equal(3, editor.Start);
        Assert.True(editor.PaintAt(1, 3));
        Assert.Equal(2, editor.Start);
        Assert.Equal("Placed as start 2: starts are numbered without gaps.", editor.Message);
        Assert.Equal([(1, 2), (1, 3)], editor.Starts);

        Assert.False(editor.PaintAt(1, 2));
        Assert.Equal("Start 1 is already there.", editor.Message);

        // Placing a start that exists moves it.
        Assert.True(editor.PaintAt(2, 1));
        Assert.Equal([(1, 2), (2, 1)], editor.Starts);
        editor.ChangeNumber(3);
        Assert.Equal(1, editor.Start);
    }

    [Fact]
    public void Validation_names_each_problem_cell_and_the_cursor_jumps_to_it()
    {
        var editor = Room();
        Paint(editor, EditorTool.Block, 5, 4);
        Paint(editor, EditorTool.Goal, 3, 2);

        Assert.Equal(2, editor.Problems.Count);
        Assert.Equal((3, 2), (editor.Problems[0].X, editor.Problems[0].Y));
        Assert.StartsWith("The goal is on a block", editor.Problems[0].Message, StringComparison.Ordinal);
        Assert.Equal((5, 4), (editor.Problems[1].X, editor.Problems[1].Y));
        Assert.StartsWith("Block group 1 is split", editor.Problems[1].Message, StringComparison.Ordinal);
        Assert.True(editor.HasProblemAt(5, 4));
        Assert.Null(editor.TryBuild());
        Assert.Throws<StageFormatException>(editor.ToDocument);

        Assert.True(editor.NextProblem(1));
        Assert.Equal((0, (3, 2)), (editor.ProblemFocus, editor.Cursor));
        Assert.True(editor.NextProblem(1));
        Assert.Equal((1, (5, 4)), (editor.ProblemFocus, editor.Cursor));
        Assert.True(editor.NextProblem(1));
        Assert.Equal(0, editor.ProblemFocus);
        Assert.True(editor.NextProblem(-1));
        Assert.Equal(1, editor.ProblemFocus);

        // A change can fix or add problems, so the focus starts over.
        Paint(editor, EditorTool.Goal, 6, 2);
        Assert.Equal(-1, editor.ProblemFocus);
        Assert.Single(editor.Problems);
    }

    [Fact]
    public void Problems_follow_the_stroke_as_it_paints()
    {
        var editor = Room();
        editor.Tool = EditorTool.Wall;
        editor.BeginStroke();
        editor.PaintAt(6, 2);
        Assert.Contains(editor.Problems, p => p.X == 6 && p.Y == 2);
        editor.EndStroke();
    }

    [Fact]
    public void Picking_a_cell_takes_its_tool_group_and_hole()
    {
        var editor = Room();
        editor.NewGroup();
        editor.OverHole = true;
        Paint(editor, EditorTool.SwivelArm, 4, 4);
        editor.OverHole = false;
        editor.Tool = EditorTool.Wall;

        editor.Pick(4, 4);
        Assert.Equal((EditorTool.SwivelArm, 2, true), (editor.Tool, editor.Group, editor.OverHole));
        editor.Pick(3, 2);
        Assert.Equal((EditorTool.Block, 1, false), (editor.Tool, editor.Group, editor.OverHole));
        editor.Pick(1, 2);
        Assert.Equal((EditorTool.Start, 1), (editor.Tool, editor.Start));
        editor.Pick(6, 2);
        Assert.Equal(EditorTool.Goal, editor.Tool);
        editor.Pick(2, 3);
        Assert.Equal(EditorTool.Floor, editor.Tool);
        editor.Pick(0, 0);
        Assert.Equal(EditorTool.Wall, editor.Tool);
    }

    [Fact]
    public void Playing_the_built_stage_leaves_the_editor_as_it_was()
    {
        var editor = Room();
        var board = new Board(editor.TryBuild()!);
        Assert.Equal(StepResult.Walked, board.Step(Direction.PlusX));
        Assert.Equal(StepResult.Pushed, board.Step(Direction.PlusX));
        Assert.Equal(new Tile(CellKind.Block, 1), editor[3, 2]);
        Assert.Equal([(1, 2)], editor.Starts);
    }

    [Fact]
    public void A_stage_reopened_from_its_document_is_the_same_and_clean()
    {
        var editor = Room();
        editor.SetTitle("  Push   Me  ");
        editor.MarkSaved("push-me");
        Assert.False(editor.Dirty);

        var document = StageDocument.Parse(editor.ToDocument().ToJson());
        var reopened = new StageEditor(document.Stage, document.Title!, "push-me");
        Assert.Equal("Push Me", reopened.Title);
        Assert.Equal(editor.TryBuild()!.ToFile(), reopened.TryBuild()!.ToFile());
        Assert.False(reopened.Dirty);
    }

    [Fact]
    public void A_title_is_trimmed_capped_and_never_blank()
    {
        var editor = new StageEditor();
        Assert.False(editor.SetTitle("   "));
        Assert.Equal("New Stage", editor.Title);
        Assert.True(editor.SetTitle(new string('x', 60)));
        Assert.Equal(StageEditor.MaxTitle, editor.Title.Length);
        Assert.True(editor.Dirty);
    }

    [Fact]
    public void The_cursor_stays_on_the_grid()
    {
        var editor = new StageEditor { Cursor = (-3, 25) };
        Assert.Equal((0, 19), editor.Cursor);
    }

    [Fact]
    public void The_canvas_shows_the_whole_grid_with_row_0_at_the_bottom()
    {
        var view = new EditorView(new RectangleF(272, 72, 600, 588));
        Assert.Equal(29, view.Cell);
        var bottomLeft = view.CellCorner(0, 0);
        var topLeft = view.CellCorner(0, 19);
        Assert.True(bottomLeft.Y > topLeft.Y);
        Assert.True(topLeft.X >= 272 && topLeft.Y >= 72);
        Assert.True(view.CellCorner(19, 0).X + view.Cell <= 872);
        Assert.Equal((0, 0), view.CellAt(bottomLeft + new Vector2(1)));
        Assert.Equal((19, 19), view.CellAt(view.CellCorner(19, 19) + new Vector2(28)));
        Assert.Null(view.CellAt(new Vector2(100, 100)));
    }

    [Fact]
    public void Zooming_keeps_the_point_under_the_pointer_and_panning_stops_at_the_edge()
    {
        var view = new EditorView(new RectangleF(272, 72, 600, 588));
        var around = view.CellCorner(5, 5) + new Vector2(10, 10);
        Assert.True(view.ZoomBy(1, around));
        Assert.Equal((5, 5), view.CellAt(around));
        Assert.True(view.ZoomBy(-5, around));
        Assert.False(view.ZoomBy(-1, around));

        view.ZoomBy(3, around);
        Assert.Equal(EditorView.Zooms.Length - 1, view.Zoom);
        view.PanBy(new Vector2(10_000, 10_000));
        Assert.Equal(new Vector2(272, 72), view.CellCorner(0, 19));
        view.PanBy(new Vector2(-10_000, -10_000));
        Assert.Equal(872, view.CellCorner(19, 0).X + view.Cell, 0.01);

        view.Follow(0, 19);
        Assert.Equal((0, 19), view.CellAt(view.CellCorner(0, 19) + new Vector2(1)));
    }

    [Fact]
    public void A_whole_stroke_is_one_undo_step_and_redo_puts_it_back()
    {
        var editor = Room();
        editor.Tool = EditorTool.Hole;
        editor.BeginStroke();
        foreach (var x in new[] { 2, 3, 4, 5 })
        {
            editor.PaintAt(x, 3);
        }

        editor.EndStroke();
        Assert.True(editor.Undo());
        Assert.All(new[] { 2, 3, 4, 5 }, x => Assert.Equal(new Tile(CellKind.Floor), editor[x, 3]));
        Assert.True(editor.Redo());
        Assert.All(new[] { 2, 3, 4, 5 }, x => Assert.Equal(new Tile(CellKind.Hole), editor[x, 3]));
        Assert.False(editor.Redo());
    }

    [Fact]
    public void Undo_takes_back_rectangles_titles_goals_and_starts_in_order()
    {
        var editor = Room();
        editor.SetTitle("Renamed");
        Paint(editor, EditorTool.Start, 2, 4);
        Assert.True(editor.Undo());
        Assert.Equal([(1, 2)], editor.Starts);
        Assert.True(editor.Undo());
        Assert.Equal("New Stage", editor.Title);
        Assert.True(editor.Undo());
        Assert.Null(editor.Goal);
        Assert.True(editor.Undo());
        Assert.Equal(new Tile(CellKind.Floor), editor[3, 2]);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Starts);
        Assert.True(editor.Undo());
        Assert.Equal(new Tile(CellKind.Wall), editor[1, 1]);
        Assert.False(editor.Undo());
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void A_change_after_an_undo_drops_the_redo()
    {
        var editor = Room();
        editor.Undo();
        Assert.True(editor.CanRedo);
        Paint(editor, EditorTool.Wall, 6, 4);
        Assert.False(editor.CanRedo);
        Assert.False(editor.Redo());
    }

    [Fact]
    public void Undoing_back_to_the_saved_stage_makes_it_clean_and_redo_dirty_again()
    {
        var editor = Room();
        editor.MarkSaved("room");
        Paint(editor, EditorTool.Hole, 2, 3);
        Assert.True(editor.Dirty);
        editor.Undo();
        Assert.False(editor.Dirty);
        editor.Redo();
        Assert.True(editor.Dirty);
    }

    [Fact]
    public void Undo_leaves_the_cursor_tools_and_group_where_they_are()
    {
        var editor = Room();
        editor.NewGroup();
        editor.Cursor = (5, 5);
        editor.Undo();
        Assert.Equal((5, 5), editor.Cursor);
        Assert.Equal((EditorTool.Goal, 2), (editor.Tool, editor.Group));
    }

    [Fact]
    public void A_capped_search_never_calls_a_stage_unsolvable()
    {
        var editor = Room();
        var stage = editor.TryBuild()!;
        Assert.Equal("Solved: it clears in 7 moves.", StageEditor.SolveMessage(Play.Solver.Solve(stage, 1000), 1000));

        var capped = StageEditor.SolveMessage(Play.Solver.Solve(stage, 1), 1);
        Assert.Equal(Play.SolveOutcome.LimitReached, Play.Solver.Solve(stage, 1).Outcome);
        Assert.StartsWith("Search limit reached (not proven unsolvable)", capped, StringComparison.Ordinal);

        // Walled off from the goal.
        Paint(editor, EditorTool.Wall, 5, 2);
        editor.FillRect((5, 1), (5, 4));
        var walled = Play.Solver.Solve(editor.TryBuild()!, 1000);
        Assert.Equal(Play.SolveOutcome.Unsolvable, walled.Outcome);
        Assert.StartsWith("Unsolvable", StageEditor.SolveMessage(walled, 1000), StringComparison.Ordinal);
    }

    [Fact]
    public void A_playtest_pause_menu_returns_to_the_editor_instead_of_the_title()
    {
        Assert.Equal(CommunityGame.PauseItems.Length, CommunityGame.PlaytestPauseItems.Length);
        Assert.Equal("Back to Editor", CommunityGame.PlaytestPauseItems[^1]);
        Assert.Equal(CommunityGame.PauseItems[..^1], CommunityGame.PlaytestPauseItems[..^1]);
    }

    [Fact]
    public void A_drag_fills_in_the_cells_it_skipped() =>
        Assert.Equal([(0, 0), (1, 1), (2, 1), (3, 2), (4, 2)], CommunityGame.Line((0, 0), (4, 2)));
}
