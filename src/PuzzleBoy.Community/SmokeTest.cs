using FontStashSharp;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// <c>--smoke-test</c>: checks, with no window, that the content beside the game is complete and that the first stage
/// plays. It proves an unpacked build has everything it needs, without a graphics device. Given a data folder, it also
/// checks the classic stages imported there.
/// </summary>
internal static class SmokeTest
{
    /// <summary>
    /// Runs the checks on <paramref name="contentFolder"/>, and on the classic stages in <paramref name="dataDir"/> when
    /// it's given, writes a short report and returns 0 on success, 1 on failure.
    /// </summary>
    public static int Run(string contentFolder, TextWriter output, string? dataDir = null)
    {
        output.WriteLine($"Puzzle Boy Community Edition {CommunityGame.Version} smoke test: {contentFolder}");
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
            TakeStep(stages[0], output, problems);
        }

        if (dataDir is not null && problems.Count == 0)
        {
            var classic = ClassicStages.Load(dataDir, problems);
            CheckClassicTheme(dataDir, output, problems);
            var known = PackArchive.ReadKnown() ?? [];
            if (classic.Count == 0 && problems.Count == 0)
            {
                output.WriteLine($"classic: nothing imported in {dataDir}");
            }

            foreach (var pack in classic)
            {
                // Every Nocturne stage is in the known-layout list, which is what keeps it out of an exported pack.
                var unknown = pack.Stages.Where(s => !KnownLayouts.Matches(known, s.Stage).Any()).Select(s => s.Id).ToList();
                output.WriteLine($"classic: {pack.Title}, {pack.Stages.Count} stage(s), {pack.Stages.Count - unknown.Count} of them known layouts that a pack export refuses");
                problems.AddRange(unknown.Select(id => $"classic stage {id}: not in the known-layout list, so a pack export wouldn't refuse it."));
                TakeStep(pack.Stages[0], output, problems);
            }
        }

        foreach (var problem in problems)
        {
            output.WriteLine($"problem: {problem}");
        }

        output.WriteLine(problems.Count == 0 ? "smoke test passed" : "smoke test failed");
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Checks the classic pack in <paramref name="dataDir"/>, if there is one, against its manifest and decodes the Classic
    /// theme's assets from it, as the first classic stage played would.
    /// </summary>
    private static void CheckClassicTheme(string dataDir, TextWriter output, List<string> problems)
    {
        var folder = ClassicStages.Folder(dataDir);
        if (!ClassicPack.IsIn(folder))
        {
            return;
        }

        if (ClassicPack.Open(folder, checkHashes: true, out var problem) is not { } pack)
        {
            problems.Add($"classic pack: {problem}");
            return;
        }

        try
        {
            var assets = ClassicAssetCache.Load(pack);
            output.WriteLine($"classic pack: {pack.Origin}, {pack.Files.Count} files, {pack.Size:N0} bytes, every SHA-256 matches");
            output.WriteLine($"classic theme: {assets.Summary}");
        }
        catch (Exception e)
        {
            // The bundle reader throws all sorts for a damaged bundle.
            problems.Add($"classic theme: its assets can't be decoded ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>Takes the first step that isn't refused on a fresh board of <paramref name="stage"/> and reports it.</summary>
    private static void TakeStep(PackStage stage, TextWriter output, List<string> problems)
    {
        var board = new Board(stage.Stage);
        var before = board.Active;
        var step = Enum.GetValues<Direction>().Select(d => (Direction: d, Result: board.Step(d))).FirstOrDefault(s => s.Result != StepResult.Refused);
        if (step.Result == StepResult.Refused || board.Active == before)
        {
            problems.Add($"stage {stage.Id}: no step moves the character.");
        }
        else
        {
            output.WriteLine($"stage {stage.Id}: {step.Direction} {step.Result.ToString().ToLowerInvariant()} from ({before.X}, {before.Y}) to ({board.Active.X}, {board.Active.Y})");
        }
    }
}
