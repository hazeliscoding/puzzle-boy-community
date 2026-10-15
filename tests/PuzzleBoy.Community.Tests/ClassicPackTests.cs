using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>The classic pack: building it, checking it, carrying it to another computer. Handmade bundles only.</summary>
public class ClassicPackTests
{
    private static readonly Stage Small = SolverTests.Draw("1.b.G");

    private static readonly string[] BundlePaths = [.. ClassicPack.Bundles.Select(ClassicPack.PathOf)];

    private static string Pack(string dataDir) => ClassicStages.Folder(dataDir);

    private static JsonObject Manifest(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, ClassicPack.ManifestName)))!.AsObject();

    private static void WriteManifest(string folder, JsonObject manifest) => File.WriteAllText(Path.Combine(folder, ClassicPack.ManifestName), manifest.ToJsonString());

    private static string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>A zip holding <paramref name="entries"/> (name, bytes), in order, with optional Unix link bits on one.</summary>
    private static string Zip(string path, IEnumerable<(string Name, byte[] Bytes)> entries, string? link = null)
    {
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (name == link)
            {
                entry.ExternalAttributes = 0xA1FF << 16;
            }

            using var stream = entry.Open();
            stream.Write(bytes);
        }

        return path;
    }

    /// <summary>The entries of a real export of a handmade pack, by name.</summary>
    private static List<(string Name, byte[] Bytes)> ExportedEntries(TempFolder temp)
    {
        var data = Path.Combine(temp.Path, "exporter");
        ClassicFixture.ImportPack(data, Small);
        using var zip = ZipFile.OpenRead(ClassicImport.Export(data, out _)!);
        return [.. zip.Entries.Select(e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return (e.FullName, copy.ToArray());
        })];
    }

    [Fact]
    public void Importing_from_the_players_install_copies_each_bundle_byte_for_byte_and_writes_the_manifest()
    {
        using var temp = new TempFolder();
        var game = ClassicFixture.GameFolder(temp.Path, ClassicFixture.AllStages(Small));
        var data = Path.Combine(temp.Path, "data");

        var result = ClassicImport.FromFolder(game, data, ClassicFixture.Open);
        Assert.True(result.Imported, result.Message);
        Assert.Equal($"Imported your classic pack from {game}: the Classic theme and 43 classic stages, 20 of 20 in Classic: Original 20 and 23 of 23 in Classic: Bonus.", result.Message);
        foreach (var bundle in ClassicPack.Bundles)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(game, "smt3hd_Data", "StreamingAssets", "PC", bundle)), File.ReadAllBytes(Path.Combine(Pack(data), ClassicPack.PathOf(bundle))));
        }

        var manifest = Manifest(Pack(data));
        Assert.Equal(ClassicPack.Format, (string?)manifest["format"]);
        Assert.Equal(1, (int?)manifest["schemaVersion"]);
        Assert.Equal(ClassicPack.LocalImport, (string?)manifest["origin"]);
        Assert.Equal(game, (string?)manifest["createdFrom"]);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", (string?)manifest["created"]);
        Assert.Equal(BundlePaths, manifest["files"]!.AsArray().Select(f => (string?)f!["path"]));
        Assert.All(manifest["files"]!.AsArray(), f =>
        {
            var file = Path.Combine(Pack(data), (string)f!["path"]!);
            Assert.Equal(new FileInfo(file).Length, (long)f["size"]!);
            Assert.Equal(Hash(file), (string?)f["sha256"]);
        });

        // Only the pack and nothing it was built in is left: no scratch folder, no earlier pack set aside.
        Assert.Equal(["classic"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
        Assert.Equal([ClassicPack.ManifestName, "smt3hd_Data"], Directory.EnumerateFileSystemEntries(Pack(data)).Select(Path.GetFileName).Order());
        Assert.NotNull(ClassicPack.Open(Pack(data), checkHashes: true, out var problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("game/smt3hd_Data/StreamingAssets/PC", "game")]
    [InlineData("copy/smt3hd_Data/StreamingAssets/PC", "copy/smt3hd_Data")]
    [InlineData("bundles", "bundles")]
    public void Every_folder_layout_a_player_may_point_at_builds_the_same_pack(string bundles, string folder)
    {
        using var temp = new TempFolder();
        ClassicFixture.WriteBundles(Path.Combine(temp.Path, bundles), ClassicFixture.AllStages(Small));
        var data = Path.Combine(temp.Path, "data");
        Assert.True(ClassicImport.FromFolder(Path.Combine(temp.Path, folder), data, ClassicFixture.Open).Imported);
        Assert.Equal(43, ClassicStages.Load(data, [], ClassicFixture.Open).Sum(p => p.Stages.Count));
    }

    public static TheoryData<string, string> BrokenPacks => new()
    {
        { "format", "classic-pack.json isn't a Puzzle Boy classic pack manifest: its \"format\" should be \"puzzle-boy-classic-pack\". Import your classic pack again, from your Nocturne install or the pack you exported." },
        { "newer", "This classic pack was made by a newer version of Puzzle Boy Community Edition (schema version 2). Update the game, or import from your own Nocturne install again." },
        { "origin", "classic-pack.json's \"origin\" is \"downloaded\"; it must be \"local-import\" or \"licensed\"." },
        { "escape", "classic-pack.json lists \"../../save.json\", which isn't one of the files a classic pack holds (pazzleboy, common, dvl0x150 under smt3hd_Data/StreamingAssets/PC/)." },
        { "extra", "classic-pack.json lists \"smt3hd_Data/StreamingAssets/PC/pazzleboy_en\", which isn't one of the files a classic pack holds" },
        { "unlisted", "classic-pack.json doesn't list smt3hd_Data/StreamingAssets/PC/common, which the Classic theme needs." },
        { "missing", "The classic pack is missing smt3hd_Data/StreamingAssets/PC/common. Import your classic pack again" },
        { "size", "smt3hd_Data/StreamingAssets/PC/dvl0x150 is 63 bytes, but classic-pack.json says 62: the file is damaged or was changed." },
        { "hash", "smt3hd_Data/StreamingAssets/PC/dvl0x150 doesn't match its SHA-256 in classic-pack.json: the file is damaged or was changed." },
        { "licensed", "This licensed classic pack doesn't give its \"evidence\", so it isn't used." },
    };

    [Theory]
    [MemberData(nameof(BrokenPacks))]
    public void A_broken_pack_says_which_file_is_wrong_and_what_to_do_and_its_stages_stay_out(string breakage, string message)
    {
        using var temp = new TempFolder();
        ClassicFixture.ImportPack(temp.Path, Small);
        var folder = Pack(temp.Path);
        var manifest = Manifest(folder);
        var files = manifest["files"]!.AsArray();
        var dvl = Path.Combine(folder, ClassicPack.PathOf("dvl0x150"));
        switch (breakage)
        {
            case "format":
                manifest["format"] = "puzzle-boy-pack";
                break;
            case "newer":
                manifest["schemaVersion"] = 2;
                break;
            case "origin":
                manifest["origin"] = "downloaded";
                break;
            case "escape":
                files[0]!["path"] = "../../save.json";
                break;
            case "extra":
                files.Add(new JsonObject { ["path"] = ClassicPack.PathOf("pazzleboy_en"), ["size"] = 1, ["sha256"] = new string('0', 64) });
                break;
            case "unlisted":
                files.RemoveAt(1);
                break;
            case "missing":
                File.Delete(Path.Combine(folder, ClassicPack.PathOf("common")));
                break;
            case "size":
                File.AppendAllText(dvl, "!");
                break;
            case "hash":
                var bytes = File.ReadAllBytes(dvl);
                bytes[^1] ^= 1;
                File.WriteAllBytes(dvl, bytes);
                break;
            case "licensed":
                (manifest["origin"], manifest["licensor"], manifest["license"]) = (ClassicPack.Licensed, "A Licensor", "A License");
                break;
        }

        WriteManifest(folder, manifest);
        Assert.Null(ClassicPack.Open(folder, checkHashes: true, out var problem));
        Assert.Contains(message, problem);

        var problems = new List<string>();
        Assert.Empty(ClassicStages.Load(temp.Path, problems, ClassicFixture.Open));
        Assert.Equal([$"classic: {problem}"], problems);
        Assert.Null(ClassicImport.Export(temp.Path, out var exported));
        Assert.Equal($"Your classic pack wasn't exported. {problem}", exported);
    }

    [Fact]
    public void A_licensed_pack_is_taken_from_a_local_folder_and_names_its_licensor_and_license()
    {
        using var temp = new TempFolder();
        var made = Path.Combine(temp.Path, "made");
        ClassicFixture.ImportPack(made, Small);
        var licensed = Pack(made);
        var manifest = Manifest(licensed);
        manifest.Remove("createdFrom");
        (manifest["origin"], manifest["licensor"], manifest["license"], manifest["evidence"]) = (ClassicPack.Licensed, "Example Licensor", "Example Terms 1.0", "https://example.com/terms");
        WriteManifest(licensed, manifest);

        var data = Path.Combine(temp.Path, "data");
        var result = ClassicImport.FromFolder(licensed, data, ClassicFixture.Open);
        Assert.True(result.Imported, result.Message);
        Assert.EndsWith("Licensed by Example Licensor under Example Terms 1.0.", result.Message);
        var pack = ClassicPack.Open(Pack(data), checkHashes: true, out _)!;
        Assert.Equal((ClassicPack.Licensed, "Example Licensor", "Example Terms 1.0", "https://example.com/terms", null), (pack.Origin, pack.Licensor, pack.License, pack.Evidence, pack.CreatedFrom));
    }

    [Fact]
    public void An_exported_pack_carries_the_note_and_imports_on_a_fresh_profile_with_the_same_files_and_stages()
    {
        using var temp = new TempFolder();
        var first = Path.Combine(temp.Path, "first");
        ClassicFixture.ImportPack(first, Small);
        var zip = ClassicImport.Export(first, out var message);
        Assert.Equal(Path.Combine(PackArchive.ExportsFolder(first), "puzzle-boy-classic-pack.zip"), zip);
        Assert.Equal($"Exported your classic pack to {zip}. Made from your own copy of Nocturne for your own computers. Don't share it.", message);
        using (var archive = ZipFile.OpenRead(zip!))
        {
            Assert.Equal(["README.txt", ClassicPack.ManifestName, .. BundlePaths], archive.Entries.Select(e => e.FullName));
            using var note = new StreamReader(archive.GetEntry("README.txt")!.Open());
            Assert.Contains(ClassicPack.Note, note.ReadToEnd());
        }

        var second = Path.Combine(temp.Path, "second");
        var result = ClassicImport.FromFolder(zip!, second, ClassicFixture.Open);
        Assert.True(result.Imported, result.Message);
        Assert.Equal(File.ReadAllText(Path.Combine(Pack(first), ClassicPack.ManifestName)), File.ReadAllText(Path.Combine(Pack(second), ClassicPack.ManifestName)));
        Assert.All(BundlePaths, path => Assert.Equal(Hash(Path.Combine(Pack(first), path)), Hash(Path.Combine(Pack(second), path))));
        Assert.Equal(
            ClassicStages.Load(first, [], ClassicFixture.Open).SelectMany(p => p.Stages).Select(s => s.Id),
            ClassicStages.Load(second, [], ClassicFixture.Open).SelectMany(p => p.Stages).Select(s => s.Id));
    }

    [Fact]
    public void With_no_pack_there_is_nothing_to_export_and_no_exports_folder()
    {
        using var temp = new TempFolder();
        Assert.Null(ClassicImport.Export(temp.Path, out var message));
        Assert.Equal("There's no classic pack to export yet. Import one from your Nocturne install first.", message);
        Assert.False(Directory.Exists(PackArchive.ExportsFolder(temp.Path)));
    }

    [Theory]
    [InlineData("../evil.txt", "\"../evil.txt\" leads outside the pack (an empty, . or .. part).")]
    [InlineData("/etc/evil", "\"/etc/evil\" is an absolute path.")]
    [InlineData("C:/evil", "\"C:/evil\" names a drive.")]
    [InlineData(@"smt3hd_Data\StreamingAssets\PC\common", "uses backslashes; a pack's paths use /.")]
    [InlineData("smt3hd_Data/StreamingAssets/PC/GameAssembly.dll", "\"smt3hd_Data/StreamingAssets/PC/GameAssembly.dll\" isn't part of a classic pack, which holds only classic-pack.json, README.txt and the pazzleboy, common, dvl0x150 bundles under smt3hd_Data/StreamingAssets/PC/.")]
    [InlineData("README.TXT", "\"README.TXT\" is in the zip twice (names are compared ignoring case).")]
    [InlineData("link", "\"smt3hd_Data/StreamingAssets/PC/common\" is a link, which a classic pack can't hold.")]
    public void A_zip_with_an_entry_a_pack_cant_hold_is_refused_before_anything_is_written(string entry, string message)
    {
        using var temp = new TempFolder();
        var entries = ExportedEntries(temp);
        if (entry != "link")
        {
            entries.Add((entry, "x"u8.ToArray()));
        }

        var zip = Zip(Path.Combine(temp.Path, "pack.zip"), entries, link: entry == "link" ? ClassicPack.PathOf("common") : null);
        var data = Path.Combine(temp.Path, "data");
        var result = ClassicImport.FromFolder(zip, data, ClassicFixture.Open);
        Assert.False(result.Imported);
        Assert.Contains(message, result.Message);
        Assert.StartsWith($"Nothing was imported from {zip}: ", result.Message);
        Assert.False(Directory.Exists(data));
    }

    [Fact]
    public void A_zip_with_too_many_entries_or_no_manifest_is_refused()
    {
        using var temp = new TempFolder();
        var entries = ExportedEntries(temp);
        var crowded = Zip(Path.Combine(temp.Path, "crowded.zip"), entries.Concat(Enumerable.Range(0, 12).Select(i => ($"smt3hd_Data/{i}/", Array.Empty<byte>()))));
        Assert.Equal($"Nothing was imported: {crowded} holds 17 entries; a classic pack holds at most 16.", ClassicImport.FromFolder(crowded, temp.Path, ClassicFixture.Open).Message);

        var bare = Zip(Path.Combine(temp.Path, "bare.zip"), entries.Where(e => e.Name != ClassicPack.ManifestName));
        Assert.Equal($"Nothing was imported: {bare} has no classic-pack.json at its top. Choose the zip that Export My Classic Pack made.", ClassicImport.FromFolder(bare, temp.Path, ClassicFixture.Open).Message);

        var notZip = temp.Write("not.zip", "just text");
        Assert.StartsWith($"Nothing was imported: {notZip} isn't a zip, or it's damaged (", ClassicImport.FromFolder(notZip, temp.Path, ClassicFixture.Open).Message);
        Assert.False(Directory.Exists(Pack(temp.Path)));
    }

    [Fact]
    public void A_zip_entry_that_unpacks_past_the_cap_is_refused_and_its_scratch_folder_removed()
    {
        using var temp = new TempFolder();
        var zip = Path.Combine(temp.Path, "bomb.zip");
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
        {
            archive.CreateEntry(ClassicPack.ManifestName).Open().Dispose();
            using var stream = archive.CreateEntry(ClassicPack.PathOf("common"), CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 129; i++)
            {
                stream.Write(zeros);
            }
        }

        Assert.True(new FileInfo(zip).Length < 1024 * 1024);
        var data = Path.Combine(temp.Path, "data");
        var result = ClassicImport.FromFolder(zip, data, ClassicFixture.Open);
        Assert.Equal("Nothing was imported: \"smt3hd_Data/StreamingAssets/PC/common\" unpacks to more than a classic pack holds (128 MiB a bundle, 160 MiB in all).", result.Message);
        Assert.False(Directory.Exists(data));
    }

    [Fact]
    public void A_zip_whose_bundle_doesnt_match_its_manifest_is_refused_and_the_earlier_pack_stays()
    {
        using var temp = new TempFolder();
        var entries = ExportedEntries(temp);
        var common = entries.FindIndex(e => e.Name == ClassicPack.PathOf("common"));
        var changed = entries[common].Bytes.ToArray();
        changed[^1] ^= 1;
        entries[common] = (entries[common].Name, changed);
        var zip = Zip(Path.Combine(temp.Path, "changed.zip"), entries);

        var data = Path.Combine(temp.Path, "data");
        ClassicFixture.ImportPack(data, SolverTests.Draw("1..b..G"));
        var before = File.ReadAllText(Path.Combine(Pack(data), ClassicPack.ManifestName));

        var result = ClassicImport.FromFolder(zip, data, ClassicFixture.Open);
        Assert.Equal($"Nothing was imported from {zip}. smt3hd_Data/StreamingAssets/PC/common doesn't match its SHA-256 in classic-pack.json: the file is damaged or was changed. Import your classic pack again, from your Nocturne install or the pack you exported.", result.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(Pack(data), ClassicPack.ManifestName)));
        Assert.Equal(["classic"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact]
    public void A_stage_only_import_from_an_earlier_version_keeps_its_stage_ids_and_progress_when_a_pack_replaces_it()
    {
        using var temp = new TempFolder();
        ClassicFixture.StoreLegacy(temp.Path, ClassicFixture.AllStages(Small));
        var legacy = ClassicStages.Load(temp.Path, [], ClassicFixture.Open).SelectMany(p => p.Stages).Select(s => s.Id).ToList();
        Assert.Equal(43, legacy.Count);
        var save = SaveStore.Load(temp.Path);
        save.Data.Record("classic/original/02", 4, 2.5);
        save.Data.Record("classic/bonus/44", 9, 7);
        save.Save();

        ClassicFixture.ImportPack(temp.Path, Small);
        Assert.False(Directory.Exists(Path.Combine(Pack(temp.Path), "stages")));
        Assert.False(File.Exists(Path.Combine(Pack(temp.Path), "index.json")));
        var problems = new List<string>();
        Assert.Equal(legacy, ClassicStages.Load(temp.Path, problems, ClassicFixture.Open).SelectMany(p => p.Stages).Select(s => s.Id));
        Assert.Empty(problems);
        var reloaded = SaveStore.Load(temp.Path).Data;
        Assert.True(reloaded.IsCompleted("classic/original/02"));
        Assert.Equal(9, reloaded.Stages["classic/bonus/44"].BestMoves);
    }

    [Fact]
    public void Importing_your_own_pack_folder_onto_itself_is_refused()
    {
        using var temp = new TempFolder();
        ClassicFixture.ImportPack(temp.Path, Small);
        Assert.Equal("That's the classic pack you already have.", ClassicImport.FromFolder(Pack(temp.Path), temp.Path, ClassicFixture.Open).Message);
        Assert.NotNull(ClassicPack.Open(Pack(temp.Path), checkHashes: true, out _));
    }
}
