using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PuzzleBoy.Community.Graphics;

/// <summary>
/// The one texture the 3D scene samples, drawn in code: a row of 32×32 tiles. Vertex colors tint them, so every tile
/// is white with darker detail. Block groups get a pattern each, so color is never the only cue.
/// </summary>
internal static class Atlas
{
    /// <summary>A plain face with a darker frame, which outlines every box.</summary>
    public const int Plain = 0;

    /// <summary>The first of <see cref="PatternCount"/> block patterns.</summary>
    public const int FirstPattern = 1;

    /// <summary>How many block patterns there are.</summary>
    public const int PatternCount = 6;

    /// <summary>A swivel center's top: concentric rings around a pin.</summary>
    public const int Pivot = 7;

    /// <summary>Plain white, no frame.</summary>
    public const int White = 8;

    /// <summary>A soft ring, transparent inside and out, for the active character's marker.</summary>
    public const int Ring = 9;

    /// <summary>A soft round glow fading to transparent at the edge.</summary>
    public const int Glow = 10;

    /// <summary>The side of a tile in pixels.</summary>
    public const int TileSize = 32;

    /// <summary>How many tiles the texture holds.</summary>
    public const int TileCount = 11;

    /// <summary>The texture coordinates of tile <paramref name="tile"/>: left, top, right, bottom, kept half a texel in.</summary>
    public static Vector4 Uv(int tile)
    {
        var width = (float)(TileSize * TileCount);
        return new Vector4(((tile * TileSize) + 0.5f) / width, 0.5f / TileSize, (((tile + 1) * TileSize) - 0.5f) / width, (TileSize - 0.5f) / TileSize);
    }

    /// <summary>Pixel (<paramref name="x"/>, <paramref name="y"/>) of tile <paramref name="tile"/>, before any tint.</summary>
    public static Color Pixel(int tile, int x, int y)
    {
        var (cx, cy) = (x - 15.5f, y - 15.5f);
        var radius = MathF.Sqrt((cx * cx) + (cy * cy));
        var edge = Math.Min(Math.Min(x, y), Math.Min(TileSize - 1 - x, TileSize - 1 - y));
        switch (tile)
        {
            case White:
                return Color.White;
            case Ring:
                return Color.White * Math.Clamp(1 - (MathF.Abs(radius - 12) / 3.5f), 0, 1);
            case Glow:
                return Color.White * MathF.Pow(Math.Clamp(1 - (radius / 16), 0, 1), 1.6f);
        }

        // Every face tile: a dark outline, a lighter bevel inside it, then the face.
        if (edge < 2)
        {
            return Gray(0.42f);
        }

        if (edge < 4)
        {
            return Gray(1f);
        }

        var detail = tile switch
        {
            FirstPattern => (x + y) % 10 < 4,
            FirstPattern + 1 => (((x - 4) % 8) is >= 2 and < 6) && (((y - 4) % 8) is >= 2 and < 6),
            FirstPattern + 2 => ((x / 6) + (y / 6)) % 2 == 0,
            FirstPattern + 3 => y % 8 < 3,
            FirstPattern + 4 => Math.Abs(cx) < 3 || Math.Abs(cy) < 3,
            FirstPattern + 5 => MathF.Abs(MathF.Abs(cx) + MathF.Abs(cy) - 9) < 2.5f,
            Pivot => MathF.Abs(radius - 10) < 1.5f || radius < 4,
            _ => false,
        };

        return Gray(detail ? 0.68f : 0.9f);
    }

    /// <summary>Draws every tile into a new texture.</summary>
    public static Texture2D Create(GraphicsDevice device)
    {
        var width = TileSize * TileCount;
        var pixels = new Color[width * TileSize];
        for (var tile = 0; tile < TileCount; tile++)
        {
            for (var y = 0; y < TileSize; y++)
            {
                for (var x = 0; x < TileSize; x++)
                {
                    pixels[(y * width) + (tile * TileSize) + x] = Pixel(tile, x, y);
                }
            }
        }

        var texture = new Texture2D(device, width, TileSize);
        texture.SetData(pixels);
        return texture;
    }

    private static Color Gray(float value) => new(value, value, value);
}
