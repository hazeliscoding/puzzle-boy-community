using Microsoft.Xna.Framework;

namespace PuzzleBoy.Community.Graphics;

/// <summary>
/// Pixel Jack as voxels: the 16×16 brand mark turned into a solid by giving each row a round cross-section as wide as
/// the row, so the pumpkin is a squat sphere and the hat a cone. The face shows only on the front; the back is plain
/// pumpkin. Model space: one cell is 1, the feet stand at y = 0, the face looks toward +z.
/// </summary>
internal static class JackModel
{
    /// <summary>How tall the figure stands, in cells.</summary>
    public const float Height = 0.92f;

    /// <summary>Builds the figure from <paramref name="theme"/>'s mark, or a plain pumpkin when the theme has none.</summary>
    public static Mesh Build(Theme theme)
    {
        var mark = theme.Mark.GetLength(0) == 16 ? theme.Mark : Fallback(theme);
        var voxel = Height / 16;
        var face = theme["ember"];
        var mesh = new Mesh();
        for (var y = 0; y < 16; y++)
        {
            var filled = Enumerable.Range(0, 16).Where(x => mark[x, y] is not null).ToList();
            if (filled.Count == 0)
            {
                continue;
            }

            var center = (filled[0] + filled[^1] + 1) / 2f;
            var half = (filled[^1] - filled[0] + 1) / 2f;
            foreach (var x in filled)
            {
                var color = mark[x, y]!.Value;
                var off = (x + 0.5f - center) / half;
                var depth = MathF.Max(0.5f, MathF.Round(half * MathF.Sqrt(MathF.Max(0, 1 - (off * off))) * 0.9f));
                var min = new Vector3((x - 8) * voxel, (15 - y) * voxel, -depth * voxel);
                var max = new Vector3((x - 7) * voxel, (16 - y) * voxel, depth * voxel);

                // The face glows on the front; elsewhere a face pixel is pumpkin skin.
                var skin = color == face ? theme["pumpkin"] : color;
                mesh.AddBox(min, max, skin, Atlas.White, Atlas.White, Matrix.Identity);
                if (color == face)
                {
                    mesh.AddQuad(new(min.X, min.Y, max.Z + 0.002f), new(max.X, min.Y, max.Z + 0.002f), new(max.X, max.Y, max.Z + 0.002f), new(min.X, max.Y, max.Z + 0.002f), face, Atlas.White, false);
                }
            }
        }

        return mesh;
    }

    // A round pumpkin, for a theme without a mark.
    private static Color?[,] Fallback(Theme theme)
    {
        var grid = new Color?[16, 16];
        for (var y = 6; y < 16; y++)
        {
            for (var x = 1; x < 15; x++)
            {
                var (dx, dy) = ((x - 7.5f) / 7, (y - 10.5f) / 5);
                if ((dx * dx) + (dy * dy) <= 1)
                {
                    grid[x, y] = theme["pumpkin"];
                }
            }
        }

        return grid;
    }
}
