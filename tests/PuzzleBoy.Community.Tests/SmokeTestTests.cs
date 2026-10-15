namespace PuzzleBoy.Community.Tests;

public class SmokeTestTests
{
    [Fact]
    public void The_smoke_test_passes_on_the_repo_content_and_moves_on_the_first_stage()
    {
        var output = new StringWriter();
        Assert.Equal(0, SmokeTest.Run(Repo.Content, output));
        var report = output.ToString();
        Assert.Contains("content: 1 pack(s), 3 stage(s), 4 fonts", report);
        Assert.Contains("stage first-steps/walk-to-the-goal: PlusX walked from (1, 1) to (2, 1)", report);
        Assert.EndsWith("smoke test passed" + Environment.NewLine, report);
    }

    [Fact]
    public void The_smoke_test_fails_with_the_reason_when_a_stage_is_broken()
    {
        using var temp = TempFolder.WithContent();
        temp.Write("stages/first-steps/first-hole.json", "{}");
        var output = new StringWriter();
        Assert.Equal(1, SmokeTest.Run(temp.Path, output));
        Assert.Contains("problem: stages/first-steps, stage \"first-hole\": ", output.ToString());
        Assert.EndsWith("smoke test failed" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void The_smoke_test_fails_rather_than_crashing_when_content_is_missing()
    {
        using var temp = new TempFolder();
        var output = new StringWriter();
        Assert.Equal(1, SmokeTest.Run(Path.Combine(temp.Path, "content"), output));
        Assert.EndsWith("smoke test failed" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void The_smoke_test_fails_when_a_font_is_damaged()
    {
        using var temp = TempFolder.WithContent();
        var font = Path.Combine(temp.Path, "fonts", "AtkinsonHyperlegible-Regular.ttf");
        File.WriteAllBytes(font, [.. File.ReadAllBytes(font).Take(64)]);
        var output = new StringWriter();
        Assert.Equal(1, SmokeTest.Run(temp.Path, output));
        Assert.Contains("problem: font \"body\": ", output.ToString());
    }
}
