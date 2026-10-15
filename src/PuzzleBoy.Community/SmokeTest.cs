using FontStashSharp;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// <c>--smoke-test</c>: checks, with no window, that the content beside the game is complete and that the first stage
/// plays. It proves an unpacked build has everything it needs, without a graphics device.
/// </summary>
internal static class SmokeTest
{
    /// <summary>Runs the checks on <paramref name="contentFolder"/>, writes a short report and returns 0 on success, 1 on failure.</summary>
    public static int Run(string contentFolder, TextWriter output)
    {
        output.WriteLine($"Puzzle Boy Community Edition smoke test: {contentFolder}");
        var problems = Provenance.Check(contentFolder);
        if (problems.Count == 0)
        {
            output.WriteLine("provenance: every file has a complete entry");
        }

        var manifest = ContentManifest.Load(contentFolder, problems);
        var stages = manifest?.Packs.SelectMany(p => p.Stages).ToList() ?? [];
        if (manifest is not null && stages.Count == 0)
        {
            problems.Add("manifest.json: no pack has a stage to play.");
        }

        foreach (var (role, path) in manifest?.Fonts ?? new Dictionary<string, string>())
        {
            try
            {
                // Measuring makes FontStashSharp parse the glyphs, which needs no graphics device.
                var fonts = new FontSystem();
                fonts.AddFont(File.ReadAllBytes(path));
                if (fonts.GetFont(24).MeasureString("Puzzle Boy").X <= 0)
                {
                    problems.Add($"font \"{role}\": measured no width.");
                }
            }
            catch (Exception e)
            {
                problems.Add($"font \"{role}\": {e.Message}");
            }
        }

        if (problems.Count == 0)
        {
            output.WriteLine($"content: {manifest!.Packs.Count} pack(s), {stages.Count} stage(s), {manifest.Fonts.Count} fonts");
            var first = stages[0];
            var board = new Board(first.Stage);
            var before = board.Active;
            var step = Enum.GetValues<Direction>().Select(d => (Direction: d, Result: board.Step(d))).FirstOrDefault(s => s.Result != StepResult.Refused);
            if (step.Result == StepResult.Refused || board.Active == before)
            {
                problems.Add($"stage {first.Id}: no step moves the character.");
            }
            else
            {
                output.WriteLine($"stage {first.Id}: {step.Direction} {step.Result.ToString().ToLowerInvariant()} from ({before.X}, {before.Y}) to ({board.Active.X}, {board.Active.Y})");
            }
        }

        foreach (var problem in problems)
        {
            output.WriteLine($"problem: {problem}");
        }

        output.WriteLine(problems.Count == 0 ? "smoke test passed" : "smoke test failed");
        return problems.Count == 0 ? 0 : 1;
    }
}
