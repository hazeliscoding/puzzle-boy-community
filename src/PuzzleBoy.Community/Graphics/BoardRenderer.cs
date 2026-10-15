using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Graphics;

/// <summary>
/// Draws a stage in play as lit boxes: a checkered floor, walls around the play area, holes as dark pits, blocks in
/// their group's color and pattern, swivels with a raised center, a glowing goal and Pixel Jack for each character.
/// Grid (x, y) is world (x, height, -y), in cells.
/// </summary>
internal sealed class BoardRenderer : IDisposable
{
    private const float FloorDepth = 1.1f;
    // Low walls, so they never hide a character standing behind them.
    private const float WallHeight = 0.12f;
    private const float BlockHeight = 0.84f;
    private const float ArmHeight = 0.6f;
    private const float CenterHeight = 0.95f;

    private readonly GraphicsDevice _device;
    private readonly Theme _theme;
    private readonly Texture2D _atlas;
    private readonly BasicEffect _effect;
    private readonly Mesh _jack;
    private readonly Mesh _solid = new();
    private readonly Mesh _glow = new();
    private Color _floorA;
    private Color _floorB;
    private Color _wall;
    private Color _wallTop;
    private Color _pit;

    /// <summary>Makes the texture, the effect and Pixel Jack's figure.</summary>
    public BoardRenderer(GraphicsDevice device, Theme theme)
    {
        _device = device;
        _theme = theme;
        _atlas = Atlas.Create(device);
        _effect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, Texture = _atlas };
        _jack = JackModel.Build(theme);
        HighContrast = false;
    }

    /// <summary>The texture of box faces and patterns, for 2D drawings that match the board, such as stage previews.</summary>
    public Texture2D AtlasTexture => _atlas;

    /// <summary>
    /// True draws a flat, lighter floor, black pits and ink walls, so the play area stands out more; false the usual
    /// checkered look.
    /// </summary>
    public bool HighContrast
    {
        set
        {
            // The floor is the brightest plane, so the play area reads at a glance against the darker walls.
            var t = _theme;
            _floorA = Color.Lerp(t["hatOnDark"], t["cream"], value ? 0.66f : 0.5f);
            _floorB = value ? _floorA : Color.Lerp(t["hatOnDark"], t["cream"], 0.42f);
            _wall = value ? t["ink"] : Color.Lerp(t["hat"], t["night"], 0.3f);
            _wallTop = value ? Color.Lerp(t["ink"], t["hatOnDark"], 0.3f) : t["hatOnDark"];
            _pit = value ? Color.Black : Color.Lerp(t["night"], Color.Black, 0.6f);
        }
    }

    /// <summary>Pixel Jack's figure in model space, for screens that show him off the board.</summary>
    public Mesh Jack => _jack;

    /// <summary>The play area's bounds: the smallest rectangle holding every cell that isn't wall.</summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) PlayBounds(Board board)
    {
        var (minX, minY, maxX, maxY) = (Stage.Width, Stage.Height, -1, -1);
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                if (Cell.KindOf(board[x, y]) != CellKind.Wall)
                {
                    (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
                }
            }
        }

        return maxX < 0 ? (0, 0, Stage.Width - 1, Stage.Height - 1) : (minX, minY, maxX, maxY);
    }

    /// <summary>Draws <paramref name="session"/>'s board.</summary>
    public void Draw(PlaySession session, Matrix view, Matrix projection, int ticks)
    {
        var board = session.Board;
        var animation = session.Animation;
        _solid.Clear();
        _glow.Clear();

        // The cells of the group the current step moves are drawn apart while it plays.
        HashSet<(int X, int Y)> moving = animation is { } a && a.Move.Result != StepResult.Walked && (!a.Move.Fell || a.Falling) ? [.. a.Move.Landings] : [];
        var opening = animation is { Falling: true };
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                AddCell(board, x, y, moving.Contains((x, y)), opening && moving.Contains((x, y)));
            }
        }

        foreach (var landing in moving)
        {
            var value = board[landing.X, landing.Y];
            var (px, py, turn) = animation!.PartAt(landing);
            var spin = Matrix.CreateRotationY(turn * MathF.PI / 2);
            var place = spin * Matrix.CreateTranslation(px, -animation.Sink * BlockHeight, -py);

            // A dropped block's landing cells are floor now; draw what was there.
            var kind = animation.Move.Fell ? CellKind.Block : Cell.KindOf(value);
            AddPart(board, landing.X, landing.Y, kind, animation.Move.Group, place, !animation.Move.Fell);
        }

        AddGoal(board.Goal, ticks, board.IsCleared);
        AddCharacters(session, ticks);

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = Matrix.Identity;
        _effect.FogEnabled = false;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _solid.Draw(_device, _effect);

        // Glows add light over what's drawn and don't hide each other.
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.BlendState = BlendState.Additive;
        _glow.Draw(_device, _effect);
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.Default;
    }

    /// <summary>Draws <paramref name="mesh"/> placed by <paramref name="world"/>, for figures and props outside a board.</summary>
    public void DrawMesh(Mesh mesh, Matrix world, Matrix view, Matrix projection, Color tint, Color? fog = null)
    {
        _effect.World = world;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.DiffuseColor = tint.ToVector3();
        _effect.FogEnabled = fog is not null;
        if (fog is { } color)
        {
            _effect.FogColor = color.ToVector3();
            _effect.FogStart = 4;
            _effect.FogEnd = 16;
        }

        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        mesh.Draw(_device, _effect);
        _effect.World = Matrix.Identity;
        _effect.DiffuseColor = Vector3.One;
        _effect.FogEnabled = false;
        _device.BlendState = BlendState.AlphaBlend;
    }

    /// <summary>The color of block group <paramref name="group"/>.</summary>
    public Color GroupColor(int group) => _theme.GroupColors[(group - 1) % _theme.GroupColors.Count];

    /// <summary>
    /// The top pattern of block group <paramref name="group"/>. Groups 1 to 8 each have their own, so within a bundled
    /// stage the pattern alone tells groups apart and color is never the only cue.
    /// </summary>
    public static int GroupPattern(int group) => Atlas.FirstPattern + ((group - 1) % Atlas.PatternCount);

    /// <inheritdoc />
    public void Dispose()
    {
        _atlas.Dispose();
        _effect.Dispose();
    }

    private void AddCell(Board board, int x, int y, bool moving, bool open)
    {
        var value = board[x, y];
        var kind = Cell.KindOf(value);
        var hole = (value & Cell.HoleFlag) != 0 || open;
        if (kind == CellKind.Wall)
        {
            if (NearPlay(board, x, y))
            {
                _solid.AddBox(new(x - 0.5f, -FloorDepth, -y - 0.5f), new(x + 0.5f, WallHeight, -y + 0.5f), _wall, Atlas.Plain, Atlas.Plain, Matrix.Identity, _wallTop);
            }

            return;
        }

        if (hole)
        {
            // A pit: its floor far down; the neighbors' floor boxes make its walls.
            _solid.AddQuad(new(x - 0.5f, -FloorDepth, -y + 0.5f), new(x + 0.5f, -FloorDepth, -y + 0.5f), new(x + 0.5f, -FloorDepth, -y - 0.5f), new(x - 0.5f, -FloorDepth, -y - 0.5f), _pit, Atlas.White, false);
        }
        else
        {
            var tone = (x + y) % 2 == 0 ? _floorA : _floorB;
            _solid.AddBox(new(x - 0.5f, -FloorDepth, -y - 0.5f), new(x + 0.5f, 0, -y + 0.5f), tone, Atlas.Plain, Atlas.Plain, Matrix.Identity);
        }

        if (!moving && kind is CellKind.Block or CellKind.SwivelCenter or CellKind.SwivelArm)
        {
            AddPart(board, x, y, kind, Cell.GroupOf(value), Matrix.CreateTranslation(x, 0, -y), true);
        }
    }

    /// <summary>A block or swivel part, built around the origin and placed by <paramref name="place"/>.</summary>
    private void AddPart(Board board, int x, int y, CellKind kind, int group, Matrix place, bool joinNeighbors)
    {
        var color = GroupColor(group);
        switch (kind)
        {
            case CellKind.SwivelCenter:
                _solid.AddBox(new(-0.36f, 0, -0.36f), new(0.36f, CenterHeight, 0.36f), Color.Lerp(color, _theme["ink"], 0.45f), Atlas.Pivot, Atlas.Plain, place, color);
                _solid.AddBox(new(-0.08f, CenterHeight, -0.08f), new(0.08f, CenterHeight + 0.14f, 0.08f), _theme["cream"], Atlas.White, Atlas.White, place);
                break;
            case CellKind.SwivelArm:
                _solid.AddBox(new(-0.42f, 0, -0.42f), new(0.42f, ArmHeight, 0.42f), color, GroupPattern(group), Atlas.Plain, place);
                break;
            default:
                // Cells of one block meet without a gap, so a block reads as one piece.
                var inset = 0.05f;
                float Side(int dx, int dy) => joinNeighbors && Same(board, x + dx, y + dy, group) ? 0.5f : 0.5f - inset;
                _solid.AddBox(new(-Side(-1, 0), 0, -Side(0, 1)), new(Side(1, 0), BlockHeight, Side(0, -1)), color, GroupPattern(group), Atlas.Plain, place);
                break;
        }
    }

    private void AddGoal((int X, int Y) goal, int ticks, bool cleared)
    {
        var (gx, gz) = ((float)goal.X, (float)-goal.Y);
        var ember = _theme["ember"];
        var pulse = 0.75f + (0.25f * MathF.Sin(ticks * 0.15f));
        _glow.AddFlat(gx, 0.02f, gz, 2.4f, ember * (0.8f * pulse), Atlas.Glow);
        _glow.AddFlat(gx, 0.03f, gz, 0.95f, ember * 0.9f, Atlas.Ring);

        // A spinning gem floats over the goal; it rises and spins fast once the stage is cleared.
        var height = 0.7f + (0.06f * MathF.Sin(ticks * 0.1f)) + (cleared ? 0.4f : 0);
        var spin = Matrix.CreateRotationY(ticks * (cleared ? 0.25f : 0.05f)) * Matrix.CreateTranslation(gx, height, gz);
        Vector3 P(float x, float y, float z) => Vector3.Transform(new Vector3(x, y, z), spin);
        var (top, bottom) = (P(0, 0.36f, 0), P(0, -0.36f, 0));
        Vector3[] ring = [P(0.25f, 0, 0), P(0, 0, 0.25f), P(-0.25f, 0, 0), P(0, 0, -0.25f)];
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (ring[i], ring[(i + 1) % 4]);
            _solid.AddTriangle(top, b, a, i % 2 == 0 ? ember : _theme["pumpkin"]);
            _solid.AddTriangle(bottom, a, b, i % 2 == 0 ? _theme["pumpkin"] : _theme["pumpkinShade"]);
        }

        _glow.AddFlat(gx, height, gz, 1.3f, ember * 0.35f, Atlas.Glow);
    }

    private void AddCharacters(PlaySession session, int ticks)
    {
        var board = session.Board;
        var several = board.Characters.Count > 1;
        for (var i = 0; i < board.Characters.Count; i++)
        {
            if (!board.Characters[i].Visible && session.Animation?.Move.Mover != i)
            {
                continue;
            }

            var (x, y, hop) = session.Where(i);
            var active = i == board.ActiveIndex && !board.IsCleared;
            var idle = MathF.Sin((ticks + (i * 11)) * 0.12f);
            var cheer = board.IsCleared ? MathF.Abs(MathF.Sin(session.ClearTicks * 0.22f)) * 0.35f : 0;
            var yaw = Facing(session.Facings[i]) + (board.IsCleared ? session.ClearTicks * 0.19f : idle * 0.05f);
            var squash = 1 + (idle * 0.025f);
            var world = Matrix.CreateScale(1 / squash, squash, 1 / squash) * Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(x, hop + cheer, -y);

            // Waiting characters are dimmer, so the one in control stands out.
            var tint = !several || active ? Color.White : new Color(0.55f, 0.55f, 0.6f);
            AddTinted(_jack, world, tint);
            if (active && several)
            {
                // Violet, unlike the goal's ember ring, so the Jack in control never looks as if he stands on the goal.
                _glow.AddFlat(x, 0.04f, -y, 1.15f, _theme["bandOnDark"] * (0.8f + (0.2f * idle)), Atlas.Ring);
            }

            // A soft shadow under each figure.
            _glow.AddFlat(x, 0.035f, -y, 1.0f, _theme["ember"] * 0.3f, Atlas.Glow);
        }
    }

    private void AddTinted(Mesh mesh, Matrix world, Color tint)
    {
        var start = _solid.Count;
        _solid.AddMesh(mesh, world);
        if (tint != Color.White)
        {
            _solid.Tint(start, tint);
        }
    }

    // Facing the starting camera (grid -y, world +z) before the first input.
    private static float Facing(Direction? facing) => facing switch
    {
        Direction.PlusY => MathF.PI,
        Direction.PlusX => MathF.PI / 2,
        Direction.MinusX => -MathF.PI / 2,
        _ => 0,
    };

    private static bool NearPlay(Board board, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var (nx, ny) = (x + dx, y + dy);
                if (nx is >= 0 and < Stage.Width && ny is >= 0 and < Stage.Height && Cell.KindOf(board[nx, ny]) != CellKind.Wall)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Same(Board board, int x, int y, int group) =>
        x is >= 0 and < Stage.Width && y is >= 0 and < Stage.Height && Cell.KindOf(board[x, y]) == CellKind.Block && Cell.GroupOf(board[x, y]) == group;
}
