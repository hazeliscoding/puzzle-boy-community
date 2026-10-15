using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PuzzleBoy.Community.Content;

namespace PuzzleBoy.Community.Graphics;

/// <summary>
/// The palette and the Pixel Jack mark from a <c>puzzle-boy-theme</c> file. Anything missing or unreadable falls back
/// to the brand's own values, so a damaged theme changes the look but never stops the game.
/// </summary>
internal sealed class Theme
{
    /// <summary>The brand palette (ROADMAP.md, decisions), used for any color a theme file doesn't give.</summary>
    public static readonly IReadOnlyDictionary<string, Color> BrandColors = new Dictionary<string, Color>
    {
        ["pumpkin"] = new(0xF0, 0x7F, 0x2A),
        ["pumpkinShade"] = new(0xC9, 0x58, 0x1C),
        ["ember"] = new(0xFF, 0xC9, 0x4A),
        ["hat"] = new(0x2A, 0x21, 0x48),
        ["hatOnDark"] = new(0x4A, 0x3B, 0x91),
        ["band"] = new(0x7A, 0x4F, 0xD0),
        ["bandOnDark"] = new(0xB4, 0x97, 0xFF),
        ["ink"] = new(0x1A, 0x14, 0x26),
        ["cream"] = new(0xF4, 0xEE, 0xE2),
        ["night"] = new(0x15, 0x10, 0x2A),
    };

    /// <summary>The Okabe-Ito colorblind-safe set without black, for block groups.</summary>
    public static readonly Color[] OkabeIto =
    [
        new(0xE6, 0x9F, 0x00), new(0x56, 0xB4, 0xE9), new(0x00, 0x9E, 0x73), new(0xF0, 0xE4, 0x42),
        new(0x00, 0x72, 0xB2), new(0xD5, 0x5E, 0x00), new(0xCC, 0x79, 0xA7),
    ];

    private const int MarkSize = 16;
    private const int MaxMarkBytes = 4096;

    private readonly Dictionary<string, Color> _colors = new(BrandColors);

    private Theme()
    {
    }

    /// <summary>The block colors, one per group in turn; never empty.</summary>
    public IReadOnlyList<Color> GroupColors { get; private set; } = OkabeIto;

    /// <summary>The 16×16 mark, row 0 at the top, as a color per pixel; null is transparent. Empty when the theme has none.</summary>
    public Color?[,] Mark { get; private set; } = new Color?[0, 0];

    /// <summary>A named palette color, such as <c>ember</c>; a name the palette lacks throws.</summary>
    public Color this[string name] => _colors[name];

    /// <summary>
    /// Reads <paramref name="themeFile"/>, a path from <see cref="ContentManifest.Theme"/>. A null path gives the brand
    /// palette with no mark. Never throws; what can't be read keeps the brand value.
    /// </summary>
    public static Theme Load(string? themeFile)
    {
        var theme = new Theme();
        if (themeFile is null)
        {
            return theme;
        }

        var problems = new List<string>();
        if (ContentFile.ReadJson(themeFile, 64 * 1024, "theme", problems) is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return theme;
        }

        if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
        {
            foreach (var color in colors.EnumerateObject())
            {
                if (Parse(ContentFile.Text(color.Value)) is { } value)
                {
                    theme._colors[color.Name] = value;
                }
            }
        }

        if (root.TryGetProperty("groupColors", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            var list = groups.EnumerateArray().Select(g => Parse(ContentFile.Text(g))).OfType<Color>().ToList();
            if (list.Count > 0)
            {
                theme.GroupColors = list;
            }
        }

        if (root.TryGetProperty("mark", out var mark) && mark.ValueKind == JsonValueKind.Object)
        {
            theme.Mark = ReadMark(Path.GetDirectoryName(themeFile)!, mark, theme._colors);
        }

        return theme;
    }

    /// <summary>A <c>#RRGGBB</c> color, or null for anything else.</summary>
    public static Color? Parse(string? hex) =>
        hex is ['#', .. var digits] && digits.Length == 6 && uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;

    private static Color?[,] ReadMark(string folder, JsonElement mark, Dictionary<string, Color> colors)
    {
        var none = new Color?[0, 0];
        var problems = new List<string>();
        if (ContentFile.Resolve(folder, ContentFile.String(mark, "file"), "mark", problems) is not { } file
            || ContentFile.Read(file, MaxMarkBytes, "mark", problems) is not { } bytes
            || !mark.TryGetProperty("legend", out var legend) || legend.ValueKind != JsonValueKind.Object)
        {
            return none;
        }

        var rows = System.Text.Encoding.UTF8.GetString(bytes).Split('\n').Select(r => r.TrimEnd('\r')).Where(r => r.Length > 0).ToArray();
        if (rows.Length != MarkSize || rows.Any(r => r.Length != MarkSize))
        {
            return none;
        }

        var grid = new Color?[MarkSize, MarkSize];
        for (var y = 0; y < MarkSize; y++)
        {
            for (var x = 0; x < MarkSize; x++)
            {
                var key = rows[y][x].ToString();
                if (ContentFile.String(legend, key) is { } name && colors.TryGetValue(name, out var color))
                {
                    grid[x, y] = color;
                }
            }
        }

        return grid;
    }
}
