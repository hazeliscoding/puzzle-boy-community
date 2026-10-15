using PuzzleBoy.Community.Classic;

namespace PuzzleBoy.Community.Tests;

/// <summary>Imports from a missing or broken copy of the game: each says what to do and writes nothing.</summary>
public class ClassicImportTests
{
    private static readonly string BundleUnderGame = Path.Combine("smt3hd_Data", "StreamingAssets", "PC", "pazzleboy");

    /// <summary>Asserts that nothing was imported: no counts, and no data folder at all.</summary>
    private static void NothingImported(ClassicImportResult result, string dataDir)
    {
        Assert.False(result.Imported);
        Assert.Equal((0, 0), (result.Original, result.Bonus));
        Assert.False(Directory.Exists(dataDir));
    }

    [Fact]
    public void With_no_copy_of_the_game_anywhere_the_search_says_to_type_the_folder_and_writes_nothing()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var result = ClassicImport.Import(ClassicSource.Find([temp.Path, Path.Combine(temp.Path, "steamapps", "common", "smt3hd")]), data, folder: null);
        Assert.Equal("Couldn't find Shin Megami Tensei III Nocturne HD Remaster beside this game or in your Steam libraries. Choose Import from a Folder or Zip and type the folder that holds smt3hd.exe.", result.Message);
        NothingImported(result, data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("a\0b")]
    public void A_blank_folder_asks_for_one(string typed)
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var result = ClassicImport.FromFolder(typed, data);
        Assert.Equal("Type the folder that holds smt3hd.exe, or the path of a classic pack.", result.Message);
        NothingImported(result, data);
    }

    [Fact]
    public void A_folder_that_isnt_there_is_named_in_the_message()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var missing = Path.Combine(temp.Path, "nowhere");
        var result = ClassicImport.FromFolder($"  \"{missing}\" ", data);
        Assert.StartsWith($"There's no folder at {missing}. Type the folder that holds smt3hd.exe", result.Message);
        NothingImported(result, data);
    }

    [Fact]
    public void A_folder_without_the_stage_bundle_says_which_file_it_needs()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        temp.Write("game/smt3hd.exe", "");
        var game = Path.Combine(temp.Path, "game");
        var result = ClassicImport.FromFolder(game, data);
        Assert.Equal($"{game} has no Puzzle Boy stages: there's no {BundleUnderGame} in it. Type the folder that holds smt3hd.exe, or a copy of its smt3hd_Data folder.", result.Message);
        NothingImported(result, data);
    }

    [Theory]
    [InlineData("game/smt3hd_Data/StreamingAssets/PC/pazzleboy", "game")]
    [InlineData("copy/smt3hd_Data/StreamingAssets/PC/pazzleboy", "copy/smt3hd_Data")]
    [InlineData("bundles/pazzleboy", "bundles")]
    public void A_garbage_file_named_like_the_bundle_is_reported_as_damaged_and_nothing_is_written(string file, string folder)
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        ClassicFixture.WriteBundles(Path.GetDirectoryName(Path.Combine(temp.Path, file))!, ClassicFixture.AllStages(SolverTests.Draw("1.b.G")));
        temp.Write(file, "This is not a Unity bundle, just text with the bundle's name.");
        var install = ClassicSource.At(Path.Combine(temp.Path, folder));
        Assert.NotNull(install);
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, file)), install.BundlePath);

        var result = ClassicImport.FromFolder(Path.Combine(temp.Path, folder), data, ClassicFixture.Open);
        Assert.StartsWith($"The pazzleboy bundle at {install.BundlePath} can't be read (it isn't a UnityFS bundle)", result.Message);
        Assert.EndsWith("so nothing was imported. In Steam, verify the game's files (Properties, Installed Files), then try again.", result.Message);
        NothingImported(result, data);

        // A failed read leaves the game's file closed, so Steam can repair it while this game runs.
        File.Delete(install.BundlePath);
    }

    [Fact]
    public void An_empty_bundle_file_is_reported_as_damaged_too()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        ClassicFixture.WriteBundles(Path.Combine(temp.Path, "bundles"), ClassicFixture.AllStages(SolverTests.Draw("1.b.G")));
        temp.Write("bundles/pazzleboy", "");
        var result = ClassicImport.FromFolder(Path.Combine(temp.Path, "bundles"), data, ClassicFixture.Open);
        Assert.Contains("can't be read", result.Message);
        NothingImported(result, data);
    }

    /// <summary>
    /// A 115-byte UnityFS header that declares a 128 TiB bundle with a 2 GiB block list. Handed to AssetsTools.NET as is,
    /// it threw OutOfMemoryException and took the game down.
    /// </summary>
    private static byte[] HeaderDeclaringHugeSizes(uint flags)
    {
        var bytes = new List<byte>();
        bytes.AddRange("UnityFS\0"u8.ToArray());
        bytes.AddRange(BigEndian(6, 4));
        bytes.AddRange("5.x.x\0"u8.ToArray());
        bytes.AddRange("2019.4.40f1\0"u8.ToArray());
        bytes.AddRange(BigEndian(0x7fffffffffff, 8));
        bytes.AddRange(BigEndian(0x7fffffff, 4));
        bytes.AddRange(BigEndian(0x7fffffff, 4));
        bytes.AddRange(BigEndian(flags, 4));
        bytes.AddRange(new byte[115 - bytes.Count]);
        return [.. bytes];

        static IEnumerable<byte> BigEndian(ulong value, int size) => Enumerable.Range(0, size).Select(i => (byte)(value >> (8 * (size - 1 - i))));
    }

    [Theory]
    [InlineData(0x00u)]
    [InlineData(0x40u)]
    [InlineData(0x80u)]
    [InlineData(0xC3u)]
    public void A_bundle_header_declaring_impossible_sizes_is_reported_as_damaged_instead_of_running_out_of_memory(uint flags)
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var bundle = Path.Combine(temp.Path, "bundles", "pazzleboy");
        ClassicFixture.WriteBundles(Path.GetDirectoryName(bundle)!, ClassicFixture.AllStages(SolverTests.Draw("1.b.G")));
        File.WriteAllBytes(bundle, HeaderDeclaringHugeSizes(flags));

        var result = ClassicImport.FromFolder(Path.Combine(temp.Path, "bundles"), data, ClassicFixture.Open);
        Assert.StartsWith($"The pazzleboy bundle at {bundle} can't be read (its header declares sizes", result.Message);
        NothingImported(result, data);
        File.Delete(bundle);
    }

    [Fact]
    public void A_failed_import_leaves_an_earlier_stage_only_import_as_it_was()
    {
        using var temp = new TempFolder();
        var stage = SolverTests.Draw("1.b.G").ToFile();
        ClassicFixture.StoreLegacy(temp.Path, ClassicStages.Original.ToDictionary(f => f, _ => stage));
        var index = File.ReadAllText(Path.Combine(ClassicStages.Folder(temp.Path), "index.json"));
        ClassicFixture.WriteBundles(Path.Combine(temp.Path, "bundles"), ClassicFixture.AllStages(SolverTests.Draw("1.b.G")));
        temp.Write("bundles/common", "garbage");

        Assert.False(ClassicImport.FromFolder(Path.Combine(temp.Path, "bundles"), temp.Path, ClassicFixture.Open).Imported);
        Assert.Equal(index, File.ReadAllText(Path.Combine(ClassicStages.Folder(temp.Path), "index.json")));
        Assert.Equal(20, ClassicStages.Load(temp.Path, [])[0].Stages.Count);
        Assert.Equal(["classic"], Directory.EnumerateFileSystemEntries(temp.Path).Select(Path.GetFileName).Where(n => n!.StartsWith("classic", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_folder_with_the_stages_but_not_every_bundle_the_theme_needs_names_the_missing_one()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var game = ClassicFixture.GameFolder(temp.Path, ClassicFixture.AllStages(SolverTests.Draw("1.b.G")));
        var common = Path.Combine(game, "smt3hd_Data", "StreamingAssets", "PC", "common");
        File.Delete(common);

        var result = ClassicImport.FromFolder(game, data, ClassicFixture.Open);
        Assert.Equal($"{game} has the Puzzle Boy stages but not the common bundle the Classic theme needs ({Path.GetFullPath(common)}). In Steam, verify the game's files (Properties, Installed Files), then try again.", result.Message);
        NothingImported(result, data);
    }

    [Fact]
    public void Steams_library_list_names_every_library()
    {
        const string Vdf = """
            "libraryfolders"
            {
                "0" { "path"  "C:\\Program Files (x86)\\Steam" "apps" { "1" "2" } }
                "1" { "path"		"D:\\Games\\SteamLibrary" }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\Games\SteamLibrary"], PuzzleBoy.Nocturne.GameInstall.LibraryPaths(Vdf));
    }
}
