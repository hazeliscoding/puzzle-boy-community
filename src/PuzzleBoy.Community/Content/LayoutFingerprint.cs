using System.Security.Cryptography;
using System.Text;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Content;

/// <summary>
/// An irreversible fingerprint of a stage's layout, the same under rotation, mirroring, translation and group renumbering.
/// Version 1: each cell becomes one symbol (wall, floor, hole, block, center, arm, each over a hole or not); the grid is
/// cropped to the cells that aren't wall; the smallest of the 8 rotations and mirrors, as text, is hashed with SHA-256.
/// </summary>
internal static class LayoutFingerprint
{
    /// <summary>The fingerprint of the terrain with the goal and the character starts marked.</summary>
    public static string Full(Stage stage) => Hash(Symbols(stage, true));

    /// <summary>The fingerprint of the terrain alone, so moving the goal or a start doesn't hide a copy.</summary>
    public static string Terrain(Stage stage) => Hash(Symbols(stage, false));

    private static char[,] Symbols(Stage stage, bool marks)
    {
        var grid = new char[Stage.Width, Stage.Height];
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                var cell = stage[x, y];
                var hole = (cell & Cell.HoleFlag) != 0;
                grid[x, y] = (cell >> 13) switch
                {
                    0 => hole ? 'o' : '.',
                    2 or 3 => hole ? 'B' : 'b',
                    4 => hole ? 'C' : 'c',
                    5 => hole ? 'A' : 'a',
                    _ => '#',
                };
            }
        }

        if (marks)
        {
            grid[stage.Goal.X, stage.Goal.Y] = 'G';
            foreach (var (_, x, y) in stage.Characters)
            {
                grid[x, y] = '1';
            }
        }

        return grid;
    }

    private static string Hash(char[,] grid)
    {
        int minX = Stage.Width, minY = Stage.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                if (grid[x, y] != '#')
                {
                    (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
                }
            }
        }

        string? best = null;
        if (maxX >= 0)
        {
            var (w, h) = (maxX - minX + 1, maxY - minY + 1);
            for (var t = 0; t < 8; t++)
            {
                // Rotations and mirrors: t & 4 transposes, t & 1 flips x, t & 2 flips y.
                var (tw, th) = (t & 4) != 0 ? (h, w) : (w, h);
                var text = new StringBuilder($"{tw}x{th}:");
                for (var ty = 0; ty < th; ty++)
                {
                    for (var tx = 0; tx < tw; tx++)
                    {
                        var (sx, sy) = (t & 4) != 0 ? (ty, tx) : (tx, ty);
                        sx = (t & 1) != 0 ? w - 1 - sx : sx;
                        sy = (t & 2) != 0 ? h - 1 - sy : sy;
                        text.Append(grid[minX + sx, minY + sy]);
                    }

                    text.Append('/');
                }

                var candidate = text.ToString();
                if (best is null || string.CompareOrdinal(candidate, best) < 0)
                {
                    best = candidate;
                }
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(best ?? "empty")));
    }
}
