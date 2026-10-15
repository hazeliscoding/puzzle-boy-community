using System.Globalization;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.StageCheck;

/// <summary>The <c>StageCheck</c> command line: validate, solve and fingerprint a stage file or a whole pack.</summary>
public static class StageCheckCommand
{
    /// <summary>The list's file name, copied beside the tool on build and publish.</summary>
    public const string KnownListName = "known-atlus-layouts.txt";

    private const string Usage = """
        Usage: StageCheck <stage.json | pack folder> [--cap N] [--known <known-atlus-layouts.txt>]

        Validates each stage, searches it for a shortest solution (at most N states, default 2000000) and checks its
        layout fingerprints against the known Nocturne layouts. The list defaults to the copy beside the tool.
        Exit codes: 0 every stage is valid, original and not proven unsolvable; 1 a stage is invalid, a known layout or
        unsolvable; 2 bad arguments or an unreadable list.
        """;

    /// <summary>Runs the command and returns its exit code; see the usage text for their meaning.</summary>
    /// <param name="args">The command line, without the program name.</param>
    /// <param name="output">Receives the report and any error.</param>
    public static int Run(string[] args, TextWriter output)
    {
        string? input = null;
        string known = Path.Combine(AppContext.BaseDirectory, KnownListName);
        var cap = Solver.DefaultCap;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cap" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out cap) && cap > 0:
                    i++;
                    break;
                case "--known" when i + 1 < args.Length:
                    known = args[++i];
                    break;
                case var arg when input is null && !arg.StartsWith("--", StringComparison.Ordinal):
                    input = arg;
                    break;
                default:
                    output.WriteLine(Usage);
                    return 2;
            }
        }

        if (input is null)
        {
            output.WriteLine(Usage);
            return 2;
        }

        HashSet<string> knownLayouts;
        try
        {
            knownLayouts = KnownLayouts.Read(known);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"Can't read the known layout list at {known} ({e.Message}). Pass --known <path to scripts/{KnownListName}>.");
            return 2;
        }

        var problems = new List<string>();
        var stages = new List<(string Name, Stage? Stage)>();
        if (Directory.Exists(input))
        {
            var pack = StagePack.Load(input, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(input))), problems);
            stages.AddRange(pack?.Stages.Select(s => ($"{s.Id} ({s.Title})", (Stage?)s.Stage)) ?? []);
        }
        else
        {
            var stage = ReadStage(input, problems);
            stages.Add((input, stage));
        }

        foreach (var problem in problems)
        {
            output.WriteLine($"invalid: {problem}");
        }

        var failed = problems.Count > 0;
        foreach (var (name, stage) in stages.Where(s => s.Stage is not null))
        {
            output.WriteLine(name);
            output.WriteLine("  valid");
            var result = Solver.Solve(stage!, cap);
            output.WriteLine(result.Outcome switch
            {
                SolveOutcome.Solved => $"  {Describe(stage!, result.Moves!)}",
                SolveOutcome.LimitReached => $"  search limit reached after {result.States} states (not proven unsolvable)",
                _ => $"  unsolvable (search exhausted after {result.States} states)",
            });
            output.WriteLine($"  fingerprint full    {LayoutFingerprint.Full(stage!)}");
            output.WriteLine($"  fingerprint terrain {LayoutFingerprint.Terrain(stage!)}");
            var matches = KnownLayouts.Matches(knownLayouts, stage!).ToList();
            output.WriteLine(matches.Count == 0 ? "  known Atlus layout: no" : $"  known Atlus layout: YES ({string.Join(", ", matches)})");
            failed |= matches.Count > 0 || result.Outcome == SolveOutcome.Unsolvable;
        }

        return failed ? 1 : 0;
    }

    /// <summary>"solved in N moves (P pushes/turns, S switches): MOVES", counted by replaying the moves on a fresh board.</summary>
    public static string Describe(Stage stage, string moves)
    {
        var board = new Board(stage);
        var (pushes, switches) = (0, 0);
        foreach (var move in moves)
        {
            if (move == 'S')
            {
                board.Switch();
                switches++;
            }
            else if (board.Step(Solver.DirectionOf(move)) is StepResult.Pushed or StepResult.Turned)
            {
                pushes++;
            }
        }

        return $"solved in {moves.Length} moves ({pushes} pushes/turns, {switches} switches): {moves}";
    }

    private static Stage? ReadStage(string file, List<string> problems)
    {
        // A lone file has no pack folder to stay inside, so it's read as given, with the same size cap as in a pack.
        var full = Path.GetFullPath(file);
        if (ContentFile.Read(full, 64 * 1024, file, problems) is not { } bytes)
        {
            return null;
        }

        try
        {
            return StageDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes)).Stage;
        }
        catch (StageFormatException e)
        {
            problems.AddRange(e.Problems.Select(p => $"{file}: {p}"));
            return null;
        }
    }
}
