using Microsoft.Xna.Framework;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Editor;

/// <summary>
/// The editor canvas's pan and zoom: where each cell of the 20×20 grid lands in a box of the virtual screen. Row 0 is
/// at the bottom, so +y points up the screen as in play. At the first zoom the whole grid fits; closer zooms pan, and
/// the grid never leaves the box entirely.
/// </summary>
/// <param name="box">The canvas, in virtual-screen pixels.</param>
internal sealed class EditorView(RectangleF box)
{
    /// <summary>The zoom levels, as multiples of <see cref="BaseCell"/>.</summary>
    public static readonly float[] Zooms = [1f, 1.5f, 2f, 3f];

    private int _zoom;
    private Vector2 _origin = Centered(box, MathF.Floor(Math.Min(box.Width, box.Height) / Stage.Height));

    /// <summary>A cell's side at the first zoom, in virtual pixels: the grid's height fits the box.</summary>
    public float BaseCell => MathF.Floor(Math.Min(Box.Width, Box.Height) / Stage.Height);

    /// <summary>The canvas, in virtual-screen pixels.</summary>
    public RectangleF Box { get; } = box;

    /// <summary>The zoom level, an index into <see cref="Zooms"/>.</summary>
    public int Zoom => _zoom;

    /// <summary>A cell's side at the current zoom, in virtual pixels.</summary>
    public float Cell => BaseCell * Zooms[_zoom];

    /// <summary>The top-left corner of cell (<paramref name="x"/>, <paramref name="y"/>) on the virtual screen.</summary>
    public Vector2 CellCorner(int x, int y) => new(_origin.X + (x * Cell), _origin.Y + ((Stage.Height - 1 - y) * Cell));

    /// <summary>The cell under virtual-screen point <paramref name="point"/>, or null outside the box or the grid.</summary>
    public (int X, int Y)? CellAt(Vector2 point)
    {
        if (!Box.Contains(point))
        {
            return null;
        }

        var x = (int)MathF.Floor((point.X - _origin.X) / Cell);
        var y = Stage.Height - 1 - (int)MathF.Floor((point.Y - _origin.Y) / Cell);
        return x is >= 0 and < Stage.Width && y is >= 0 and < Stage.Height ? (x, y) : null;
    }

    /// <summary>
    /// Zooms <paramref name="by"/> levels, keeping the grid point under <paramref name="around"/> where it is. False when
    /// already at that end.
    /// </summary>
    public bool ZoomBy(int by, Vector2 around)
    {
        var zoom = Math.Clamp(_zoom + by, 0, Zooms.Length - 1);
        if (zoom == _zoom)
        {
            return false;
        }

        var grid = (around - _origin) / Cell;
        _zoom = zoom;
        _origin = around - (grid * Cell);
        Clamp();
        return true;
    }

    /// <summary>
    /// Zooms as close as shows cells (<paramref name="minX"/>, <paramref name="minY"/>) to (<paramref name="maxX"/>,
    /// <paramref name="maxY"/>) whole with a cell to spare around them, and centers them.
    /// </summary>
    public void Frame(int minX, int minY, int maxX, int maxY)
    {
        _zoom = 0;
        for (var zoom = Zooms.Length - 1; zoom > 0; zoom--)
        {
            var cell = BaseCell * Zooms[zoom];
            if ((maxX - minX + 3) * cell <= Box.Width && (maxY - minY + 3) * cell <= Box.Height)
            {
                _zoom = zoom;
                break;
            }
        }

        var middle = new Vector2((minX + maxX + 1) / 2f, Stage.Height - ((minY + maxY + 1) / 2f));
        _origin = new Vector2(Box.X + (Box.Width / 2), Box.Y + (Box.Height / 2)) - (middle * Cell);
        Clamp();
    }

    /// <summary>Moves the grid by <paramref name="delta"/> virtual pixels, as far as it can go.</summary>
    public void PanBy(Vector2 delta)
    {
        _origin += delta;
        Clamp();
    }

    /// <summary>Pans just enough that cell (<paramref name="x"/>, <paramref name="y"/>) is inside the box.</summary>
    public void Follow(int x, int y)
    {
        var corner = CellCorner(x, y);
        var dx = corner.X < Box.X ? Box.X - corner.X : Math.Min(0, Box.Right - (corner.X + Cell));
        var dy = corner.Y < Box.Y ? Box.Y - corner.Y : Math.Min(0, Box.Bottom - (corner.Y + Cell));
        PanBy(new Vector2(dx, dy));
    }

    private static Vector2 Centered(RectangleF box, float cell) =>
        new(box.X + ((box.Width - (cell * Stage.Width)) / 2), box.Y + ((box.Height - (cell * Stage.Height)) / 2));

    /// <summary>Centers the grid on an axis where it fits, and keeps the box covered on an axis where it doesn't.</summary>
    private void Clamp()
    {
        var size = Cell * Stage.Width;
        var centered = Centered(Box, Cell);
        _origin.X = size <= Box.Width ? centered.X : Math.Clamp(_origin.X, Box.Right - size, Box.X);
        _origin.Y = size <= Box.Height ? centered.Y : Math.Clamp(_origin.Y, Box.Bottom - size, Box.Y);
    }
}

/// <summary>An axis-aligned rectangle in virtual-screen pixels.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge.</summary>
    public float Right => X + Width;

    /// <summary>The bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>True when <paramref name="point"/> is inside, counting the top and left edges but not the bottom and right.</summary>
    public bool Contains(Vector2 point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}
