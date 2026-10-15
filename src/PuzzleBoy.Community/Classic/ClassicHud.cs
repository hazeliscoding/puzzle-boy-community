using PuzzleBoy.Community.Play;
using PuzzleBoy.Rendering;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Classic;

/// <summary>What covers the board in the Classic theme's play, for the controls bar.</summary>
internal enum ClassicOverlay
{
    /// <summary>Nothing: the stage plays.</summary>
    None,

    /// <summary>The □ menu.</summary>
    Menu,

    /// <summary>A help page.</summary>
    Help,
}

/// <summary>
/// The original HUD's timing, for the shared <see cref="Hud"/>, as puzzle-boy's <c>PuzzleBoyGame</c> keeps it: UNDO for 30
/// ticks after an undo, COMPLETE! for 30 ticks from the clear, the scroll arrows' bob from the start of scroll mode,
/// and the controls bar fading in again whenever its set changes or a stage starts. One <see cref="Tick"/> a game tick.
/// </summary>
internal sealed class ClassicHud
{
    /// <summary>Ticks UNDO shows after an undo, and COMPLETE! after a clear.</summary>
    public const int ShowTicks = 30;

    private int _undo;
    private int _scroll;
    private int _bar;
    private BarSet _set = BarSet.None;
    private bool _scrolling;

    /// <summary>What the HUD shows now.</summary>
    public HudState State { get; private set; }

    /// <summary>Shows UNDO, from the tick the board went back.</summary>
    public void Undid() => _undo = ShowTicks;

    /// <summary>
    /// The stage number the counter shows: <paramref name="index"/> + 1, held at 99 since the counter has two digits.
    /// shortcut: a pack's stages past 99 show 99 (packs hold up to 200); the intro and the pause screen give the real one.
    /// </summary>
    public static int Counter(int index) => Math.Min(index + 1, 99);

    /// <summary>
    /// The scroll arrows to show for a scroll cursor at <paramref name="cursor"/> as <paramref name="camera"/> maps the
    /// screen: a bit per screen direction (1 up, 2 right, 4 down, 8 left) the cursor can still move, none out of scroll mode.
    /// </summary>
    public static int Arrows((int X, int Y)? cursor, CameraRig camera)
    {
        if (cursor is not { } at)
        {
            return 0;
        }

        var mask = 0;
        for (var screen = 0; screen < 4; screen++)
        {
            var (dx, dy) = camera.DirectionFor(screen).Offset();
            if (at.X + dx is >= 0 and < Stage.Width && at.Y + dy is >= 0 and < Stage.Height)
            {
                mask |= 1 << screen;
            }
        }

        return mask;
    }

    /// <summary>
    /// Counts one tick of <paramref name="session"/> under <paramref name="overlay"/>, and sets <see cref="State"/>. Called
    /// once a tick after play and the menu have run, so an undo made this tick already shows.
    /// </summary>
    public void Tick(PlaySession session, CameraRig camera, ClassicOverlay overlay)
    {
        var scrolling = session.Scroll is not null;
        _scroll = scrolling && _scrolling ? _scroll + 1 : 0;
        _scrolling = scrolling;

        var set = overlay switch
        {
            ClassicOverlay.Help => BarSet.Help,
            ClassicOverlay.Menu => BarSet.Menu,
            _ => scrolling ? BarSet.Scroll : BarSet.Play,
        };

        // The bar fades in again at a stage start or restart, and whenever it shows other keys.
        _bar = set != _set || session.Ticks <= 1 ? 0 : _bar + 1;
        _set = set;
        if (session.Ticks <= 1)
        {
            _undo = 0;
        }

        var clear = session.Board.IsCleared && session.ClearTicks is >= 1 and <= ShowTicks;
        State = new HudState(Counter(session.Index), clear, _undo > 0, Arrows(session.Scroll, camera), _scroll, set, _bar);

        // Counted down after it's shown, so an undo this tick shows for the next 30 draws, its own tick's included.
        _undo = Math.Max(0, _undo - 1);
    }
}
