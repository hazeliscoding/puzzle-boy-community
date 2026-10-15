using Microsoft.Xna.Framework.Audio;

namespace PuzzleBoy.Community.Audio;

/// <summary>
/// Plays the synthesized effects and the music loop. With no audio device, or any audio failure, the game stays silent
/// instead of stopping.
/// </summary>
internal sealed class Sounds : IDisposable
{
    private const float MusicVolume = 0.45f;

    private readonly Dictionary<Sfx, SoundEffect> _effects = [];
    private readonly SoundEffect? _music;
    private SoundEffectInstance? _loop;

    /// <summary>Renders every sound and hands it to the audio device, if there is one.</summary>
    public Sounds()
    {
        try
        {
            foreach (var sfx in Enum.GetValues<Sfx>())
            {
                _effects[sfx] = new SoundEffect(Synth.Effect(sfx), Synth.SampleRate, AudioChannels.Mono);
            }

            _music = new SoundEffect(Synth.Music(), Synth.SampleRate, AudioChannels.Mono);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            // shortcut: silence with no message when there's no audio device; settings (C2) can say so.
            Silent = true;
        }
    }

    /// <summary>True when there's no working audio device and nothing plays.</summary>
    public bool Silent { get; private set; }

    /// <summary>Plays <paramref name="sfx"/> once.</summary>
    public void Play(Sfx sfx)
    {
        if (!Silent && _effects.TryGetValue(sfx, out var effect))
        {
            Guard(() => effect.Play(0.8f, 0, 0));
        }
    }

    /// <summary>Starts the music loop, if it isn't playing.</summary>
    public void StartMusic()
    {
        if (Silent || _music is null || _loop is not null)
        {
            return;
        }

        Guard(() =>
        {
            _loop = _music.CreateInstance();
            _loop.IsLooped = true;
            _loop.Volume = MusicVolume;
            _loop.Play();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loop?.Dispose();
        _music?.Dispose();
        foreach (var effect in _effects.Values)
        {
            effect.Dispose();
        }
    }

    private void Guard(Action play)
    {
        try
        {
            play();
        }
        catch (InstancePlayLimitException)
        {
            // Too many sounds at once: this one is dropped.
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException)
        {
            // A device lost mid-game: carry on in silence.
            Silent = true;
        }
    }
}
