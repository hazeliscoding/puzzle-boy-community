namespace PuzzleBoy.Community.Audio;

/// <summary>The sound effects, all synthesized at startup.</summary>
internal enum Sfx
{
    /// <summary>A walking step.</summary>
    Step,

    /// <summary>A block pushed.</summary>
    Push,

    /// <summary>A swivel turned.</summary>
    Turn,

    /// <summary>A block sinking into holes.</summary>
    Fall,

    /// <summary>A character reaching the goal.</summary>
    Goal,

    /// <summary>The stage cleared.</summary>
    Clear,

    /// <summary>The view turning or tilting, or control passing to another character.</summary>
    View,

    /// <summary>Menu focus moving.</summary>
    MenuMove,

    /// <summary>A menu choice made.</summary>
    Confirm,

    /// <summary>Going back or closing.</summary>
    Cancel,

    /// <summary>Something that can't be done right now, such as an undo with nothing to undo.</summary>
    Buzz,
}

/// <summary>
/// Chiptune synthesis: square, triangle and noise voices with short envelopes, rendered to 16-bit mono PCM. No samples
/// are read from anywhere; every sound is made here, so the default game ships no audio files.
/// </summary>
internal static class Synth
{
    /// <summary>The sample rate of everything rendered, in Hz.</summary>
    public const int SampleRate = 22050;

    /// <summary>The music's tempo, in beats a minute.</summary>
    public const int Tempo = 132;

    // The tune, in eighth notes: MIDI note numbers, 0 holds the previous note, -1 rests. A minor, 8 bars, written for
    // this game.
    private static readonly int[] Lead =
    [
        69, 72, 76, 81, 79, 76, 72, 76,
        77, 76, 74, 72, 74, 0, 71, 0,
        69, 72, 76, 81, 83, 81, 79, 76,
        77, 0, 76, 0, 74, 76, 72, 0,
        74, 77, 81, 86, 84, 81, 77, 81,
        79, 77, 76, 74, 76, 0, 72, 0,
        71, 74, 76, 80, 83, 81, 80, 76,
        81, 0, 76, 0, 69, 0, 0, -1,
    ];

    // One bass root per bar: A, F, A, E, D, C, E, A.
    private static readonly int[] BassRoots = [45, 41, 45, 40, 38, 48, 40, 45];

    private enum Wave
    {
        Square,
        Pulse,
        Triangle,
        Noise,
    }

    /// <summary>The seconds one eighth note lasts.</summary>
    public static double EighthSeconds => 60.0 / Tempo / 2;

    /// <summary>The music loop, 8 bars long, as 16-bit little-endian mono PCM at <see cref="SampleRate"/>.</summary>
    public static byte[] Music()
    {
        var eighth = (int)Math.Round(EighthSeconds * SampleRate);
        var buffer = new float[eighth * Lead.Length];
        for (var i = 0; i < Lead.Length; i++)
        {
            if (Lead[i] <= 0)
            {
                continue;
            }

            var length = 1;
            while (i + length < Lead.Length && Lead[i + length] == 0)
            {
                length++;
            }

            Mix(buffer, i * eighth, Note(Lead[i]), Note(Lead[i]), (length * eighth) - 200, Wave.Pulse, 0.16f, 0.004f, 0.35f);
        }

        for (var bar = 0; bar < BassRoots.Length; bar++)
        {
            int[] steps = [0, 12, 7, 12, 0, 12, 7, 12];
            for (var step = 0; step < steps.Length; step++)
            {
                var start = ((bar * 8) + step) * eighth;
                var note = Note(BassRoots[bar] + steps[step]);
                Mix(buffer, start, note, note, eighth - 300, Wave.Triangle, 0.30f, 0.003f, 0.8f);

                // A soft hat on every off-beat.
                if (step % 2 == 1)
                {
                    Mix(buffer, start, 8000, 8000, eighth / 6, Wave.Noise, 0.05f, 0.001f, 1f);
                }
            }
        }

        return ToPcm(buffer);
    }

    /// <summary>Sound effect <paramref name="sfx"/> as 16-bit little-endian mono PCM at <see cref="SampleRate"/>.</summary>
    public static byte[] Effect(Sfx sfx)
    {
        var buffer = new float[SampleRate / 2];
        var length = sfx switch
        {
            Sfx.Step => Mix(buffer, 0, 520, 380, Ms(45), Wave.Triangle, 0.35f, 0.002f, 1f),
            Sfx.Push => Math.Max(
                Mix(buffer, 0, 150, 90, Ms(110), Wave.Square, 0.22f, 0.002f, 1f),
                Mix(buffer, 0, 1200, 1200, Ms(60), Wave.Noise, 0.12f, 0.001f, 1f)),
            Sfx.Turn => Mix(buffer, 0, 300, 640, Ms(130), Wave.Pulse, 0.2f, 0.004f, 0.8f),
            Sfx.Fall => Math.Max(
                Mix(buffer, 0, 420, 70, Ms(320), Wave.Square, 0.2f, 0.004f, 0.7f),
                Mix(buffer, Ms(180), 600, 600, Ms(140), Wave.Noise, 0.14f, 0.002f, 1f)),
            Sfx.Goal => Arpeggio(buffer, [72, 76, 79, 84], Ms(55), 0.18f),
            Sfx.Clear => Arpeggio(buffer, [72, 76, 79, 84, 88, 91], Ms(60), 0.18f),
            Sfx.View => Mix(buffer, 0, 700, 900, Ms(50), Wave.Triangle, 0.22f, 0.002f, 1f),
            Sfx.MenuMove => Mix(buffer, 0, 880, 880, Ms(30), Wave.Pulse, 0.12f, 0.001f, 1f),
            Sfx.Confirm => Arpeggio(buffer, [76, 83], Ms(45), 0.15f),
            Sfx.Cancel => Arpeggio(buffer, [83, 76], Ms(45), 0.13f),
            _ => Mix(buffer, 0, 110, 104, Ms(160), Wave.Square, 0.18f, 0.002f, 0.6f),
        };

        return ToPcm(buffer.AsSpan(0, Math.Min(length + Ms(5), buffer.Length)));
    }

    private static int Ms(int milliseconds) => SampleRate * milliseconds / 1000;

    private static float Note(int midi) => 440f * MathF.Pow(2, (midi - 69) / 12f);

    private static int Arpeggio(float[] buffer, int[] notes, int each, float volume)
    {
        var end = 0;
        for (var i = 0; i < notes.Length; i++)
        {
            var hold = i == notes.Length - 1 ? each * 3 : each;
            end = Math.Max(end, Mix(buffer, i * each, Note(notes[i]), Note(notes[i]), hold, Wave.Pulse, volume, 0.002f, 0.6f));
        }

        return end;
    }

    /// <summary>
    /// Adds a voice to <paramref name="buffer"/> from <paramref name="start"/>, gliding from one frequency to another
    /// over <paramref name="length"/> samples, with a linear attack and an exponential fade to silence at the end.
    /// Returns the sample where it ends.
    /// </summary>
    private static int Mix(float[] buffer, int start, float fromHz, float toHz, int length, Wave wave, float volume, float attackSeconds, float sustain)
    {
        var attack = Math.Max(1, (int)(attackSeconds * SampleRate));
        var phase = 0.0;
        uint noise = 0xACE1u;
        var held = 0f;
        var end = Math.Min(start + length, buffer.Length);
        for (var i = start; i < end; i++)
        {
            var t = (i - start) / (float)length;
            var hz = fromHz + ((toHz - fromHz) * t);
            var before = phase;
            phase = (phase + (hz / SampleRate)) % 1.0;
            var value = wave switch
            {
                Wave.Square => phase < 0.5 ? 1f : -1f,
                Wave.Pulse => phase < 0.25 ? 1f : -1f,
                Wave.Triangle => (float)(phase < 0.5 ? (phase * 4) - 1 : 3 - (phase * 4)),
                _ => held,
            };

            if (wave == Wave.Noise && phase < before)
            {
                // A 16-bit LFSR, clocked at the voice's frequency like a chip's noise channel.
                noise = (noise >> 1) ^ (uint)(-(int)(noise & 1) & 0xB400);
                held = (noise & 1) == 0 ? 1f : -1f;
            }

            // Hold at full, then fade over the rest; sustain is the share of the length held.
            var envelope = Math.Min(1f, (i - start) / (float)attack) * (t < sustain ? 1f : MathF.Pow(1 - ((t - sustain) / (1 - sustain + 1e-6f)), 2));
            buffer[i] += value * volume * envelope;
        }

        return end;
    }

    private static byte[] ToPcm(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)value;
            bytes[(i * 2) + 1] = (byte)(value >> 8);
        }

        return bytes;
    }
}
