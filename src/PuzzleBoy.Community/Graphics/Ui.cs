using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community.App;

namespace PuzzleBoy.Community.Graphics;

/// <summary>The four fonts by role.</summary>
internal enum Font
{
    /// <summary>Atkinson Hyperlegible: body text.</summary>
    Body,

    /// <summary>Atkinson Hyperlegible Bold: menu items and numbers.</summary>
    BodyBold,

    /// <summary>Silkscreen: small labels.</summary>
    Title,

    /// <summary>Silkscreen Bold: titles.</summary>
    TitleBold,
}

/// <summary>
/// 2D drawing on the 1280×720 virtual screen: text in the content fonts, rectangles, rounded panels, key caps and the
/// night-sky backdrop. Coordinates are virtual pixels; <see cref="Begin"/> scales them to the window.
/// </summary>
internal sealed class Ui : IDisposable
{
    private const int Corner = 10;

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    private readonly Texture2D _rounded;
    private readonly Texture2D _sky;
    private readonly Dictionary<Font, FontSystem> _fonts = [];
    private readonly Theme _theme;

    /// <summary>Loads the fonts named in <paramref name="fonts"/> and draws the backdrop textures.</summary>
    /// <exception cref="InvalidDataException">When no font can be read.</exception>
    public Ui(GraphicsDevice device, Theme theme, IReadOnlyDictionary<string, string> fonts)
    {
        _device = device;
        _theme = theme;
        _batch = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
        _rounded = Rounded(device);
        _sky = Sky(device, theme);
        foreach (var (font, role) in new[] { (Font.Body, "body"), (Font.BodyBold, "bodyBold"), (Font.Title, "title"), (Font.TitleBold, "titleBold") })
        {
            if (fonts.TryGetValue(role, out var path))
            {
                var system = new FontSystem();
                system.AddFont(File.ReadAllBytes(path));
                _fonts[font] = system;
            }
        }

        if (_fonts.Count == 0)
        {
            throw new InvalidDataException("No font in content/fonts could be read.");
        }
    }

    /// <summary>How big body text is drawn, as a multiple of the size asked for; titles keep their size.</summary>
    public float TextScale { get; set; } = 1f;

    /// <summary>True draws text and panels at full strength, however faded the caller asks for, unless nearly gone.</summary>
    public bool HighContrast { get; set; }

    /// <summary>True keeps the backdrop's stars still.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>A palette color by name.</summary>
    public Color this[string name] => _theme[name];

    /// <summary>Starts drawing on the virtual screen, scaled to the back buffer.</summary>
    public void Begin()
    {
        var pp = _device.PresentationParameters;
        var (scale, offset) = VirtualScreen.Fit(pp.BackBufferWidth, pp.BackBufferHeight);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null,
            Matrix.CreateScale(scale) * Matrix.CreateTranslation(offset.X, offset.Y, 0));
    }

    /// <summary>Ends drawing begun with <see cref="Begin"/>.</summary>
    public void End() => _batch.End();

    /// <summary>
    /// Fills the whole window with the night sky: a gradient and slowly twinkling stars. Draws on its own, outside
    /// <see cref="Begin"/>.
    /// </summary>
    public void Backdrop(int ticks)
    {
        var pp = _device.PresentationParameters;
        var (w, h) = (pp.BackBufferWidth, pp.BackBufferHeight);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        _batch.Draw(_sky, new Rectangle(0, 0, w, h), Color.White);
        for (var i = 0; i < 90; i++)
        {
            // Fixed pseudo-random places, so the sky is the same every run.
            var hash = (uint)(i * 2654435761u);
            var x = (hash % 10007) / 10007f * w;
            var y = ((hash / 10007) % 9973) / 9973f * h * 0.8f;
            var twinkle = 0.35f + (0.3f * MathF.Sin(((ReduceMotion ? 0 : ticks) * 0.05f) + i));
            var size = i % 7 == 0 ? 3 : 2;
            _batch.Draw(_pixel, new Rectangle((int)x, (int)y, size, size), _theme["cream"] * twinkle);
        }

        _batch.End();
    }

    /// <summary>The size <paramref name="text"/> takes in <paramref name="font"/> at <paramref name="size"/> pixels.</summary>
    public Vector2 Measure(Font font, float size, string text, float spacing = 0) =>
        Get(font).GetFont(Scaled(font, size)).MeasureString(text, characterSpacing: spacing);

    /// <summary>
    /// Draws <paramref name="text"/> with its top at <paramref name="position"/>.Y, aligned on <paramref name="position"/>.X:
    /// <paramref name="align"/> 0 puts its left there, 0.5 its middle, 1 its right. Returns its size.
    /// </summary>
    public Vector2 Text(Font font, float size, string text, Vector2 position, Color color, float align = 0, float spacing = 0)
    {
        var measure = Measure(font, size, text, spacing);
        var at = new Vector2(MathF.Round(position.X - (measure.X * align)), MathF.Round(position.Y));
        _batch.DrawString(Get(font).GetFont(Scaled(font, size)), text, at, Strong(color, 90), characterSpacing: spacing);
        return measure;
    }

    /// <summary>Draws <paramref name="text"/> with a soft drop shadow under it.</summary>
    public Vector2 ShadowText(Font font, float size, string text, Vector2 position, Color color, Color shadow, float align = 0, float spacing = 0, float depth = 4)
    {
        Text(font, size, text, position + new Vector2(0, depth), shadow, align, spacing);
        return Text(font, size, text, position, color, align, spacing);
    }

    /// <summary>
    /// Draws <paramref name="text"/> wrapped to lines no wider than <paramref name="width"/>, from <paramref name="position"/>
    /// down. Returns the height used.
    /// </summary>
    public float Wrapped(Font font, float size, string text, Vector2 position, float width, Color color, float lineHeight = 1.35f)
    {
        var y = position.Y;
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var tryLine = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && Measure(font, size, tryLine).X > width)
                {
                    Text(font, size, line, new Vector2(position.X, y), color);
                    y += Scaled(font, size) * lineHeight;
                    line = word;
                }
                else
                {
                    line = tryLine;
                }
            }

            Text(font, size, line, new Vector2(position.X, y), color);
            y += Scaled(font, size) * lineHeight;
        }

        return y - position.Y;
    }

    /// <summary>Fills a rectangle.</summary>
    public void Rect(float x, float y, float width, float height, Color color) =>
        _batch.Draw(_pixel, new Vector2(x, y), null, color, 0, Vector2.Zero, new Vector2(width, height), SpriteEffects.None, 0);

    /// <summary>Fills a rectangle with rounded corners.</summary>
    public void Panel(float x, float y, float width, float height, Color color)
    {
        color = Strong(color, 60);
        var c = Math.Min(Corner, Math.Min(width, height) / 2);
        void Part(float px, float py, float pw, float ph, Rectangle source) =>
            _batch.Draw(_rounded, new Vector2(px, py), source, color, 0, Vector2.Zero, new Vector2(pw / source.Width, ph / source.Height), SpriteEffects.None, 0);

        Part(x, y, c, c, new Rectangle(0, 0, Corner, Corner));
        Part(x + width - c, y, c, c, new Rectangle(Corner, 0, Corner, Corner));
        Part(x, y + height - c, c, c, new Rectangle(0, Corner, Corner, Corner));
        Part(x + width - c, y + height - c, c, c, new Rectangle(Corner, Corner, Corner, Corner));
        Rect(x + c, y, width - (2 * c), height, color);
        Rect(x, y + c, c, height - (2 * c), color);
        Rect(x + width - c, y + c, c, height - (2 * c), color);
    }

    /// <summary>Draws a line <paramref name="thickness"/> wide from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Line(Vector2 from, Vector2 to, float thickness, Color color)
    {
        var along = to - from;
        _batch.Draw(_pixel, from, null, color, MathF.Atan2(along.Y, along.X), new Vector2(0, 0.5f), new Vector2(along.Length(), thickness), SpriteEffects.None, 0);
    }

    /// <summary>Draws the <paramref name="source"/> part of <paramref name="texture"/> stretched over a rectangle.</summary>
    public void Sprite(Texture2D texture, Rectangle source, float x, float y, float width, float height, Color color) =>
        _batch.Draw(texture, new Vector2(x, y), source, color, 0, Vector2.Zero, new Vector2(width / source.Width, height / source.Height), SpriteEffects.None, 0);

    /// <summary>Draws a key cap with <paramref name="key"/> on it at <paramref name="position"/>, and returns its width.</summary>
    public float KeyCap(string key, Vector2 position, float size = 18)
    {
        var text = Measure(Font.BodyBold, size, key);
        var width = Math.Max(text.X + 16, size + 12);
        var height = size + 12;
        Panel(position.X, position.Y + 2, width, height, _theme["ink"]);
        Panel(position.X, position.Y, width, height, _theme["cream"]);
        Text(Font.BodyBold, size, key, new Vector2(position.X + (width / 2), position.Y + 4), _theme["ink"], 0.5f);
        return width;
    }

    /// <summary>
    /// Draws a row of key hints, such as ("Q E", "Turn"), centered on <paramref name="centerX"/> with their tops at
    /// <paramref name="y"/>. Keys separated by spaces get a cap each.
    /// </summary>
    public void Hints(IEnumerable<(string Keys, string Action)> hints, float centerX, float y, Color label, float size = 18)
    {
        var list = hints.ToList();
        float Width((string Keys, string Action) hint) =>
            hint.Keys.Split(' ').Sum(k => Math.Max(Measure(Font.BodyBold, size, k).X + 16, size + 12) + 4) + 4 + Measure(Font.Body, size, hint.Action).X;
        const float Gap = 28;
        var x = centerX - ((list.Sum(Width) + (Gap * (list.Count - 1))) / 2);
        foreach (var hint in list)
        {
            foreach (var key in hint.Keys.Split(' '))
            {
                x += KeyCap(key, new Vector2(x, y), size) + 4;
            }

            x += 4;
            x += Text(Font.Body, size, hint.Action, new Vector2(x, y + 4), label).X + Gap;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _batch.Dispose();
        _pixel.Dispose();
        _rounded.Dispose();
        _sky.Dispose();
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }
    }

    // Body text follows the UI scale; titles are display type and keep their layout.
    private float Scaled(Font font, float size) => font is Font.Body or Font.BodyBold ? size * TextScale : size;

    // In high contrast a dimmed color is drawn opaque, unless it's fading out (alpha below the threshold).
    private Color Strong(Color color, byte threshold) =>
        HighContrast && color.A >= threshold && color.A < 255 ? color * (255f / color.A) : color;

    private FontSystem Get(Font font) => _fonts.TryGetValue(font, out var system) ? system : _fonts.Values.First();

    // A white disc of radius Corner, cut into four corners for Panel.
    private static Texture2D Rounded(GraphicsDevice device)
    {
        var size = Corner * 2;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var (dx, dy) = (x + 0.5f - Corner, y + 0.5f - Corner);
                pixels[(y * size) + x] = Color.White * Math.Clamp(Corner - MathF.Sqrt((dx * dx) + (dy * dy)) + 0.5f, 0, 1);
            }
        }

        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }

    // Night at the top fading to the hat's purple near the bottom.
    private static Texture2D Sky(GraphicsDevice device, Theme theme)
    {
        const int Height = 256;
        var pixels = new Color[Height];
        var top = Color.Lerp(theme["night"], Color.Black, 0.35f);
        for (var y = 0; y < Height; y++)
        {
            var t = y / (float)(Height - 1);
            pixels[y] = Color.Lerp(top, theme["hat"], t * t);
        }

        var texture = new Texture2D(device, 1, Height);
        texture.SetData(pixels);
        return texture;
    }
}
