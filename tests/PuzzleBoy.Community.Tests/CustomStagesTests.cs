using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class CustomStagesTests
{
    private static readonly Stage Small = SolverTests.Draw("1.b.G");

    [Theory]
    [InlineData("First Push", "first-push")]
    [InlineData("  Café   Crème!  ", "cafe-creme")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("C:\\Windows\\win.ini", "c-windows-win-ini")]
    [InlineData("name. . .", "name")]
    [InlineData("CON", "con-stage")]
    [InlineData("nul", "nul-stage")]
    [InlineData("Com1", "com1-stage")]
    [InlineData("LPT9", "lpt9-stage")]
    [InlineData("com10", "com10")]
    [InlineData("ステージ", "stage")]
    [InlineData("", "stage")]
    [InlineData("...", "stage")]
    public void A_title_becomes_a_safe_file_name(string title, string expected)
    {
        Assert.Equal(expected, CustomStages.SafeName(title));
        Assert.True(CustomStages.IsSafeName(expected));
    }

    [Fact]
    public void A_long_title_is_cut_to_the_name_limit_without_a_trailing_dash()
    {
        var name = CustomStages.SafeName(new string('a', 47) + " b c");
        Assert.Equal(new string('a', 47), name);
        Assert.Equal(CustomStages.MaxName, CustomStages.SafeName(new string('x', 200)).Length);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("Upper")]
    [InlineData("con")]
    [InlineData("trailing-")]
    [InlineData("a b")]
    [InlineData("name.")]
    [InlineData("")]
    public void A_name_that_isnt_safe_is_refused_before_touching_the_disk(string name)
    {
        using var temp = new TempFolder();
        Assert.False(CustomStages.IsSafeName(name));
        Assert.Throws<ArgumentException>(() => CustomStages.PathFor(temp.Path, name));
    }

    [Fact]
    public void Every_path_stays_in_the_custom_folder()
    {
        using var temp = new TempFolder();
        foreach (var title in new[] { "..", "../x", "/abs", "C:", "a\\..\\..\\b", "\0" })
        {
            var path = CustomStages.PathFor(temp.Path, CustomStages.SafeName(title));
            Assert.Equal(Path.GetFullPath(CustomStages.Folder(temp.Path)), Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void A_saved_stage_lists_with_its_title_and_reads_back_the_same()
    {
        using var temp = new TempFolder();
        Assert.Empty(CustomStages.List(temp.Path));
        Assert.Null(CustomStages.Write(temp.Path, "first", new StageDocument(Small, "First")));

        var stage = Assert.Single(CustomStages.List(temp.Path));
        Assert.Equal(("first", "First", "custom/first", null), (stage.Name, stage.Title, stage.Id, stage.Problem));
        Assert.Equal(Small.ToFile(), stage.Stage!.ToFile());
        Assert.False(File.Exists(CustomStages.PathFor(temp.Path, "first") + ".tmp"));
    }

    [Fact]
    public void A_taken_name_gets_a_number()
    {
        using var temp = new TempFolder();
        Assert.Equal("walk", CustomStages.UniqueName(temp.Path, "Walk"));
        CustomStages.Write(temp.Path, "walk", new StageDocument(Small, "Walk"));
        Assert.Equal("walk-2", CustomStages.UniqueName(temp.Path, "Walk"));
        CustomStages.Write(temp.Path, "walk-2", new StageDocument(Small, "Walk"));
        Assert.Equal("walk-3", CustomStages.UniqueName(temp.Path, "WALK"));

        var longTitle = new string('z', 60);
        CustomStages.Write(temp.Path, CustomStages.SafeName(longTitle), new StageDocument(Small, longTitle));
        var next = CustomStages.UniqueName(temp.Path, longTitle);
        Assert.Equal(new string('z', 46) + "-2", next);
        Assert.True(CustomStages.IsSafeName(next));
    }

    [Fact]
    public void A_damaged_file_lists_with_its_problem_and_strange_files_are_left_out()
    {
        using var temp = new TempFolder();
        temp.Write("custom/broken.json", "{ not json");
        temp.Write("custom/Not Safe.json", "{}");
        temp.Write("custom/notes.txt", "hello");
        temp.Write("custom/big.json", new string(' ', 70 * 1024));

        var stages = CustomStages.List(temp.Path);
        Assert.Equal(["big", "broken"], stages.Select(s => s.Name));
        Assert.All(stages, s => Assert.Null(s.Stage));
        Assert.All(stages, s => Assert.NotNull(s.Problem));
        Assert.Empty(CustomStages.AsPack(stages).Stages);
    }

    [Fact]
    public void A_duplicate_gets_its_own_name_and_a_copy_title()
    {
        using var temp = new TempFolder();
        CustomStages.Write(temp.Path, "walk", new StageDocument(Small, "Walk"));
        var copy = CustomStages.Duplicate(temp.Path, "walk", out var error);
        Assert.Null(error);
        Assert.Equal("walk-copy", copy);
        Assert.Equal(["Walk", "Walk copy"], CustomStages.List(temp.Path).Select(s => s.Title));
    }

    [Fact]
    public void Deleting_removes_only_that_stage()
    {
        using var temp = new TempFolder();
        CustomStages.Write(temp.Path, "a", new StageDocument(Small, "A"));
        CustomStages.Write(temp.Path, "b", new StageDocument(Small, "B"));
        Assert.Null(CustomStages.Delete(temp.Path, "a"));
        Assert.Equal(["b"], CustomStages.List(temp.Path).Select(s => s.Name));
    }

    [Fact]
    public void My_stages_are_a_pack_of_custom_ids_that_are_always_open()
    {
        using var temp = new TempFolder();
        CustomStages.Write(temp.Path, "a", new StageDocument(Small, "A"));
        CustomStages.Write(temp.Path, "b", new StageDocument(Small, "B"));
        CustomStages.Write(temp.Path, "c", new StageDocument(Small, "C"));
        var pack = CustomStages.AsPack(CustomStages.List(temp.Path));
        Assert.Equal(("custom", "My Stages"), (pack.PackId, pack.Title));
        Assert.Equal(["custom/a", "custom/b", "custom/c"], pack.Stages.Select(s => s.Id));

        // Nothing is cleared, yet the third stage is open: a player's own stages never lock.
        Assert.True(new SaveData().IsUnlocked(pack.Stages, 2));
    }

    [Fact]
    public void A_folder_that_cant_be_written_gives_a_message_not_an_exception()
    {
        using var temp = new TempFolder();

        // A file where the custom folder should be.
        temp.Write("custom", "in the way");
        var error = CustomStages.Write(temp.Path, "a", new StageDocument(Small, "A"));
        Assert.StartsWith("The stage couldn't be saved", error);
        Assert.Empty(CustomStages.List(temp.Path));
    }
}
