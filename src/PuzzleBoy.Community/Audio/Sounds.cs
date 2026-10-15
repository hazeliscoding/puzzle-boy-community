using Microsoft.Xna.Framework.Audio;

namespace PuzzleBoy.Community.Audio;

/// <summary>
/// Plays the synthesized effects and the music loop. With no audio device, or any audio failure, the game stays silent
/// instead of stopping.
/// </summary>
internal sealed class Sounds : IDisposable
{
    // The loop's level at full music volume, under the effects.
    private const float MusicLevel = 0.65f;

    private readonly Dictionary<Sfx, SoundEffect> _effects = [];
    private readonly SoundEffect? _music;
    private SoundEffectInstance? _loop;
    private float _musicVolume = 0.7f;
    private float _effectsVolume = 0.8f;

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
            // No audio device: play silently; the settings screen says so.
            Silent = true;
        }
    }

    /// <summary>True when there's no working audio device and nothing plays.</summary>
    public bool Silent { get; private set; }

    /// <summary>Plays <paramref name="sfx"/> once.</summary>
    public void Play(Sfx sfx)
    {
        if (!Silent && _effectsVolume > 0 && _effects.TryGetValue(sfx, out var effect))
        {
            Guard(() => effect.Play(_effectsVolume, 0, 0));
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
            _loop.Volume = _musicVolume * MusicLevel;
            _loop.Play();
        });
    }

    /// <summary>Stops the music loop, if it's playing; <see cref="StartMusic"/> starts it again from the top.</summary>
    public void StopMusic()
    {
        if (_loop is { } loop)
        {
            _loop = null;
            Guard(loop.Dispose);
        }
    }

    /// <summary>Sets the music and effects volumes, each 0 to 1; <paramref name="mute"/> silences both.</summary>
    public void SetVolumes(float music, float effects, bool mute)
    {
        _musicVolume = mute ? 0 : Math.Clamp(music, 0, 1);
        _effectsVolume = mute ? 0 : Math.Clamp(effects, 0, 1);
        if (_loop is { } loop)
        {
            Guard(() => loop.Volume = _musicVolume * MusicLevel);
        }
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
