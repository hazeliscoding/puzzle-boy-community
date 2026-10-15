using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PuzzleBoy.Community.Graphics;

/// <summary>
/// A growing list of textured, vertex-colored triangles. Lighting is baked into the vertex colors from one fixed sun, so
/// the scene needs no lighting shader.
/// </summary>
internal sealed class Mesh
{
    // The sun comes from above, a little to the left and in front of the starting view.
    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(-0.45f, 1f, 0.6f));

    private VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[4096];

    /// <summary>How many vertices the mesh holds; every 3 make a triangle.</summary>
    public int Count { get; private set; }

    /// <summary>Empties the mesh, keeping its storage.</summary>
    public void Clear() => Count = 0;

    /// <summary>
    /// Adds an axis-aligned box from <paramref name="min"/> to <paramref name="max"/>, transformed by
    /// <paramref name="transform"/>, its top textured with tile <paramref name="top"/> and its sides with
    /// <paramref name="sides"/>.
    /// </summary>
    public void AddBox(Vector3 min, Vector3 max, Color color, int top, int sides, Matrix transform, Color? topColor = null)
    {
        Vector3 Corner(int i) => Vector3.Transform(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), transform);

        // Corners by bits: 1 = max x, 2 = max y, 4 = max z. Each face lists its corners counterclockwise from outside.
        AddQuad(Corner(2), Corner(6), Corner(7), Corner(3), topColor ?? color, top, true);
        AddQuad(Corner(4), Corner(5), Corner(7), Corner(6), color, sides, true);
        AddQuad(Corner(1), Corner(0), Corner(2), Corner(3), color, sides, true);
        AddQuad(Corner(5), Corner(1), Corner(3), Corner(7), color, sides, true);
        AddQuad(Corner(0), Corner(4), Corner(6), Corner(2), color, sides, true);
        AddQuad(Corner(0), Corner(1), Corner(5), Corner(4), color, sides, true);
    }

    /// <summary>
    /// Adds a quad from four corners in order around its edge, textured with tile <paramref name="tile"/>, shaded by the
    /// sun when <paramref name="lit"/>.
    /// </summary>
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, int tile, bool lit)
    {
        if (lit)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, d - a));
            var light = 0.58f + (0.42f * Math.Max(0, Vector3.Dot(normal, Sun)));
            color = new Color(color.ToVector3() * light) { A = color.A };
        }

        var uv = Atlas.Uv(tile);
        Ensure(6);
        Add(a, color, uv.X, uv.W);
        Add(b, color, uv.Z, uv.W);
        Add(c, color, uv.Z, uv.Y);
        Add(a, color, uv.X, uv.W);
        Add(c, color, uv.Z, uv.Y);
        Add(d, color, uv.X, uv.Y);
    }

    /// <summary>Adds a flat square of side <paramref name="size"/> lying at height <paramref name="y"/>, centered on (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public void AddFlat(float x, float y, float z, float size, Color color, int tile)
    {
        var h = size / 2;
        AddQuad(new(x - h, y, z + h), new(x + h, y, z + h), new(x + h, y, z - h), new(x - h, y, z - h), color, tile, false);
    }

    /// <summary>Adds a lit triangle.</summary>
    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        var light = 0.6f + (0.4f * Math.Max(0, Vector3.Dot(normal, Sun)));
        color = new Color(color.ToVector3() * light) { A = color.A };
        var uv = Atlas.Uv(Atlas.White);
        Ensure(3);
        Add(a, color, uv.X, uv.Y);
        Add(b, color, uv.X, uv.Y);
        Add(c, color, uv.X, uv.Y);
    }

    /// <summary>Appends another mesh's triangles, transformed.</summary>
    public void AddMesh(Mesh other, Matrix transform)
    {
        Ensure(other.Count);
        for (var i = 0; i < other.Count; i++)
        {
            var v = other._vertices[i];
            v.Position = Vector3.Transform(v.Position, transform);
            _vertices[Count++] = v;
        }
    }

    /// <summary>Multiplies the colors of every vertex from <paramref name="start"/> on by <paramref name="tint"/>.</summary>
    public void Tint(int start, Color tint)
    {
        for (var i = start; i < Count; i++)
        {
            _vertices[i].Color = new Color(_vertices[i].Color.ToVector4() * tint.ToVector4());
        }
    }

    /// <summary>Draws the triangles with <paramref name="effect"/>'s current settings.</summary>
    public void Draw(GraphicsDevice device, Effect effect)
    {
        if (Count == 0)
        {
            return;
        }

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, Count / 3);
        }
    }

    private void Add(Vector3 position, Color color, float u, float v) => _vertices[Count++] = new VertexPositionColorTexture(position, color, new Vector2(u, v));

    private void Ensure(int more)
    {
        if (Count + more > _vertices.Length)
        {
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, Count + more));
        }
    }
}
