using PuzzleBoy.Community.Content;

namespace PuzzleBoy.Community.Tests;

public class StagePackTests
{
    private static readonly string FirstPush = File.ReadAllText(Path.Combine(Repo.Content, "stages", "first-steps", "first-push.json"));

    private static string Pack(string stages, string header = "\"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1") =>
        $$"""{ {{header}}, "packId": "test", "title": "Test", "creator": "Tests", "license": "CC0-1.0", "stages": [{{stages}}] }""";

    private static string Entry(string id, string file) => $$"""{ "id": "{{id}}", "title": "A stage", "file": "{{file}}" }""";

    private static (StagePack? Pack, List<string> Problems) Load(TempFolder temp)
    {
        var problems = new List<string>();
        return (StagePack.Load(Path.Combine(temp.Path, "pack"), "pack", problems), problems);
    }

    [Fact]
    public void A_pack_loads_its_stages_in_order_with_pack_scoped_ids()
    {
        using var temp = new TempFolder();
        temp.Write("pack/a.json", FirstPush);
        temp.Write("pack/more/b.json", FirstPush);
        temp.Write("pack/pack.json", Pack($"{Entry("second", "more/b.json")}, {Entry("first", "a.json")}"));
        var (pack, problems) = Load(temp);
        Assert.Empty(problems);
        Assert.Equal(["test/second", "test/first"], pack!.Stages.Select(s => s.Id));
    }

    [Theory]
    [InlineData("../outside.json", "must stay inside its folder")]
    [InlineData("more/../../outside.json", "must stay inside its folder")]
    [InlineData("./a.json", "must stay inside its folder")]
    [InlineData("more//a.json", "must stay inside its folder")]
    [InlineData("/etc/passwd", "must be a relative path")]
    [InlineData("C:/Windows/win.ini", "must be a relative path")]
    [InlineData("a.json:stream", "must be a relative path")]
    [InlineData("more\\\\a.json", "must be a relative path")]
    [InlineData("", "the path is missing")]
    [InlineData("missing.json", "the file is missing")]
    public void A_stage_file_outside_the_pack_folder_or_missing_is_refused(string file, string problem)
    {
        using var temp = new TempFolder();
        temp.Write("outside.json", FirstPush);
        temp.Write("pack/a.json", FirstPush);
        temp.Write("pack/pack.json", Pack($"{Entry("ok", "a.json")}, {Entry("bad", file)}"));
        var (pack, problems) = Load(temp);
        Assert.Contains(problem, Assert.Single(problems));
        Assert.StartsWith("pack, stage \"bad\": ", problems[0]);
        Assert.Equal(["test/ok"], pack!.Stages.Select(s => s.Id));
    }

    [Fact]
    public void A_stage_file_reached_through_a_link_is_refused()
    {
        // Declared first so it outlives the link, which can only be deleted while its target exists.
        using var outside = new TempFolder();
        using var temp = new TempFolder();
        outside.Write("a.json", FirstPush);
        temp.Write("pack/pack.json", Pack(Entry("linked", "linked/a.json")));
        TempFolder.LinkFolder(Path.Combine(temp.Path, "pack", "linked"), outside.Path);
        var (pack, problems) = Load(temp);
        Assert.Equal(["pack, stage \"linked\": \"linked/a.json\" goes through a link, which content can't use."], problems);
        Assert.Empty(pack!.Stages);
    }

    [Theory]
    [InlineData("Upper", "\"id\" should be 1 to 48 lowercase letters, digits or dashes")]
    [InlineData("has space", "\"id\" should be 1 to 48 lowercase letters, digits or dashes")]
    [InlineData("dots.not.allowed", "\"id\" should be 1 to 48 lowercase letters, digits or dashes")]
    [InlineData("a-very-long-id-that-goes-past-the-forty-eight-limit", "\"id\" should be 1 to 48 lowercase letters, digits or dashes")]
    [InlineData("ends-in-newline\\n", "\"id\" should be 1 to 48 lowercase letters, digits or dashes")]
    public void A_stage_id_outside_the_allowed_characters_is_refused(string id, string problem)
    {
        using var temp = new TempFolder();
        temp.Write("pack/a.json", FirstPush);
        temp.Write("pack/pack.json", Pack(Entry(id, "a.json")));
        Assert.Contains(problem, Assert.Single(Load(temp).Problems));
    }

    [Fact]
    public void Two_stages_with_one_id_are_refused()
    {
        using var temp = new TempFolder();
        temp.Write("pack/a.json", FirstPush);
        temp.Write("pack/pack.json", Pack($"{Entry("same", "a.json")}, {Entry("same", "a.json")}"));
        var (pack, problems) = Load(temp);
        Assert.Equal(["pack, stage \"same\": another stage in this pack has the same id."], problems);
        Assert.Single(pack!.Stages);
    }

    [Fact]
    public void A_stage_that_breaks_a_rule_reports_the_engine_problem_with_its_cell()
    {
        using var temp = new TempFolder();
        temp.Write("pack/bad.json", FirstPush.Replace("\"goal\": [7, 5]", "\"goal\": [0, 0]", StringComparison.Ordinal));
        temp.Write("pack/pack.json", Pack(Entry("bad", "bad.json")));
        var (pack, problems) = Load(temp);
        Assert.StartsWith("pack, stage \"bad\": (0, 0): ", Assert.Single(problems));
        Assert.Empty(pack!.Stages);
    }

    [Theory]
    [InlineData("\"format\": \"puzzle-boy-pack\", \"schemaVersion\": 2", "made by a newer version of the game")]
    [InlineData("\"format\": \"puzzle-boy-pack\", \"schemaVersion\": 0", "\"schemaVersion\" should be 1")]
    [InlineData("\"format\": \"puzzle-boy-pack\", \"schemaVersion\": \"1\"", "\"schemaVersion\" should be 1")]
    [InlineData("\"format\": \"puzzle-boy-stage\", \"schemaVersion\": 1", "isn't a puzzle-boy-pack file")]
    [InlineData("\"schemaVersion\": 1", "isn't a puzzle-boy-pack file")]
    public void A_pack_file_with_the_wrong_header_is_refused(string header, string problem)
    {
        using var temp = new TempFolder();
        temp.Write("pack/pack.json", Pack(Entry("a", "a.json"), header));
        var (pack, problems) = Load(temp);
        Assert.Null(pack);
        Assert.Contains(problem, Assert.Single(problems));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"format\": \"puzzle-boy-pack\", \"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1 }")]
    [InlineData("{ \"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1 }")]
    [InlineData("{ \"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1, \"packId\": \"x\", \"title\": \"\\uD800\", \"creator\": \"c\", \"license\": \"l\", \"stages\": [] }")]
    [InlineData("{ \"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1, \"packId\": \"x\", \"title\": \"t\", \"creator\": \"c\", \"license\": \"l\", \"stages\": [1, null, \"s\", [], {}] }")]
    [InlineData("{ \"format\": \"puzzle-boy-pack\", \"schemaVersion\": 1, \"packId\": \"x\", \"title\": \"t\", \"creator\": \"c\", \"license\": \"l\", \"stages\": [{ \"id\": \"a\", \"title\": \"t\", \"file\": 5 }] }")]
    [InlineData("{\"a\":[[[[[[[[[[[[1]]]]]]]]]]]}")]
    public void A_malformed_pack_file_gives_problems_rather_than_an_exception(string json)
    {
        using var temp = new TempFolder();
        temp.Write("pack/pack.json", json);
        Assert.NotEmpty(Load(temp).Problems);
    }

    [Fact]
    public void A_pack_file_over_the_size_limit_is_refused_before_it_is_read()
    {
        using var temp = new TempFolder();
        temp.Write("pack/pack.json", Pack(Entry("a", "a.json")).PadRight(StagePack.MaxPackBytes + 1));
        Assert.Equal(["pack/pack.json: the file is over 64 KiB."], Load(temp).Problems);
    }

    [Fact]
    public void A_pack_listing_too_many_stages_is_refused()
    {
        using var temp = new TempFolder();
        temp.Write("pack/pack.json", Pack(string.Join(", ", Enumerable.Range(0, StagePack.MaxStages + 1).Select(i => Entry($"s{i}", "a.json")))));
        Assert.Equal(["pack/pack.json: \"stages\" should list 1 to 200 stages."], Load(temp).Problems);
    }

    [Fact]
    public void A_manifest_path_that_leaves_the_content_folder_is_refused()
    {
        using var temp = TempFolder.WithContent();
        var manifest = Path.Combine(temp.Path, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("themes/default/theme.json", "../theme.json", StringComparison.Ordinal));
        var problems = new List<string>();
        Assert.Null(ContentManifest.Load(temp.Path, problems)!.Theme);
        Assert.Equal(["manifest.json, theme: \"../theme.json\" must stay inside its folder, without empty, . or .. parts."], problems);
    }

    [Fact]
    public void A_manifest_from_a_newer_version_is_refused()
    {
        using var temp = TempFolder.WithContent();
        var manifest = Path.Combine(temp.Path, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 3", StringComparison.Ordinal));
        var problems = new List<string>();
        Assert.Null(ContentManifest.Load(temp.Path, problems));
        Assert.Equal(["manifest.json: this file was made by a newer version of the game (schema version 3)."], problems);
    }

    [Fact]
    public void A_font_that_is_not_TrueType_is_refused()
    {
        using var temp = TempFolder.WithContent();
        File.WriteAllText(Path.Combine(temp.Path, "fonts", "Silkscreen-Bold.ttf"), "<html>");
        var problems = new List<string>();
        Assert.DoesNotContain("titleBold", ContentManifest.Load(temp.Path, problems)!.Fonts.Keys);
        Assert.Equal(["manifest.json, font \"titleBold\": the file isn't a TrueType font."], problems);
    }
}
