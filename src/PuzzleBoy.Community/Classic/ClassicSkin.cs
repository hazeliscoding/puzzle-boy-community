using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Nocturne;
using PuzzleBoy.Rendering;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The Classic theme's look for Community's own screens: the original's font (baked by the engine's
/// <see cref="TextRenderer"/>), the minigame's window panel and cursor from <c>pzlUI01</c>, the keyboard sheet's key
/// prompts, the <c>BG_pzl</c> backdrop, and the Jack-o'-Lantern idling where the title puts Pixel Jack. Community keeps its
/// layouts and words; only the look changes.
/// </summary>
internal sealed class ClassicSkin : IUiSkin, IDisposable
{
    /// <summary>The original's 1920×1080 layout in Community's 1280×720 virtual screen: two thirds.</summary>
    public const float FromLayout = 1280f / 1920;

    /// <summary>How many TextMeshPro font units a Community pixel size is; Matisse's capitals are taller than Atkinson's.</summary>
    internal const float TextSize = 0.95f;

    // Sprite rects, in Unity's texels from the bottom-left, on pzlUI01 (2048×2048): the □ menu's panel and cursor.
    private static readonly Rectangle MenuPanel = new(632, 808, 516, 284);
    private static readonly Rectangle MenuCursor = new(1964, 4, 60, 56);

    // The panel's border, in texels, kept unstretched when it's drawn at another size.
    private const int Border = 12;

    // Keys with no icon on the keyboard sheet get a cap drawn in its colours.
    private static readonly Color WindowGround = new(0x0C, 0x0A, 0x0E);
    private static readonly Color CapFill = new(0x26, 0x27, 0x47), CapRim = new(0x8C, 0x88, 0xD4);

    private readonly FontData _font;
    private readonly IReadOnlyDictionary<string, TextMaterial> _materials;
    private readonly Texture2D _sheet;
    private readonly Texture2D _keyboard;
    private readonly Texture2D _backdrop;
    private readonly Texture2D _pixel;
    private readonly GraphicsDevice _device;
    private readonly CharacterView _characterView;
    private readonly CharacterModel _character;
    private readonly Dictionary<(string Text, float Size, string Material, float Scale), (Texture2D? Texture, Point At)> _baked = [];

    /// <summary>
    /// Uploads what it draws from <paramref name="ui"/>, the <paramref name="backdrop"/> and the
    /// <paramref name="character"/>; text is baked on first use.
    /// </summary>
    public ClassicSkin(GraphicsDevice device, ClassicUi ui, TextureData backdrop, CharacterData character)
    {
        _characterView = new CharacterView(device, character);
        _character = new CharacterModel(character);
        _device = device;
        _font = ui.Text.Font;
        _materials = ui.Text.Materials;
        _sheet = Upload(device, ui.Sheets.Sheet);
        _keyboard = Upload(device, ui.Sheets.Keyboard);
        _backdrop = Upload(device, backdrop);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
        Strings = ui.Text.Strings;
    }

    /// <summary>The original's strings by id; Community reads only the □ menu's labels.</summary>
    public IReadOnlyDictionary<string, string> Strings { get; }

    /// <summary>
    /// The original □ menu's labels (<see cref="PuzzleBoy.Rendering.Menu.Labels"/>): Undo, Restart, Help and Quit, in
    /// Community's words where the text lacks one.
    /// </summary>
    public IReadOnlyList<string> OriginalMenuLabels =>
        [.. PuzzleBoy.Rendering.Menu.Labels.Zip(new[] { "Undo", "Restart", "Help", "Quit" }, (id, word) => Strings.TryGetValue(id, out var label) ? label : word)];

    /// <summary>
    /// The material text in <paramref name="font"/>'s role is drawn with: headings in the help page's title material
    /// (white, an amber edge), small labels in its body text's (white, a deep blue edge), and the rest in the □ menu's
    /// (white, a grey edge). Community's colours tint them.
    /// </summary>
    public static string MaterialFor(Font font) => font switch
    {
        Font.TitleBold => "TMC12",
        Font.Title => "TMC24",
        _ => "TMC22",
    };

    /// <summary>
    /// The keyboard sheet's icon for a key as Community's hints name it, in Unity's texels from the bottom-left, or null
    /// for a key the sheet has no icon for (Enter, Esc, the arrows, digits). Letters run A to Z from x 28, 72 apart.
    /// </summary>
    public static Rectangle? KeyIcon(string key) => key switch
    {
        [var c] when c is >= 'A' and <= 'Z' => new Rectangle(28 + (72 * (c - 'A')), 60, 48, 48),
        "Ctrl" => new Rectangle(28, 108, 48, 48),
        "Alt" => new Rectangle(172, 108, 48, 48),
        "Backspace" => new Rectangle(316, 108, 48, 48),
        "Tab" => new Rectangle(460, 108, 48, 48),
        "Space" => new Rectangle(532, 108, 48, 48),
        _ => null,
    };

    /// <summary>
    /// True when <paramref name="font"/> has every character of <paramref name="text"/>, once <see cref="Normalize"/>
    /// has swapped in the look-alikes it has.
    /// </summary>
    public static bool Covers(FontData font, string text) => Normalize(text).All(font.Glyphs.ContainsKey);

    /// <summary>
    /// <paramref name="text"/> with the characters Community uses that the original's font lacks swapped for the
    /// look-alikes it has: the minus sign for a hyphen.
    /// </summary>
    public static string Normalize(string text) => text.Replace('−', '-');

    /// <inheritdoc />
    public bool Covers(string text) => Covers(_font, text);

    /// <inheritdoc />
    public Vector2 Measure(float size, string text) =>
        new(TextRenderer.Advance(_font, Normalize(text), size * TextSize), (_font.Ascent - _font.Descent) / _font.PointSize * size * TextSize);

    /// <inheritdoc />
    public void Text(SpriteBatch batch, (float Scale, Vector2 Offset) fit, Font font, float size, string text, Vector2 top, Color color)
    {
        var s = size * TextSize;
        var baseline = top.Y + (_font.Ascent / _font.PointSize * s);
        DrawBaked(batch, fit, Normalize(text), s, MaterialFor(font), new Vector2(top.X, baseline), color);
    }

    /// <summary>Draws <paramref name="text"/> at <paramref name="size"/> font units, its pen and baseline at <paramref name="pen"/>.</summary>
    public void DrawBaked(SpriteBatch batch, (float Scale, Vector2 Offset) fit, string text, float size, string material, Vector2 pen, Color color)
    {
        var key = (text, size, material, fit.Scale);
        if (!_baked.TryGetValue(key, out var baked))
        {
            // A bake is per string, size and window scale; a screen of changing text (a clock, typing) makes many, so the
            // cache starts over now and then rather than growing for the session.
            if (_baked.Count > 1500)
            {
                Clear();
            }

            var image = TextRenderer.Bake(_font, [_materials.TryGetValue(material, out var m) ? m : _materials.Values.First()], [.. text.Select(c => (c, 0))], size, 0, 0, (fit.Scale, Vector2.Zero));
            Texture2D? texture = null;
            if (image.Width > 0)
            {
                texture = new Texture2D(_device, image.Width, image.Height);
                texture.SetData(image.Rgba);
            }

            _baked[key] = baked = (texture, new Point(image.X, image.Y));
        }

        if (baked.Texture is { } t)
        {
            // Baked at whole window pixels, so the pen snaps to one and the bake lands texel for pixel.
            var window = new Vector2(MathF.Round(fit.Offset.X + (pen.X * fit.Scale)), MathF.Round(fit.Offset.Y + (pen.Y * fit.Scale))) + baked.At.ToVector2();
            batch.Draw(t, (window - fit.Offset) / fit.Scale, null, color, 0, Vector2.Zero, 1 / fit.Scale, SpriteEffects.None, 0);
        }
    }

    /// <inheritdoc />
    public void Window(SpriteBatch batch, float x, float y, float width, float height, float alpha)
    {
        // The □ menu's panel lets the board show through; Community's windows hold more text, so a dark ground goes under it.
        Fill(batch, x + 1, y + 1, width - 2, height - 2, WindowGround * (alpha * 0.8f));
        Sliced(batch, MenuPanel, x, y, width, height, Color.White * alpha);
    }

    /// <inheritdoc />
    public float KeyWidth(string key, float height) =>
        KeyIcon(key) is null ? Math.Max(height, Measure(height * 0.62f, key).X + (height * 0.5f)) : height;

    /// <inheritdoc />
    public float Key(SpriteBatch batch, (float Scale, Vector2 Offset) fit, string key, Vector2 at, float height)
    {
        if (KeyIcon(key) is { } icon)
        {
            batch.Draw(_keyboard, new Rectangle((int)MathF.Round(at.X), (int)MathF.Round(at.Y), (int)height, (int)height), Flip(icon, _keyboard.Height), Color.White);
            return height;
        }

        // The sheet's caps: a dark navy face inside a lavender rim, with the label in the font.
        var width = KeyWidth(key, height);
        var inset = height * 0.08f;
        Fill(batch, at.X + inset, at.Y + inset, width - (2 * inset), height - (2 * inset), CapRim);
        Fill(batch, at.X + inset + 1.5f, at.Y + inset + 1.5f, width - (2 * inset) - 3, height - (2 * inset) - 3, CapFill);
        var size = height * 0.62f;
        var text = Measure(size, key);
        Text(batch, fit, Font.BodyBold, size, key, new Vector2(at.X + ((width - text.X) / 2), at.Y + ((height - text.Y) / 2) + (height * 0.02f)), Color.White);
        return width;
    }

    /// <inheritdoc />
    public void Cursor(SpriteBatch batch, float x, float y, float height, float alpha)
    {
        // The □ menu's cursor is 60×56 for rows 58 apart.
        var h = height * 56 / 58;
        var w = h * 60 / 56;
        batch.Draw(_sheet, new Vector2(x - (w / 2), y - (h / 2)), Flip(MenuCursor, _sheet.Height), Color.White * alpha, 0, Vector2.Zero, new Vector2(w / 60, h / 56), SpriteEffects.None, 0);
    }

    /// <inheritdoc />
    public void Backdrop(SpriteBatch batch, int width, int height)
    {
        // Covers the window, cropping the backdrop's sides or top and bottom as its shape needs; dimmed behind the menus.
        var scale = Math.Max(width / (float)_backdrop.Width, height / (float)_backdrop.Height);
        var (w, h) = (_backdrop.Width * scale, _backdrop.Height * scale);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(_backdrop, new Vector2((width - w) / 2, (height - h) / 2), null, new Color(0.7f, 0.7f, 0.7f), 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        batch.End();
    }

    /// <summary>
    /// The Jack-o'-Lantern idling with the original's animation at <paramref name="ticks"/>, where the title puts Pixel
    /// Jack: right of the menu, as big, turning a little from side to side, with its shadow and lantern glow.
    /// </summary>
    public bool Character(int ticks, float aspect)
    {
        // The model stands on the cell at the origin, facing the camera; the title's placement moves it into view.
        _character.Pose(ticks, 0, 0, 180 + (MathF.Sin(ticks * 0.025f) * 25));
        var place = Matrix.CreateScale(1.35f) * Matrix.CreateTranslation(1.6f, -0.45f, 0);
        var view = place * Matrix.CreateLookAt(new Vector3(0, 0.45f, 4.2f), new Vector3(0, 0.45f, 0), Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(40), aspect, 0.05f, 60f);
        _characterView.DrawModels([_character], view, projection, 0);
        _characterView.DrawShadowsAndGlows([_character], view, projection);
        return true;
    }

    /// <summary>
    /// The original's □ menu with <paramref name="labels"/>, as the shared <see cref="Hud.DrawMenu"/> draws its four: the
    /// panel at (90, 138) in the original's layout, grown by a row for each label past four, the cursor on
    /// <paramref name="focus"/> and each label at 42 units in the menu's material.
    /// </summary>
    public void OriginalMenu(SpriteBatch batch, (float Scale, Vector2 Offset) fit, IReadOnlyList<string> labels, int focus)
    {
        var grow = 58 * Math.Max(0, labels.Count - 4);
        Sliced(batch, MenuPanel, 90 * FromLayout, 138 * FromLayout, 516 * FromLayout, (284 + grow) * FromLayout, Color.White, vertical: true);
        batch.Draw(_sheet, new Vector2(104, 164 + (58 * focus)) * FromLayout, Flip(MenuCursor, _sheet.Height), Color.White, 0, Vector2.Zero, FromLayout, SpriteEffects.None, 0);
        for (var i = 0; i < labels.Count; i++)
        {
            DrawBaked(batch, fit, labels[i], 42 * FromLayout, "TMC22", new Vector2(168, 207.96f + (58 * i)) * FromLayout, Color.White);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Clear();
        _characterView.Dispose();
        _sheet.Dispose();
        _keyboard.Dispose();
        _backdrop.Dispose();
        _pixel.Dispose();
    }

    /// <summary>A Unity sprite rect (texels from the bottom-left) as a rect on a sheet uploaded top row first.</summary>
    internal static Rectangle Flip(Rectangle unity, int sheetHeight) => new(unity.X, sheetHeight - unity.Y - unity.Height, unity.Width, unity.Height);

    /// <summary>
    /// Uploads <paramref name="data"/>'s top level with its rows turned top-down and its colour premultiplied, as
    /// Community's sprite batch blends.
    /// </summary>
    private static Texture2D Upload(GraphicsDevice device, TextureData data)
    {
        var (src, row) = (data.Levels[0], data.Width * 4);
        var pixels = new byte[src.Length];
        for (var y = 0; y < data.Height; y++)
        {
            src.AsSpan((data.Height - 1 - y) * row, row).CopyTo(pixels.AsSpan(y * row));
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            (pixels[i], pixels[i + 1], pixels[i + 2]) = ((byte)(pixels[i] * a / 255), (byte)(pixels[i + 1] * a / 255), (byte)(pixels[i + 2] * a / 255));
        }

        var texture = new Texture2D(device, data.Width, data.Height);
        texture.SetData(pixels);
        return texture;
    }

    private void Fill(SpriteBatch batch, float x, float y, float width, float height, Color color) =>
        batch.Draw(_pixel, new Vector2(x, y), null, color, 0, Vector2.Zero, new Vector2(width, height), SpriteEffects.None, 0);

    /// <summary>
    /// Draws a sheet sprite over a rectangle, nine-sliced so its <see cref="Border"/> keeps the original's thickness; with
    /// <paramref name="vertical"/>, only its height stretches, as the □ menu grows a row at a time.
    /// </summary>
    private void Sliced(SpriteBatch batch, Rectangle unity, float x, float y, float width, float height, Color color, bool vertical = false)
    {
        var source = Flip(unity, _sheet.Height);
        var b = Border * FromLayout;
        float[] xs = vertical ? [x, x + width] : [x, x + b, x + width - b, x + width];
        float[] us = vertical ? [source.Left, source.Right] : [source.Left, source.Left + Border, source.Right - Border, source.Right];
        float[] ys = [y, y + b, y + height - b, y + height];
        float[] vs = [source.Top, source.Top + Border, source.Bottom - Border, source.Bottom];
        for (var i = 0; i + 1 < xs.Length; i++)
        {
            for (var j = 0; j + 1 < ys.Length; j++)
            {
                var part = new Rectangle((int)us[i], (int)vs[j], (int)(us[i + 1] - us[i]), (int)(vs[j + 1] - vs[j]));
                var size = new Vector2(xs[i + 1] - xs[i], ys[j + 1] - ys[j]);
                if (size.X > 0 && size.Y > 0)
                {
                    batch.Draw(_sheet, new Vector2(xs[i], ys[j]), part, color, 0, Vector2.Zero, size / part.Size.ToVector2(), SpriteEffects.None, 0);
                }
            }
        }
    }

    private void Clear()
    {
        foreach (var (texture, _) in _baked.Values)
        {
            texture?.Dispose();
        }

        _baked.Clear();
    }
}
