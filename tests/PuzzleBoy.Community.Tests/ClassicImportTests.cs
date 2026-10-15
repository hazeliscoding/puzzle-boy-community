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
        var result = ClassicImport.Import(ClassicInstall.Find([temp.Path, Path.Combine(temp.Path, "steamapps", "common", "smt3hd")]), data, folder: null);
        Assert.Equal("Couldn't find Shin Megami Tensei III Nocturne HD Remaster beside this game or in your Steam libraries. Choose Enter Folder Path and type the folder that holds smt3hd.exe.", result.Message);
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
        Assert.Equal("Type the folder that holds smt3hd.exe.", result.Message);
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
        temp.Write(file, "This is not a Unity bundle, just text with the bundle's name.");
        var install = ClassicInstall.At(Path.Combine(temp.Path, folder));
        Assert.NotNull(install);
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, file)), install.BundlePath);

        var result = ClassicImport.FromFolder(Path.Combine(temp.Path, folder), data);
        Assert.StartsWith($"The Puzzle Boy bundle at {install.BundlePath} can't be read (", result.Message);
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
        temp.Write("bundles/pazzleboy", "");
        var result = ClassicImport.FromFolder(Path.Combine(temp.Path, "bundles"), data);
        Assert.Contains("can't be read", result.Message);
        NothingImported(result, data);
    }

    [Fact]
    public void A_failed_import_leaves_an_earlier_import_as_it_was()
    {
        using var temp = new TempFolder();
        var stage = SolverTests.Draw("1.b.G").ToFile();
        Assert.Null(ClassicStages.Store(temp.Path, "handmade", ClassicStages.Original.ToDictionary(f => f, _ => stage)));
        var index = File.ReadAllText(Path.Combine(ClassicStages.Folder(temp.Path), "index.json"));
        temp.Write("bundles/pazzleboy", "garbage");

        Assert.False(ClassicImport.FromFolder(Path.Combine(temp.Path, "bundles"), temp.Path).Imported);
        Assert.Equal(index, File.ReadAllText(Path.Combine(ClassicStages.Folder(temp.Path), "index.json")));
        Assert.Equal(20, ClassicStages.Load(temp.Path, [])[0].Stages.Count);
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
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\Games\SteamLibrary"], ClassicInstall.LibraryPaths(Vdf));
    }
}
