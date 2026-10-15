using System.IO.Compression;
using System.Text;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Editor;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class PackArchiveTests
{
    private static readonly Stage Small = SolverTests.Draw("1.b.G");
    private static readonly HashSet<string> NoneKnown = [];
    private static readonly HashSet<string> Reserved = PackArchive.Reserved(ContentManifest.Load(Repo.Content, [])!.Packs);

    private static string StageJson(string title = "Small") => new StageDocument(Small, title).ToJson();

    private static string PackJson(string packId = "shared", string file = "stages/small.json", int schemaVersion = 1) => $$"""
        { "format": "puzzle-boy-pack", "schemaVersion": {{schemaVersion}}, "packId": "{{packId}}", "title": "Shared", "creator": "Someone", "license": "CC0-1.0",
          "stages": [ { "id": "small", "title": "Small", "file": "{{file}}" } ] }
        """;

    /// <summary>Writes a zip of <paramref name="entries"/> (name, text) in <paramref name="folder"/>; <paramref name="edit"/> may change each entry.</summary>
    private static string Zip(string folder, IEnumerable<(string Name, string Text)> entries, Action<ZipArchiveEntry>? edit = null)
    {
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in entries)
            {
                var entry = zip.CreateEntry(name);
                edit?.Invoke(entry);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return path;
    }

    private static string GoodZip(string folder, params (string Name, string Text)[] extra) =>
        Zip(folder, [("pack.json", PackJson()), ("stages/small.json", StageJson()), .. extra]);

    /// <summary>Every file and folder under <paramref name="dir"/>, with each file's size, to compare before and after.</summary>
    private static List<string> Snapshot(string dir) =>
        [.. Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Select(p => $"{Path.GetRelativePath(dir, p)} {(File.Exists(p) ? new FileInfo(p).Length : -1)}").Order(StringComparer.Ordinal)];

    private static bool Clears(Stage stage, string moves)
    {
        var board = new Board(stage);
        foreach (var move in moves)
        {
            if (!(move == 'S' ? board.Switch() : board.Step(Solver.DirectionOf(move)) != StepResult.Refused))
            {
                return false;
            }
        }

        return board.IsCleared;
    }

    public static TheoryData<string, string> Malicious => new()
    {
        { "../evil.json", "leads outside the pack" },
        { "stages/../../evil.json", "leads outside the pack" },
        { "/etc/evil.json", "is an absolute path" },
        { "C:/Windows/evil.json", "names a drive" },
        { "//server/share/evil.json", "network (UNC) path" },
        { @"\\server\share\evil.json", "network (UNC) path" },
        { @"stages\evil.json", "uses backslashes" },
        { "stages/a:b.json", "has a colon" },
        { "evil.exe", "isn't part of a pack" },
        { "stages/deep/evil.json", "isn't part of a pack" },
        { "other/", "is a folder" },
        { "stages/inner.zip", "is another archive" },
    };

    [Theory]
    [MemberData(nameof(Malicious))]
    public void A_pack_entry_with_a_dangerous_name_is_refused_and_nothing_is_written(string name, string expected)
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "save.json"), "{}");
        var before = Snapshot(data);

        var result = PackArchive.Import(GoodZip(temp.Path, (name, StageJson())), data, NoneKnown, Reserved);

        Assert.Null(result.PackId);
        Assert.Contains(expected, result.Message);
        Assert.Contains(name, result.Message);
        Assert.Equal(before, Snapshot(data));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "symlink", "is a link" },
        { "duplicate", "is in the archive twice" },
        { "duplicate-case", "is in the archive twice" },
        { "oversized", "unpacks to more than 64 KiB" },
        { "too-many", "a pack may hold 256" },
        { "too-big", "over 2 MiB" },
        { "not-zip", "isn't a zip archive" },
        { "no-pack", "has no pack.json" },
        { "bad-pack-json", "pack.json isn't valid JSON" },
        { "bad-stage-json", "stage \"small\"" },
        { "newer-pack", "made by a newer version" },
        { "newer-stage", "newer version" },
        { "file-outside-stages", "must be a .json file directly in stages/" },
        { "unlisted-stage", "doesn't list this file" },
        { "nested-zip-bytes", "is another archive" },
        { "reserved-id", "belongs to the game's own stages" },
        { "known-layout", PackArchive.KnownLayoutMessage },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_bad_pack_is_refused_with_a_specific_message_and_nothing_is_written(string kind, string expected)
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "save.json"), "{}");
        var before = Snapshot(data);
        var known = NoneKnown;
        var zip = kind switch
        {
            // 0xA1FF in the top 16 bits is a Unix symbolic link with mode 777.
            "symlink" => Zip(temp.Path, [("pack.json", PackJson()), ("stages/small.json", StageJson())], e => e.ExternalAttributes = e.FullName == "stages/small.json" ? unchecked((int)0xA1FF0000) : 0),
            "duplicate" => GoodZip(temp.Path, ("stages/small.json", StageJson())),
            "duplicate-case" => GoodZip(temp.Path, ("Stages/Small.json", StageJson())),

            // Spaces compress about a thousand to one, so the archive is small and the entry isn't.
            "oversized" => GoodZip(temp.Path, ("stages/big.json", new string(' ', 10 * 1024 * 1024))),
            "too-many" => Zip(temp.Path, [("pack.json", PackJson()), .. Enumerable.Range(0, 256).Select(i => ($"stages/s{i}.json", "{}"))]),
            "too-big" => WriteBytes(Path.Combine(temp.Path, "big.zip"), (int)PackArchive.MaxZipBytes + 1),
            "not-zip" => WriteBytes(Path.Combine(temp.Path, "text.zip"), 100),
            "no-pack" => Zip(temp.Path, [("stages/small.json", StageJson())]),
            "bad-pack-json" => Zip(temp.Path, [("pack.json", "{ \"format\": "), ("stages/small.json", StageJson())]),
            "bad-stage-json" => Zip(temp.Path, [("pack.json", PackJson()), ("stages/small.json", "{ not json")]),
            "newer-pack" => Zip(temp.Path, [("pack.json", PackJson(schemaVersion: 2)), ("stages/small.json", StageJson())]),
            "newer-stage" => Zip(temp.Path, [("pack.json", PackJson()), ("stages/small.json", StageJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9", StringComparison.Ordinal))]),
            "file-outside-stages" => Zip(temp.Path, [("pack.json", PackJson(file: "pack.json")), ("stages/small.json", StageJson())]),
            "unlisted-stage" => GoodZip(temp.Path, ("stages/extra.json", StageJson())),
            "nested-zip-bytes" => GoodZip(temp.Path, ("stages/extra.json", "PK\u0003\u0004 not a stage")),
            "reserved-id" => Zip(temp.Path, [("pack.json", PackJson(packId: "first-steps")), ("stages/small.json", StageJson())]),
            "known-layout" => GoodZip(temp.Path),
            _ => throw new ArgumentException(kind),
        };
        if (kind == "known-layout")
        {
            // A fingerprint planted in a scratch list, never a real Nocturne layout.
            known = [$"terrain {LayoutFingerprint.Terrain(Small)}"];
        }

        var result = PackArchive.Import(zip, data, known, Reserved);

        Assert.Null(result.PackId);
        Assert.False(result.Conflict);
        Assert.Contains(expected, result.Message);
        Assert.Equal(before, Snapshot(data));

        static string WriteBytes(string path, int length)
        {
            File.WriteAllBytes(path, [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))]);
            return path;
        }
    }

    [Fact]
    public void The_newer_stage_case_really_changes_the_schema_version() =>
        Assert.Contains("\"schemaVersion\": 1", StageJson());

    [Fact]
    public void The_known_layout_list_sits_beside_the_game() =>
        Assert.Equal(KnownLayouts.Read(Repo.KnownLayouts).Order(), PackArchive.ReadKnown()!.Order());

    [Fact]
    public void Without_the_known_layout_list_nothing_is_imported()
    {
        using var temp = new TempFolder();
        var result = PackArchive.Import(GoodZip(temp.Path), Path.Combine(temp.Path, "data"), null, Reserved);
        Assert.Null(result.PackId);
        Assert.Contains("missing beside the game", result.Message);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "data")));
    }

    [Fact]
    public void A_good_pack_imports_into_its_own_folder_and_leaves_out_a_readme_with_a_warning()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");

        var result = PackArchive.Import(GoodZip(temp.Path, ("README.md", "# Hi"), ("stages/notes.txt", "notes")), data, NoneKnown, Reserved);

        Assert.Equal("shared", result.PackId);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal(["packs/shared/pack.json", "packs/shared/stages/small.json"], Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(data, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
        var problems = new List<string>();
        var pack = Assert.Single(PackArchive.LoadImported(data, Reserved, problems));
        Assert.Empty(problems);
        Assert.Equal("shared/small", Assert.Single(pack.Stages).Id);
    }

    [Fact]
    public void A_pack_json_saved_with_a_byte_order_mark_imports_and_keeps_both()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var zip = Zip(temp.Path, [("pack.json", "﻿" + PackJson()), ("stages/small.json", StageJson())]);
        Assert.Equal("shared", PackArchive.Import(zip, data, NoneKnown, Reserved).PackId);
        Assert.Equal("shared-2", PackArchive.Import(zip, data, NoneKnown, Reserved, ImportChoice.KeepBoth).PackId);
    }

    [Fact]
    public void A_taken_id_asks_first_then_replaces_or_keeps_both()
    {
        using var temp = new TempFolder();
        var data = Path.Combine(temp.Path, "data");
        var zip = GoodZip(temp.Path);
        Assert.Equal("shared", PackArchive.Import(zip, data, NoneKnown, Reserved).PackId);
        var before = Snapshot(data);

        var asked = PackArchive.Import(zip, data, NoneKnown, Reserved);
        Assert.True(asked.Conflict);
        Assert.Null(asked.PackId);
        Assert.Equal(before, Snapshot(data));

        Assert.Equal("shared", PackArchive.Import(zip, data, NoneKnown, Reserved, ImportChoice.Replace).PackId);
        Assert.Equal(before, Snapshot(data));
        Assert.DoesNotContain(Directory.EnumerateDirectories(PackArchive.PacksFolder(data)), d => Path.GetFileName(d).StartsWith('.'));

        Assert.Equal("shared-2", PackArchive.Import(zip, data, NoneKnown, Reserved, ImportChoice.KeepBoth).PackId);
        Assert.Equal(["shared", "shared-2"], PackArchive.LoadImported(data, Reserved, []).Select(p => p.PackId));
        Assert.Equal("shared-2/small", PackArchive.LoadImported(data, Reserved, [])[1].Stages[0].Id);
    }

    [Fact]
    public void A_pack_folder_whose_id_doesnt_match_or_is_reserved_is_not_listed()
    {
        using var temp = new TempFolder();
        temp.Write("packs/renamed/pack.json", PackJson());
        temp.Write("packs/renamed/stages/small.json", StageJson());
        temp.Write("packs/first-steps/pack.json", PackJson(packId: "first-steps"));
        temp.Write("packs/first-steps/stages/small.json", StageJson());
        var problems = new List<string>();
        Assert.Empty(PackArchive.LoadImported(temp.Path, Reserved, problems));
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Export_writes_the_built_in_pack_format_and_refuses_a_known_layout()
    {
        using var temp = new TempFolder();
        CustomStages.Write(temp.Path, "small", new StageDocument(Small, "Small"));
        var stages = CustomStages.List(temp.Path);

        Assert.Null(PackArchive.Export(temp.Path, stages, "Nope", "Me", "CC0-1.0", new HashSet<string> { $"full {LayoutFingerprint.Full(Small)}" }, out var refused));
        Assert.Contains("matches a stage from the original game", refused);
        Assert.Null(PackArchive.Export(temp.Path, stages, "Nope", " ", "CC0-1.0", NoneKnown, out var noCreator));
        Assert.Equal("The pack needs a creator.", noCreator);
        Assert.Null(PackArchive.Export(temp.Path, stages, "Nope", "Me", "GPL-3.0-only", NoneKnown, out _));
        Assert.False(Directory.Exists(PackArchive.ExportsFolder(temp.Path)));

        var path = PackArchive.Export(temp.Path, stages, "Café Puzzles", "Me", "CC-BY-4.0", NoneKnown, out var message);

        Assert.Equal(Path.Combine(Path.GetFullPath(PackArchive.ExportsFolder(temp.Path)), "cafe-puzzles.zip"), path);
        Assert.Contains(path!, message);
        Assert.Equal(["cafe-puzzles.zip"], Directory.EnumerateFiles(PackArchive.ExportsFolder(temp.Path)).Select(Path.GetFileName));
        using var zip = ZipFile.OpenRead(path!);
        Assert.Equal(["pack.json", "stages/small.json"], zip.Entries.Select(e => e.FullName));

        // The same reader as the built-in packs takes it.
        var unpacked = Path.Combine(temp.Path, "unpacked");
        zip.ExtractToDirectory(unpacked);
        var problems = new List<string>();
        var pack = StagePack.Load(unpacked, "unpacked", problems);
        Assert.Empty(problems);
        Assert.Equal(("cafe-puzzles", "Café Puzzles", "Me", "CC-BY-4.0"), (pack!.PackId, pack.Title, pack.Creator, pack.License));
        Assert.Equal("cafe-puzzles/small", Assert.Single(pack.Stages).Id);
    }

    /// <summary>
    /// The handoff's M5 gate, without a window: make a stage in the editor, validate it, playtest it by replaying a
    /// solution on the engine's board, save it, read it back through a new store as after a restart, export it, import
    /// the pack into a fresh data folder, and clear it there, with the clear saved per stage.
    /// </summary>
    [Fact]
    public void A_stage_made_in_the_editor_travels_through_a_pack_to_a_fresh_profile_and_clears()
    {
        using var temp = new TempFolder();
        var (first, second) = (Path.Combine(temp.Path, "first"), Path.Combine(temp.Path, "second"));
        var known = KnownLayouts.Read(Repo.KnownLayouts);

        var editor = new StageEditor { Tool = EditorTool.Floor };
        editor.FillRect((1, 1), (7, 1));
        editor.Tool = EditorTool.Hole;
        editor.PaintAt(4, 1);
        editor.Tool = EditorTool.Block;
        editor.PaintAt(3, 1);
        editor.Tool = EditorTool.Goal;
        editor.PaintAt(7, 1);
        editor.Tool = EditorTool.Start;
        editor.PaintAt(1, 1);
        editor.SetTitle("Gate Crossing");
        Assert.Empty(editor.Problems);

        var stage = editor.TryBuild()!;
        var solved = Solver.Solve(stage);
        Assert.Equal(SolveOutcome.Solved, solved.Outcome);
        Assert.True(Clears(stage, solved.Moves!));

        var name = CustomStages.UniqueName(first, editor.Title);
        Assert.Null(CustomStages.Write(first, name, editor.ToDocument()));

        var restarted = SaveStore.Load(first);
        var mine = Assert.Single(CustomStages.List(restarted.Folder));
        Assert.Equal(("gate-crossing", "Gate Crossing"), (mine.Name, mine.Title));

        var zip = PackArchive.Export(restarted.Folder, [mine], "Gate Pack", "Tester", PackArchive.Licenses[0], known, out _);
        Assert.NotNull(zip);

        var imported = PackArchive.Import(zip!, second, known, Reserved);
        Assert.Equal("gate-pack", imported.PackId);

        var fresh = SaveStore.Load(second);
        var pack = Assert.Single(PackArchive.LoadImported(fresh.Folder, Reserved, []));
        var played = Assert.Single(pack.Stages);
        Assert.Equal(("gate-pack/gate-crossing", "Gate Crossing"), (played.Id, played.Title));
        Assert.True(Clears(played.Stage, solved.Moves!));
        Assert.True(fresh.Data.IsUnlocked(pack.Stages, 0));

        fresh.Data.Record(played.Id, solved.Moves!.Length, 12.5);
        Assert.True(fresh.Save());
        Assert.True(SaveStore.Load(second).Data.IsCompleted("gate-pack/gate-crossing"));
    }
}
