using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rendering;
using Sfx = PuzzleBoy.Community.Audio.Sfx;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// Draws and sounds a stage in play in the Classic theme, through the engine's shared renderer as puzzle-boy does:
/// <see cref="BoardView"/> for the atlas-textured board, glass walls, outlines, goal, backdrop and Jack-o'-Lantern,
/// <see cref="Sounds"/> for the original's effects and music, and with a schema 2 pack, the shared <see cref="Hud"/> for
/// the original's HUD and help page and a <see cref="ClassicSkin"/> for the rest of the interface. With a schema 1 pack,
/// Community's HUD, pause and clear screens draw over the board instead.
/// </summary>
internal sealed class ClassicPresenter : IDisposable
{
    private readonly BoardView _board;
    private readonly Sounds _effects;
    private readonly Dictionary<string, Nocturne.SoundData> _musicClips;
    private readonly Hud? _hud;
    private Sounds? _music;

    /// <summary>Uploads <paramref name="assets"/> to <paramref name="device"/>.</summary>
    public ClassicPresenter(GraphicsDevice device, ClassicAssets assets)
    {
        Assets = assets;
        _board = new BoardView(device, assets.Atlas, assets.Background, assets.Character);

        // The engine's Sounds can't stop its music, so the music gets an instance of its own to dispose when play ends.
        _musicClips = assets.Sounds.Where(s => s.Key.Contains("_BGM", StringComparison.Ordinal)).ToDictionary();
        _effects = new Sounds(assets.Sounds.Where(s => !_musicClips.ContainsKey(s.Key)).ToDictionary());
        if (assets.Ui is { } ui)
        {
            _hud = new Hud(device, ui.Sheets, ui.Text);
            Skin = new ClassicSkin(device, ui, assets.Background, assets.Character);
        }
    }

    /// <summary>The Nocturne-styled interface for Community's screens, or null when the pack has none.</summary>
    public ClassicSkin? Skin { get; }

    /// <summary>True when play draws the original's HUD, □ menu and help page rather than Community's.</summary>
    public bool HasHud => _hud is not null;

    /// <summary>The original HUD's timing for the stage in play; the game ticks it.</summary>
    public ClassicHud Clock { get; } = new();

    /// <summary>The assets it draws with.</summary>
    public ClassicAssets Assets { get; }

    /// <summary>
    /// The original's effect for a Community sound, by number (puzzle-boy's <c>PuzzleBoyGame</c>): 0 step, 1 push, 2 turn,
    /// 4 confirm, 5 cancel, 6 cursor, 7 refused, 8 view. Null for a fall, the goal and a clear: <see cref="ClassicStageView"/>
    /// times the fall and the goal as the original does, and the original has no clear sound.
    /// </summary>
    public static int? Effect(Sfx sfx) => sfx switch
    {
        Sfx.Step => 0,
        Sfx.Push => 1,
        Sfx.Turn => 2,
        Sfx.Confirm => 4,
        Sfx.Cancel => 5,
        Sfx.MenuMove => 6,
        Sfx.Buzz => 7,
        Sfx.View => 8,
        _ => null,
    };

    /// <summary>Draws <paramref name="session"/> as <paramref name="stage"/> sees it, on a viewport of <paramref name="aspect"/>.</summary>
    /// <param name="session">The stage in play.</param>
    /// <param name="stage">The camera, step and characters.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <param name="ticks">Ticks since the game started, for the goal's spin.</param>
    public void Draw(PlaySession session, ClassicStageView stage, float aspect, int ticks)
    {
        stage.Follow(session);
        _board.Draw(session.Board, stage.Animation, stage.View(session), ClassicStageView.Projection(aspect), ticks, stage.Pose(session), stage.Camera.Yaw);
    }

    /// <summary>
    /// Draws the original's HUD as <see cref="Clock"/> last set it: the stage counter, COMPLETE!, UNDO, the scroll arrows
    /// and the controls bar. Nothing without a HUD.
    /// </summary>
    public void DrawHud() => _hud?.Draw(Clock.State);

    /// <summary>Draws the original's rules page. Nothing without a HUD.</summary>
    public void DrawHelp() => _hud?.DrawHelp();

    /// <summary>Plays the original's effect <paramref name="number"/>.</summary>
    public void Play(int number) => _effects.Play(number);

    /// <summary>
    /// Sets the level of everything the theme plays: the original plays at full volume, so the effects volume (or mute)
    /// scales it all through MonoGame's master volume. Community's own sounds pass their volume per sound, so they're
    /// unaffected once <see cref="Leave"/> restores it.
    /// </summary>
    /// <param name="effects">The effects volume, 0 to 1.</param>
    /// <param name="music">The music volume, 0 to 1; 0 leaves the music off.</param>
    /// <param name="mute">True silences everything.</param>
    // shortcut: the music follows the effects volume, since the engine's Sounds has no volume of its own; Music volume 0
    // turns it off. Give Sounds a volume when players ask for separate levels in the Classic theme.
    public void Enter(float effects, float music, bool mute)
    {
        SetMaster(mute ? 0 : effects);
        if (!mute && music > 0)
        {
            _music ??= new Sounds(_musicClips);
            _music.StartMusic();
        }
    }

    /// <summary>Stops the music and gives MonoGame's master volume back to Community's sounds.</summary>
    public void Leave()
    {
        _music?.Dispose();
        _music = null;
        SetMaster(1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Leave();
        _board.Dispose();
        _effects.Dispose();
        _hud?.Dispose();
        Skin?.Dispose();
    }

    private static void SetMaster(float volume)
    {
        try
        {
            SoundEffect.MasterVolume = volume;
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            // No audio device: there's nothing to set.
        }
    }
}
