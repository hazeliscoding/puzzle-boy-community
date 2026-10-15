using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>A classic pack carried beside the executable, as a personal build does: read-only, and second to an imported one.</summary>
public class ClassicBesideTests
{
    private static readonly Stage Small = SolverTests.Draw("1.b.G");

    /// <summary>A game folder under <paramref name="root"/> with a valid pack in its <c>classic-pack</c> folder.</summary>
    private static string GameWithPack(string root)
    {
        var maker = Path.Combine(root, "maker");
        ClassicFixture.ImportPack(maker, Small);
        var exe = Path.Combine(root, "exe");
        Directory.CreateDirectory(exe);
        Directory.Move(ClassicStages.Folder(maker), Path.Combine(exe, ClassicStages.BesideName));
        return exe;
    }

    private static Dictionary<string, (long, DateTime)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => (new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)));

    [Fact]
    public void A_valid_pack_beside_the_executable_is_used_read_only_when_the_data_folder_has_none()
    {
        using var temp = new TempFolder();
        var exe = GameWithPack(temp.Path);
        var beside = Path.Combine(exe, ClassicStages.BesideName);
        var before = Snapshot(beside);
        var data = Path.Combine(temp.Path, "data");

        Assert.Equal(beside, ClassicStages.PackFolder(data, exe));
        var pack = ClassicPack.Open(beside, checkHashes: true, out var problem);
        Assert.Null(problem);
        Assert.True(ClassicTheme.Choose(Themes.Auto, packPresent: true, problem, out var notice));
        Assert.Null(notice);
        Assert.Equal(43, ClassicStages.Load(data, [], ClassicFixture.Open, exe).Sum(p => p.Stages.Count));
        Assert.NotNull(ClassicAssetCache.Load(pack!, ClassicFixture.Open).Ui);

        // Nothing was copied into the data folder, and nothing beside the executable changed.
        Assert.False(Directory.Exists(data));
        Assert.Equal(before, Snapshot(beside));
    }

    [Fact]
    public void A_damaged_pack_beside_the_executable_plays_the_Default_theme_with_the_usual_notice()
    {
        using var temp = new TempFolder();
        var exe = GameWithPack(temp.Path);
        var beside = Path.Combine(exe, ClassicStages.BesideName);
        var dvl = Path.Combine(beside, ClassicPack.PathOf("dvl0x150"));
        var bytes = File.ReadAllBytes(dvl);
        bytes[^1] ^= 1;
        File.WriteAllBytes(dvl, bytes);

        var folder = ClassicStages.PackFolder(Path.Combine(temp.Path, "data"), exe);
        Assert.Equal(beside, folder);
        Assert.Null(ClassicPack.Open(folder, checkHashes: true, out var problem));
        Assert.False(ClassicTheme.Choose(Themes.Auto, packPresent: true, problem, out var notice));
        Assert.Equal($"Your classic pack can't be used, so this plays with the Default theme. {problem}", notice);
        Assert.Contains("smt3hd_Data/StreamingAssets/PC/dvl0x150 doesn't match its SHA-256", notice);
    }

    [Fact]
    public void An_imported_pack_in_the_data_folder_wins_over_one_beside_the_executable()
    {
        using var temp = new TempFolder();
        var exe = GameWithPack(temp.Path);
        var data = Path.Combine(temp.Path, "data");
        ClassicFixture.ImportPack(data, Small);
        Assert.Equal(ClassicStages.Folder(data), ClassicStages.PackFolder(data, exe));
    }

    [Fact]
    public void With_no_pack_anywhere_the_data_folder_is_named_and_Auto_plays_Default_silently()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var folder = ClassicStages.PackFolder(data, Path.Combine(temp.Path, "exe"));
        Assert.Equal(ClassicStages.Folder(data), folder);
        Assert.False(ClassicPack.IsIn(folder));
        Assert.False(ClassicTheme.Choose(Themes.Auto, packPresent: false, null, out var notice));
        Assert.Null(notice);
        Assert.Empty(ClassicStages.Load(data, [], ClassicFixture.Open, Path.Combine(temp.Path, "exe")));
    }
}
