using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>
/// Every bundled stage has a recorded solution in <c>Solutions/&lt;pack&gt;/&lt;stage&gt;.txt</c>: one line of
/// <c>R</c> +x, <c>L</c> −x, <c>D</c> +y (down the drawing), <c>U</c> −y and <c>S</c> switch. Replayed on a fresh
/// board, it must clear the stage on its last input with no input refused.
/// </summary>
public class SolutionTests
{
    private static readonly string Solutions = Path.Combine(Repo.Root, "tests", "PuzzleBoy.Community.Tests", "Solutions");

    private static readonly Dictionary<string, Stage> Bundled = ContentManifest.Load(Repo.Content, [])!
        .Packs.SelectMany(p => p.Stages).ToDictionary(s => s.Id, s => s.Stage);

    public static TheoryData<string> BundledIds => [.. Bundled.Keys];

    /// <summary>Why <paramref name="moves"/> doesn't solve <paramref name="stage"/>, or null when it does.</summary>
    private static string? Fault(Stage stage, string moves)
    {
        var board = new Board(stage);
        for (var i = 0; i < moves.Length; i++)
        {
            if (board.IsCleared)
            {
                return $"the stage clears after input {i}, before the end";
            }

            var moved = moves[i] switch
            {
                'R' => board.Step(Direction.PlusX) != StepResult.Refused,
                'L' => board.Step(Direction.MinusX) != StepResult.Refused,
                'D' => board.Step(Direction.PlusY) != StepResult.Refused,
                'U' => board.Step(Direction.MinusY) != StepResult.Refused,
                'S' => board.Switch(),
                var other => throw new FormatException($"'{other}' isn't R, L, D, U or S."),
            };
            if (!moved)
            {
                return $"input {i + 1} ({moves[i]}) is refused";
            }
        }

        return board.IsCleared ? null : "the stage isn't cleared at the end";
    }

    [Theory]
    [MemberData(nameof(BundledIds))]
    public void Every_bundled_stage_has_a_solution_that_clears_it(string id)
    {
        var file = Path.Combine(Solutions, $"{id}.txt");
        Assert.True(File.Exists(file), $"{id} has no solution at {Path.GetRelativePath(Repo.Root, file)}.");
        Assert.Null(Fault(Bundled[id], File.ReadAllText(file).Trim()));
    }

    [Fact]
    public void Every_solution_file_belongs_to_a_bundled_stage()
    {
        var ids = Directory.EnumerateFiles(Solutions, "*.txt", SearchOption.AllDirectories)
            .Select(f => Path.ChangeExtension(Path.GetRelativePath(Solutions, f), null).Replace('\\', '/'));
        Assert.Empty(ids.Except(Bundled.Keys));
    }

    [Theory]
    [InlineData("RRRRDDRR", "the stage isn't cleared at the end")]
    [InlineData("RRRRDDRRRL", "the stage clears after input 9, before the end")]
    [InlineData("URRRRDDRRR", "input 1 (U) is refused")]
    [InlineData("SRRRRDDRRR", "input 1 (S) is refused")]
    public void A_wrong_solution_is_caught(string moves, string fault) =>
        Assert.Equal(fault, Fault(Bundled["first-steps/walk-to-the-goal"], moves));
}
