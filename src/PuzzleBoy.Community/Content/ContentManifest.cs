using System.Text.Json;

namespace PuzzleBoy.Community.Content;

/// <summary>
/// The game's built-in content, as <c>content/manifest.json</c> lists it: format <c>puzzle-boy-content</c>, schema
/// version 1, with a theme file, the fonts by role and the stage pack folders in play order.
/// </summary>
/// <param name="Theme">The full path of the theme file, or null when it's missing or unusable.</param>
/// <param name="Fonts">The full path of each font, by role (<c>body</c>, <c>bodyBold</c>, <c>title</c>, <c>titleBold</c>).</param>
/// <param name="Packs">The packs that loaded, in the manifest's order.</param>
internal sealed record ContentManifest(string? Theme, IReadOnlyDictionary<string, string> Fonts, IReadOnlyList<StagePack> Packs)
{
    /// <summary>The font roles the manifest must name.</summary>
    public static readonly string[] FontRoles = ["body", "bodyBold", "title", "titleBold"];

    private const int MaxManifestBytes = 64 * 1024;
    private const int MaxThemeBytes = 64 * 1024;
    private const int MaxFontBytes = 4 * 1024 * 1024;
    private const int MaxPacks = 64;

    /// <summary>
    /// Reads the manifest in <paramref name="contentFolder"/> and every pack it lists, leaving out what has a problem.
    /// Returns null when the manifest itself can't be read. The content is fit to ship only when
    /// <paramref name="problems"/> gained nothing; nothing throws.
    /// </summary>
    public static ContentManifest? Load(string contentFolder, List<string> problems)
    {
        const string Where = "manifest.json";
        if (ContentFile.Resolve(contentFolder, Where, Where, problems) is not { } file
            || ContentFile.ReadJson(file, MaxManifestBytes, Where, problems) is not { } root
            || !ContentFile.CheckHeader(root, "puzzle-boy-content", Where, problems))
        {
            return null;
        }

        var theme = ContentFile.Resolve(contentFolder, ContentFile.String(root, "theme"), $"{Where}, theme", problems);
        if (theme is not null && ContentFile.ReadJson(theme, MaxThemeBytes, $"{Where}, theme", problems) is null)
        {
            theme = null;
        }

        var fonts = new Dictionary<string, string>();
        root.TryGetProperty("fonts", out var fontList);
        foreach (var role in FontRoles)
        {
            var where = $"{Where}, font \"{role}\"";
            if (ContentFile.Resolve(contentFolder, ContentFile.String(fontList, role), where, problems) is { } font
                && ContentFile.Read(font, MaxFontBytes, where, problems) is { } bytes)
            {
                // TrueType outlines (0x00010000 or "true"); FontStashSharp's rasterizer reads nothing else.
                if (bytes is [0, 1, 0, 0, ..] or [(byte)'t', (byte)'r', (byte)'u', (byte)'e', ..])
                {
                    fonts[role] = font;
                }
                else
                {
                    problems.Add($"{where}: the file isn't a TrueType font.");
                }
            }
        }

        var packs = new List<StagePack>();
        if (!root.TryGetProperty("packs", out var packList) || packList.ValueKind != JsonValueKind.Array || packList.GetArrayLength() is 0 or > MaxPacks)
        {
            problems.Add($"{Where}: \"packs\" should list 1 to {MaxPacks} pack folders.");
            return new ContentManifest(theme, fonts, packs);
        }

        foreach (var entry in packList.EnumerateArray())
        {
            var folder = ContentFile.Text(entry);
            if (ContentFile.Resolve(contentFolder, folder, $"{Where}, pack", problems) is not { } full)
            {
                continue;
            }

            if (StagePack.Load(full, folder!, problems) is { } pack)
            {
                if (packs.Any(p => p.PackId == pack.PackId))
                {
                    problems.Add($"{folder}/pack.json: another pack has the id \"{pack.PackId}\".");
                    continue;
                }

                packs.Add(pack);
            }
        }

        return new ContentManifest(theme, fonts, packs);
    }
}
