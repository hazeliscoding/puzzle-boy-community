using Microsoft.Xna.Framework;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Nocturne;
using PuzzleBoy.Rendering;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The Classic theme's view of a stage in play, driven the way puzzle-boy's <c>PuzzleBoyGame</c> drives the shared
/// renderer: the original's follow camera, the step played with the original's timing, and one Jack-o'-Lantern per
/// character, posed each tick. It only reads the session; play itself is the same in both themes. Plain logic, so tests
/// run it without a graphics device.
/// </summary>
/// <param name="character">Jack-o'-Lantern, from the classic pack.</param>
internal sealed class ClassicStageView(CharacterData character)
{
    // The original orbits its model object, which sits 10 units (a quarter cell) below the character's position.
    private const float PivotHeight = -0.25f;

    private Rendering.MoveAnimation? _animation;
    private Play.MoveAnimation? _seen;
    private Rules.Board? _board;
    private CharacterModel[] _models = [];
    private bool _overhead;

    // The play camera's turn last seen, and the quarter turns it has made since that this camera hasn't: + right, - left.
    private int _seenMode;
    private int _owed;

    /// <summary>The original's camera, which follows the play camera's turns and overhead view.</summary>
    public ViewCamera Camera { get; } = new();

    /// <summary>The step being drawn with the original's timing, or null.</summary>
    public Rendering.MoveAnimation? Animation => _animation;

    /// <summary>
    /// One tick, after the session's: follows the play camera to <paramref name="mode"/> and <paramref name="overhead"/>,
    /// advances the step, and shakes the camera when a fall ends. Returns the original's sound effects this tick plays,
    /// by number: 3 when a fall starts and 9 when a character reaches the goal, on the ticks puzzle-boy plays them.
    /// </summary>
    public List<int> Tick(PlaySession session, int mode, bool overhead)
    {
        var sounds = new List<int>();
        Camera.Tick();
        Follow(session);
        _animation?.Tick();
        if (_animation is { FallStarts: true })
        {
            sounds.Add(3);
        }

        if (_animation is { FallEnds: true })
        {
            Camera.Quake();
        }

        if (_animation?.ArrivesAt(session.Board.Goal) == true)
        {
            sounds.Add(9);
        }

        if (_animation is { Done: true })
        {
            _animation = null;
        }

        // The play camera turns over 7 ticks; the original's camera over 9, or 12 overhead, so quick turns pile up here.
        // They're owed in the way the player turned, never made up the other way round. The controls follow the play
        // camera, so a step can start while this view is still turning, a few ticks behind overhead.
        var turned = (mode - _seenMode + 4) % 4;
        _owed += turned == 3 ? -1 : turned;
        _seenMode = mode;
        if (!Camera.IsBusy)
        {
            if (_owed != 0 && (_owed > 0 ? Camera.TurnRight() : Camera.TurnLeft()))
            {
                _owed -= Math.Sign(_owed);
            }
            else if (overhead != _overhead && Camera.ToggleAngle())
            {
                _overhead = overhead;
            }
        }

        return sounds;
    }

    /// <summary>
    /// Picks up a step the session started since the last look, or drops one undo took back. A step shows from its input
    /// tick, as in the original, so drawing calls this too.
    /// </summary>
    public void Follow(PlaySession session)
    {
        if (session.Animation is not { } step)
        {
            _animation = null;
        }
        else if (!ReferenceEquals(step, _seen))
        {
            _seen = step;
            _animation = new Rendering.MoveAnimation(step.Move, step.From);
        }
    }

    /// <summary>
    /// The view matrix: the original's camera on the active character, or on the scroll cursor in scroll mode, a quarter
    /// cell below it.
    /// </summary>
    public Matrix View(PlaySession session)
    {
        var (x, y) = session.Scroll is { } cursor ? (cursor.X, cursor.Y) : Rendering.MoveAnimation.Where(session.Board, _animation, session.Board.ActiveIndex);
        return Camera.View(new Vector3(x, PivotHeight, -y));
    }

    /// <summary>The original's projection: 60° vertical, near 0.05 and far 30 cells.</summary>
    public static Matrix Projection(float aspect) => Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), aspect, 0.05f, 30f);

    /// <summary>
    /// The characters on the board, posed for this tick: each faces the way it last tried to step, or grid -y toward the
    /// default camera before its first input, and the idle restarts with the stage.
    /// </summary>
    public List<CharacterModel> Pose(PlaySession session)
    {
        var board = session.Board;
        if (!ReferenceEquals(board, _board) || _models.Length != board.Characters.Count)
        {
            _board = board;
            _models = [.. board.Characters.Select(_ => new CharacterModel(character))];
        }

        var posed = new List<CharacterModel>();
        for (var i = 0; i < _models.Length; i++)
        {
            if (board.Characters[i].Visible || _animation?.Move.Mover == i)
            {
                var (x, y) = Rendering.MoveAnimation.Where(board, _animation, i);
                _models[i].Pose(session.Ticks, x, y, session.Facings[i] is { } facing ? 90 * (int)facing : 180);
                posed.Add(_models[i]);
            }
        }

        return posed;
    }
}
