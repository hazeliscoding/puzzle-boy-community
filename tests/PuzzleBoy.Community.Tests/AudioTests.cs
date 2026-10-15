using PuzzleBoy.Community.Audio;

namespace PuzzleBoy.Community.Tests;

public class AudioTests
{
    [Fact]
    public void Every_effect_is_short_audible_16_bit_audio()
    {
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            var pcm = Synth.Effect(sfx);
            Assert.True(pcm.Length > 0 && pcm.Length % 2 == 0, $"{sfx} is empty or not 16-bit");
            Assert.InRange(pcm.Length / 2.0 / Synth.SampleRate, 0.01, 0.5);
            Assert.Contains(pcm, b => b != 0);
        }
    }

    [Fact]
    public void The_music_loops_eight_bars_of_eighth_notes()
    {
        var eighth = (int)Math.Round(Synth.EighthSeconds * Synth.SampleRate);
        Assert.Equal(64 * eighth * 2, Synth.Music().Length);
    }
}
