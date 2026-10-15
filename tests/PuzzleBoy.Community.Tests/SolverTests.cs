using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;
using PuzzleBoy.StageCheck;

namespace PuzzleBoy.Community.Tests;

public class SolverTests
{
    /// <summary>
    /// A stage drawn row by row from y = 0; everything outside is wall. Legend: <c>#</c> wall, <c>.</c> floor, <c>o</c> hole,
    /// <c>G</c> goal, <c>1</c>–<c>4</c> characters, <c>b</c>/<c>B</c> block group 1 (B over a hole), <c>k</c> block group 2,
    /// <c>c</c> swivel center and <c>a</c>/<c>A</c> its arms, group 3 (A over a hole).
    /// </summary>
    private static Stage Draw(params string[] rows)
    {
        var builder = new StageBuilder();
        var characters = new SortedDictionary<char, (int, int)>();
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var c = rows[y][x];
                builder[x, y] = c switch
                {
                    '#' => new Tile(CellKind.Wall),
                    'o' => new Tile(CellKind.Hole),
                    'b' => new Tile(CellKind.Block, 1),
                    'B' => new Tile(CellKind.Block, 1, OverHole: true),
                    'k' => new Tile(CellKind.Block, 2),
                    'c' => new Tile(CellKind.SwivelCenter, 3),
                    'a' => new Tile(CellKind.SwivelArm, 3),
                    'A' => new Tile(CellKind.SwivelArm, 3, OverHole: true),
                    _ => new Tile(CellKind.Floor),
                };
                if (c == 'G')
                {
                    builder.Goal = (x, y);
                }
                else if (c is >= '1' and <= '4')
                {
                    characters[c] = (x, y);
                }
            }
        }

        builder.Characters.AddRange(characters.Values);
        return builder.Build();
    }

    private static Board Replay(Stage stage, string moves)
    {
        var board = new Board(stage);
        foreach (var move in moves)
        {
            Assert.False(board.IsCleared, $"The board cleared before the end of {moves}.");
            Assert.True(move == 'S' ? board.Switch() : board.Step(Solver.DirectionOf(move)) != StepResult.Refused, $"{move} was refused in {moves}.");
        }

        return board;
    }

    [Fact]
    public void The_solver_finds_a_shortest_solution_that_the_board_accepts()
    {
        var stage = Draw(
            "1.b..",
            "##.##",
            "##..G");
        var result = Solver.Solve(stage);
        Assert.Equal(SolveOutcome.Solved, result.Outcome);
        Assert.Equal(6, result.Moves!.Length);
        Assert.True(Replay(stage, result.Moves).IsCleared);
    }

    [Fact]
    public void A_stage_whose_goal_cannot_be_reached_is_reported_unsolvable()
    {
        var result = Solver.Solve(Draw("1..#G"));
        Assert.Equal(new SolveResult(SolveOutcome.Unsolvable, null, 3), result);
    }

    [Fact]
    public void Running_out_of_states_is_not_reported_as_unsolvable()
    {
        var result = Solver.Solve(Draw("1.......G"), cap: 3);
        Assert.Equal(new SolveResult(SolveOutcome.LimitReached, null, 3), result);
    }

    [Fact]
    public void A_stage_that_needs_a_switch_is_solved_with_one()
    {
        // The first character can't get past the second until control switches to it.
        var stage = Draw("1.2..G");
        var moves = Solver.Solve(stage).Moves!;
        Assert.Equal(9, moves.Length);
        Assert.Single(moves, m => m == 'S');
        Assert.True(Replay(stage, moves).IsCleared);
        Assert.Equal($"solved in 9 moves (0 pushes/turns, 1 switches): {moves}", StageCheckCommand.Describe(stage, moves));
    }

    [Fact]
    public void Pushes_and_turns_are_counted_from_the_moves()
    {
        // A turn, a push, then a push that drops the block into the hole.
        var stage = Draw(
            "1a.b.oG",
            ".c.....");
        Assert.True(Replay(stage, "RRRRRR").IsCleared);
        Assert.Equal("solved in 6 moves (3 pushes/turns, 0 switches): RRRRRR", StageCheckCommand.Describe(stage, "RRRRRR"));
    }

    [Fact]
    public void A_frozen_board_plays_on_exactly_like_the_board_it_came_from()
    {
        var stage = Draw(
            "1....o..",
            ".b.a..k.",
            ".bAc.ok2",
            "..oa....",
            "3.......",
            "....G..4");
        var random = new Random(7);
        for (var walk = 0; walk < 50; walk++)
        {
            var board = new Board(stage);
            for (var step = 0; step < 60 && !board.IsCleared; step++)
            {
                var frozen = new Board(Solver.Freeze(stage, board));
                Assert.Equal(Snapshot(board), Snapshot(frozen));
                var move = "RLDUS"[random.Next(5)];
                Assert.Equal(Apply(board, move), Apply(frozen, move));
                Assert.Equal(Snapshot(board), Snapshot(frozen));
            }
        }

        static object Apply(Board board, char move) => move == 'S' ? board.Switch() : board.Step(Solver.DirectionOf(move));

        // The cells, the cleared flag, and the visible characters in the order control passes, from the active one.
        static string Snapshot(Board board)
        {
            var cells = string.Join(",", Enumerable.Range(0, 400).Select(i => board[i % 20, i / 20]));
            var turn = Enumerable.Range(0, board.Characters.Count).Select(i => board.Characters[(board.ActiveIndex + i) % board.Characters.Count]);
            return $"{board.IsCleared} {cells} {string.Join(" ", turn.Where(c => c.Visible).Select(c => (c.X, c.Y)))}";
        }
    }

    [Fact]
    public void The_command_reports_a_known_layout_and_fails()
    {
        using var temp = new TempFolder();
        var stage = Path.Combine(Repo.Content, "stages", "first-steps", "nudge.json");
        var document = StageDocument.Parse(File.ReadAllText(stage));
        var known = temp.Write("known.txt", $"terrain {LayoutFingerprint.Terrain(document.Stage)}\n");
        var output = new StringWriter();
        Assert.Equal(1, StageCheckCommand.Run([stage, "--known", known], output));
        Assert.Contains("  known Atlus layout: YES (terrain)", output.ToString());
    }

    [Fact]
    public void The_command_passes_a_pack_of_the_first_campaign_stages_against_the_real_list()
    {
        // The whole campaign takes about a minute to solve; its replays in SolutionTests prove it clears.
        using var temp = new TempFolder();
        string[] ids = ["garden-path", "nudge", "stepping-stone"];
        foreach (var id in ids)
        {
            temp.Write($"pack/{id}.json", File.ReadAllText(Path.Combine(Repo.Content, "stages", "first-steps", $"{id}.json")));
        }

        var entries = string.Join(", ", ids.Select(id => $$"""{ "id": "{{id}}", "title": "{{id}}", "file": "{{id}}.json" }"""));
        temp.Write("pack/pack.json", $$"""{ "format": "puzzle-boy-pack", "schemaVersion": 1, "packId": "test", "title": "Test", "creator": "Tests", "license": "Apache-2.0", "stages": [{{entries}}] }""");
        var output = new StringWriter();
        Assert.Equal(0, StageCheckCommand.Run([Path.Combine(temp.Path, "pack"), "--known", Repo.KnownLayouts], output));
        Assert.Equal(3, output.ToString().Split('\n').Count(l => l.StartsWith("  solved in ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData]
    [InlineData("a.json", "--cap")]
    [InlineData("a.json", "--cap", "0")]
    [InlineData("a.json", "b.json")]
    [InlineData("a.json", "--unknown")]
    public void Bad_arguments_print_the_usage_and_exit_2(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(2, StageCheckCommand.Run(args, output));
        Assert.StartsWith("Usage: StageCheck", output.ToString());
    }

    [Fact]
    public void A_missing_known_list_exits_2_rather_than_skipping_the_check()
    {
        var output = new StringWriter();
        Assert.Equal(2, StageCheckCommand.Run([Repo.Content, "--known", Path.Combine(Repo.Root, "no-such-list.txt")], output));
        Assert.StartsWith("Can't read the known layout list", output.ToString());
    }

    [Fact]
    public void An_invalid_stage_file_is_reported_and_fails()
    {
        using var temp = new TempFolder();
        var file = temp.Write("bad.json", "{ \"format\": \"puzzle-boy-stage\" }");
        var output = new StringWriter();
        Assert.Equal(1, StageCheckCommand.Run([file, "--known", Repo.KnownLayouts], output));
        Assert.StartsWith($"invalid: {file}: ", output.ToString());
    }
}
