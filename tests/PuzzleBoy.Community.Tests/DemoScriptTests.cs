using Microsoft.Xna.Framework.Input;

namespace PuzzleBoy.Community.Tests;

public class DemoScriptTests
{
    [Fact]
    public void A_script_reads_as_waits_presses_holds_and_moves_with_comments_skipped()
    {
        var steps = DemoRecorder.Parse("""
            # the title idles
            wait 60

            press down ENTER   # stage select
            hold LeftControl+Z 3
            play RRDUS
            """);

        Assert.Equal(7, steps.Count);
        Assert.Empty(steps[0].Keys);
        Assert.Equal(60, steps[0].Ticks);
        Assert.Equal([Keys.Down], steps[1].Keys);
        Assert.Empty(steps[2].Keys);
        Assert.Equal(1, steps[2].Ticks);
        Assert.Equal([Keys.Enter], steps[3].Keys);
        Assert.Equal([Keys.LeftControl, Keys.Z], steps[5].Keys);
        Assert.Equal(3, steps[5].Ticks);
        Assert.Equal("RRDUS", steps[6].Moves);
        Assert.All(steps[..6], s => Assert.Null(s.Moves));
    }

    [Theory]
    [InlineData("wait", 1)]
    [InlineData("wait 0", 1)]
    [InlineData("wait -3", 1)]
    [InlineData("press Banana", 1)]
    [InlineData("press 13", 1)]
    [InlineData("hold Up+ 4", 1)]
    [InlineData("hold Up", 1)]
    [InlineData("play RRX", 1)]
    [InlineData("wait 5\n\njump", 3)]
    public void A_line_the_recorder_cant_play_is_refused_by_its_number(string script, int line)
    {
        var e = Assert.Throws<FormatException>(() => DemoRecorder.Parse(script));
        Assert.StartsWith($"line {line}:", e.Message, StringComparison.Ordinal);
    }
}
