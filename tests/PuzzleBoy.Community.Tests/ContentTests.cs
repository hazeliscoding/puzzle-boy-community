using System.Text.Json.Nodes;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class ContentTests
{
    private static List<StagePack> BundledPacks(List<string> problems) => ContentManifest.Load(Repo.Content, problems)?.Packs.ToList() ?? [];

    /// <summary>Each bundled stage whose layout is in the list at <paramref name="knownList"/>, as "id: full" or "id: terrain".</summary>
    private static List<string> BundledCopiesOf(string knownList)
    {
        var known = KnownLayouts.Read(knownList);
        return BundledPacks([]).SelectMany(p => p.Stages).SelectMany(s => KnownLayouts.Matches(known, s.Stage).Select(m => $"{s.Id}: {m}")).ToList();
    }

    [Fact]
    public void Every_file_under_content_has_a_complete_provenance_entry() => Assert.Empty(Provenance.Check(Repo.Content));

    [Fact]
    public void Every_bundled_pack_and_stage_loads_without_a_problem()
    {
        var problems = new List<string>();
        var manifest = ContentManifest.Load(Repo.Content, problems);
        Assert.Empty(problems);
        Assert.NotNull(manifest!.Theme);
        Assert.Equal(ContentManifest.FontRoles.Order(), manifest.Fonts.Keys.Order());
        Assert.NotEmpty(manifest.Packs);
        Assert.All(manifest.Packs, p => Assert.NotEmpty(p.Stages));
    }

    [Fact]
    public void No_bundled_stage_copies_a_known_Nocturne_layout() => Assert.Empty(BundledCopiesOf(Repo.KnownLayouts));

    [Fact]
    public void The_known_layout_list_holds_a_full_and_a_terrain_fingerprint_for_each_of_the_50_stage_files()
    {
        var lines = KnownLayouts.Read(Repo.KnownLayouts);
        Assert.Equal(50, lines.Count(l => l.StartsWith("full ", StringComparison.Ordinal)));
        Assert.Equal(50, lines.Count(l => l.StartsWith("terrain ", StringComparison.Ordinal)));
        Assert.All(lines, l => Assert.Matches("^(full|terrain) [0-9a-f]{64}$", l));
    }

    [Fact]
    public void A_bundled_stage_whose_fingerprint_is_listed_is_caught()
    {
        using var temp = new TempFolder();
        var stage = BundledPacks([])[0].Stages[1];
        var list = temp.Write("known.txt", $"terrain {LayoutFingerprint.Terrain(stage.Stage)}\nfull {LayoutFingerprint.Full(stage.Stage)}\n");
        Assert.Equal([$"{stage.Id}: full", $"{stage.Id}: terrain"], BundledCopiesOf(list));
    }

    [Fact]
    public void Rotated_mirrored_translated_and_regrouped_copies_share_both_fingerprints()
    {
        var original = Sample((x, y) => (x, y), group => group);
        var copies = new[]
        {
            Sample((x, y) => (19 - y, x), group => group),
            Sample((x, y) => (19 - x, y), group => group),
            Sample((x, y) => (x, 19 - y), group => group),
            Sample((x, y) => (y, x), group => group),
            Sample((x, y) => (x + 7, y + 9), group => group),
            Sample((x, y) => (x, y), group => group + 20),
        };

        Assert.All(copies, copy =>
        {
            Assert.Equal(LayoutFingerprint.Full(original), LayoutFingerprint.Full(copy));
            Assert.Equal(LayoutFingerprint.Terrain(original), LayoutFingerprint.Terrain(copy));
        });
    }

    [Fact]
    public void Moving_the_goal_changes_the_full_fingerprint_but_not_the_terrain()
    {
        var original = Sample((x, y) => (x, y), group => group);
        var builder = StageBuilder.From(original);
        builder.Goal = (1, 4);
        var moved = builder.Build();
        Assert.NotEqual(LayoutFingerprint.Full(original), LayoutFingerprint.Full(moved));
        Assert.Equal(LayoutFingerprint.Terrain(original), LayoutFingerprint.Terrain(moved));
    }

    [Fact]
    public void A_file_without_a_provenance_entry_is_caught()
    {
        using var temp = TempFolder.WithContent();
        temp.Write("fonts/Extra-OFL.txt", "unlisted");
        Assert.Equal(["fonts/Extra-OFL.txt: no entry in provenance.json covers this file."], Provenance.Check(temp.Path));
    }

    [Theory]
    [InlineData("creator")]
    [InlineData("license")]
    [InlineData("modified")]
    [InlineData("redistribution")]
    public void An_entry_missing_a_field_is_caught(string field)
    {
        using var temp = TempFolder.WithContent();
        var provenance = JsonNode.Parse(File.ReadAllText(Path.Combine(temp.Path, "provenance.json")))!;
        provenance["entries"]![0]!.AsObject().Remove(field);
        temp.Write("provenance.json", provenance.ToJsonString());
        Assert.Equal([$"provenance.json, entry 1 (manifest.json): missing {field}."], Provenance.Check(temp.Path));
    }

    [Fact]
    public void Content_that_may_not_be_redistributed_is_caught()
    {
        using var temp = TempFolder.WithContent();
        var provenance = JsonNode.Parse(File.ReadAllText(Path.Combine(temp.Path, "provenance.json")))!;
        provenance["entries"]![0]!["redistribution"] = "unknown";
        temp.Write("provenance.json", provenance.ToJsonString());
        Assert.Equal(["provenance.json, entry 1 (manifest.json): only content whose redistribution is \"allowed\" can ship."], Provenance.Check(temp.Path));
    }

    [Fact]
    public void A_font_that_differs_from_its_recorded_hash_is_caught()
    {
        using var temp = TempFolder.WithContent();
        File.AppendAllText(Path.Combine(temp.Path, "fonts", "Silkscreen-Bold.ttf"), "x");
        var problem = Assert.Single(Provenance.Check(temp.Path));
        Assert.Contains("(fonts/Silkscreen-Bold.ttf): the file's SHA-256 is", problem);
    }

    [Theory]
    [InlineData("stages/first-steps/helper.dll")]
    [InlineData("stages/first-steps/pbMap00.map")]
    [InlineData("stages/first-steps/pazzleboy.assets")]
    [InlineData("stages/first-steps/run.sh")]
    public void A_file_type_content_may_not_hold_is_caught(string file)
    {
        using var temp = TempFolder.WithContent();
        temp.Write(file, "x");
        Assert.Contains($"{file}: content may only hold .json, .md, .txt, .ttf, .png, .wav, .ogg files.", Provenance.Check(temp.Path));
    }

    [Fact]
    public void A_file_beside_the_stages_isnt_covered_by_their_provenance_or_their_pack()
    {
        using var temp = TempFolder.WithContent();
        temp.Write("stages/first-steps/texture.png", "\u0089PNG");
        Assert.Equal(["stages/first-steps/texture.png: no entry in provenance.json covers this file."], Provenance.Check(temp.Path));
        var problems = new List<string>();
        ContentManifest.Load(temp.Path, problems);
        Assert.Equal(["stages/first-steps/texture.png: the pack doesn't list this file."], problems);
    }

    [Fact]
    public void A_stage_file_its_pack_doesnt_list_is_caught()
    {
        using var temp = TempFolder.WithContent();
        temp.Write("stages/first-steps/extra/unlisted.json", "UnityFS");
        var problems = new List<string>();
        ContentManifest.Load(temp.Path, problems);
        Assert.Equal(["stages/first-steps/extra/unlisted.json: the pack doesn't list this file."], problems);
    }

    [Fact]
    public void A_file_by_anyone_else_needs_a_hash_whatever_its_source_says()
    {
        using var temp = TempFolder.WithContent();
        var provenance = JsonNode.Parse(File.ReadAllText(Path.Combine(temp.Path, "provenance.json")))!;
        var font = provenance["entries"]![3]!.AsObject();
        font["source"] = "a friend's hard drive";
        font.Remove("sha256");
        temp.Write("provenance.json", provenance.ToJsonString());
        Assert.Equal(["provenance.json, entry 4 (fonts/AtkinsonHyperlegible-Regular.ttf): a file from elsewhere needs its own entry with a \"sha256\"."], Provenance.Check(temp.Path));
    }

    [Fact]
    public void A_link_under_content_is_caught()
    {
        // Declared first so it outlives the link, which can only be deleted while its target exists.
        using var outside = new TempFolder();
        using var temp = TempFolder.WithContent();
        TempFolder.LinkFolder(Path.Combine(temp.Path, "stages", "linked"), outside.Path);
        Assert.Equal(["stages/linked: content can't hold links."], Provenance.Check(temp.Path));
    }

    /// <summary>A small stage with every kind of tile, drawn through <paramref name="place"/> and with its groups renumbered by <paramref name="regroup"/>.</summary>
    private static Stage Sample(Func<int, int, (int X, int Y)> place, Func<int, int> regroup)
    {
        string[] rows =
        [
            "......",
            ".bb.o.",
            "..a...",
            ".BcaA.",
            "......",
        ];
        var builder = new StageBuilder();
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var (px, py) = place(x, y);
                builder[px, py] = rows[y][x] switch
                {
                    '.' => new Tile(CellKind.Floor),
                    'o' => new Tile(CellKind.Hole),
                    'b' => new Tile(CellKind.Block, regroup(1)),
                    'B' => new Tile(CellKind.Block, regroup(3), OverHole: true),
                    'c' => new Tile(CellKind.SwivelCenter, regroup(2)),
                    'a' => new Tile(CellKind.SwivelArm, regroup(2)),
                    _ => new Tile(CellKind.SwivelArm, regroup(2), OverHole: true),
                };
            }
        }

        builder.Goal = place(5, 4);
        builder.Characters.Add(place(0, 0));
        return builder.Build();
    }
}
