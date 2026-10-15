using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>The classic campaigns, read from a handmade import: these tests never need the game.</summary>
public class ClassicStagesTests
{
    private static readonly Stage Small = SolverTests.Draw("1.b.G");

    private static readonly Stage Pair = SolverTests.Draw(
        "1..G",
        "....",
        "2...");

    /// <summary>A fake import of every classic file, each a handmade stage, stored the way a real import stores it.</summary>
    private static TempFolder Imported(Stage? stage = null)
    {
        var temp = new TempFolder();
        var files = ClassicStages.Original.Concat(ClassicStages.Bonus).ToDictionary(f => f, _ => (stage ?? Small).ToFile());
        Assert.Null(ClassicStages.Store(temp.Path, "handmade", files));
        return temp;
    }

    [Fact]
    public void The_original_order_is_exactly_the_twenty_documented_files() =>
        Assert.Equal([2, 4, 32, 8, 38, 36, 37, 48, 42, 49, 40, 20, 22, 13, 21, 24, 14, 10, 25, 26], ClassicStages.Original);

    [Fact]
    public void The_bonus_order_is_exactly_the_twenty_three_solved_unused_files() =>
        Assert.Equal([1, 30, 31, 0, 3, 9, 35, 43, 5, 6, 33, 7, 19, 47, 39, 41, 45, 34, 12, 46, 15, 11, 44], ClassicStages.Bonus);

    [Fact]
    public void The_two_orders_are_distinct_disjoint_stage_files()
    {
        Assert.Equal(20, ClassicStages.Original.Distinct().Count());
        Assert.Equal(23, ClassicStages.Bonus.Distinct().Count());
        Assert.Empty(ClassicStages.Original.Intersect(ClassicStages.Bonus));
        Assert.All(ClassicStages.Original.Concat(ClassicStages.Bonus), f => Assert.InRange(f, 0, 49));
    }

    [Fact]
    public void With_nothing_imported_there_are_no_classic_campaigns_and_nothing_is_written()
    {
        using var temp = new TempFolder();
        var problems = new List<string>();
        Assert.Empty(ClassicStages.Load(temp.Path, problems));
        Assert.Empty(problems);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public void An_import_shows_both_classic_campaigns_in_the_documented_order()
    {
        using var temp = Imported();
        var problems = new List<string>();
        var packs = ClassicStages.Load(temp.Path, problems);
        Assert.Empty(problems);
        Assert.Equal([ClassicStages.OriginalTitle, ClassicStages.BonusTitle], packs.Select(p => p.Title));
        Assert.Equal(ClassicStages.Original.Select(f => $"classic/original/{f:D2}"), packs[0].Stages.Select(s => s.Id));
        Assert.Equal(ClassicStages.Bonus.Select(f => $"classic/bonus/{f:D2}"), packs[1].Stages.Select(s => s.Id));
        Assert.Equal(["Stage 1", "Stage 20"], new[] { packs[0].Stages[0].Title, packs[0].Stages[^1].Title });
        Assert.Equal(Small.ToFile(), packs[1].Stages[0].Stage.ToFile());
        Assert.Equal(828, new FileInfo(Path.Combine(ClassicStages.Folder(temp.Path), "stages", "pbMap02.map")).Length);
    }

    [Fact]
    public void A_missing_changed_or_unreadable_classic_file_is_left_out_with_a_notice_and_the_rest_still_load()
    {
        using var temp = new TempFolder();
        var files = ClassicStages.Original.Concat(ClassicStages.Bonus).ToDictionary(f => f, _ => Small.ToFile());
        files[32] = "not a stage"u8.ToArray();
        Assert.Null(ClassicStages.Store(temp.Path, "handmade", files));
        var stages = Path.Combine(ClassicStages.Folder(temp.Path), "stages");
        File.Delete(Path.Combine(stages, "pbMap02.map"));
        File.WriteAllBytes(Path.Combine(stages, "pbMap01.map"), Pair.ToFile());

        var problems = new List<string>();
        var packs = ClassicStages.Load(temp.Path, problems);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("pbMap02.map: the file is missing", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("pbMap01.map: the file changed since the import", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("pbMap32.map: it can't be read as a stage", StringComparison.Ordinal));
        Assert.Equal(18, packs[0].Stages.Count);
        Assert.Equal("classic/original/04", packs[0].Stages[0].Id);
        Assert.Equal(22, packs[1].Stages.Count);
        Assert.Equal("classic/bonus/30", packs[1].Stages[0].Id);
    }

    [Fact]
    public void Removing_the_classic_stages_deletes_only_the_classic_folder_and_keeps_progress()
    {
        using var temp = Imported();
        var save = SaveStore.Load(temp.Path);
        save.Data.Record("classic/original/02", 3, 1.5);
        save.Save();
        Assert.Null(CustomStages.Write(temp.Path, "mine", new StageDocument(Small, "Mine")));

        Assert.Null(ClassicStages.Remove(temp.Path));
        Assert.False(Directory.Exists(ClassicStages.Folder(temp.Path)));
        Assert.Empty(ClassicStages.Load(temp.Path, []));
        Assert.True(SaveStore.Load(temp.Path).Data.IsCompleted("classic/original/02"));
        Assert.Single(CustomStages.List(temp.Path));
    }

    [Fact]
    public void A_classic_stage_is_never_one_of_the_players_stages_and_a_copy_of_one_is_refused_in_a_pack()
    {
        using var temp = Imported();
        var classic = ClassicStages.Load(temp.Path, [])[0].Stages[0].Stage;
        Assert.Empty(CustomStages.List(temp.Path));

        // The real known-layout list holds every Nocturne stage file's fingerprints; a scratch one holds this stage's.
        Assert.Null(CustomStages.Write(temp.Path, "copy", new StageDocument(classic, "Copy")));
        HashSet<string> known = [$"full {LayoutFingerprint.Full(classic)}", $"terrain {LayoutFingerprint.Terrain(classic)}"];
        Assert.Null(PackArchive.Export(temp.Path, CustomStages.List(temp.Path), "Copies", "Me", "CC0-1.0", known, [], out var message));
        Assert.Contains("matches a stage from the original game", message);
        Assert.False(Directory.Exists(PackArchive.ExportsFolder(temp.Path)));
    }

    [Fact]
    public void The_switch_hands_control_over_on_a_multi_character_classic_stage()
    {
        using var temp = Imported(Pair);
        var bonus = ClassicStages.Load(temp.Path, [])[1];
        var session = new PlaySession(bonus.Stages, 0);
        var first = session.Board.Active;
        Assert.True(session.CanSwitch);
        Assert.True(session.Switch());
        Assert.NotEqual((first.X, first.Y), (session.Board.Active.X, session.Board.Active.Y));
    }
}
