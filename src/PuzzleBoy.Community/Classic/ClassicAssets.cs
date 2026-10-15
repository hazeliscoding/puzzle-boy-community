using PuzzleBoy.Nocturne;
using PuzzleBoy.Rendering;

namespace PuzzleBoy.Community.Classic;

/// <summary>What the Nocturne-styled interface draws with, decoded from a schema 2 classic pack.</summary>
/// <param name="Sheets">
/// The minigame's UI sheets as puzzle-boy reads them, except the two only the Manikin Kid's dialogue uses
/// (<c>talkUI01</c> and <c>buttonUI01_en_common</c>), which are a blank texel each.
/// </param>
/// <param name="Text">The font, its materials and the strings; Community reads only help, menu and controls-bar ids.</param>
internal sealed record ClassicUi(UiSheets Sheets, TextData Text)
{
    /// <summary>The stand-in for a dialogue-only sheet: one transparent texel.</summary>
    public static TextureData Blank { get; } = new(1, 1, [new byte[4]]);
}

/// <summary>Everything the Classic theme draws and plays, decoded from a classic pack.</summary>
/// <param name="Atlas">The board atlas, <c>sample1</c> from <c>pazzleboy</c>.</param>
/// <param name="Background">The backdrop, <c>BG_pzl</c> from <c>common</c>.</param>
/// <param name="Sounds">The minigame's effects and music by clip name, from <c>pazzleboy</c>.</param>
/// <param name="Character">Jack-o'-Lantern, from <c>dvl0x150</c> with <c>common</c>'s toon ramp.</param>
/// <param name="Ui">The interface's sheets and text, or null for a schema 1 pack or when they didn't decode.</param>
/// <param name="UiProblem">Why a schema 2 pack's interface didn't decode, or null.</param>
internal sealed record ClassicAssets(
    TextureData Atlas, TextureData Background, IReadOnlyDictionary<string, SoundData> Sounds, CharacterData Character,
    ClassicUi? Ui = null, string? UiProblem = null)
{
    /// <summary>What was decoded, in a line for the smoke test.</summary>
    public string Summary => $"atlas {Atlas.Width}x{Atlas.Height}, backdrop {Background.Width}x{Background.Height}, {Sounds.Count} sounds, Jack-o'-Lantern with {Character.Meshes.Length} meshes, "
        + (Ui is { } ui ? $"the interface with {ui.Text.Font.Glyphs.Count} glyphs" : UiProblem is null ? "no interface (schema 1 pack)" : $"no interface ({UiProblem})");
}

/// <summary>
/// Where the classic stages and the Classic theme's assets come from. The game reads only classic packs
/// (<see cref="ClassicPackSource"/>); tests hand in their own.
/// </summary>
internal interface IClassicAssetSource
{
    /// <summary>
    /// The <c>pazzleboy</c> bundle's TextAssets by name, of which the game uses only the stage files. Throws whatever the
    /// bundle reader throws for a damaged bundle, so a caller must catch broadly.
    /// </summary>
    IReadOnlyDictionary<string, byte[]> ReadTextAssets();

    /// <summary>
    /// Decodes the Classic theme's assets: a couple of seconds' work. Throws as <see cref="ReadTextAssets"/> does for the
    /// board, Jack-o'-Lantern and sound; interface parts that don't decode leave <see cref="ClassicAssets.Ui"/> null with
    /// the reason instead.
    /// </summary>
    ClassicAssets ReadAssets();
}

/// <summary>
/// A classic pack folder read through the engine's <see cref="GameInstall"/>, the same reader puzzle-boy uses, and the
/// same textures, sounds and text it loads (its <c>GameAssets</c>) minus the dialogue's sheets.
/// </summary>
/// <param name="folder">The pack's folder, laid out as a game folder.</param>
/// <param name="ui">True to read the interface's sheets and text too, from a pack that has them (schema 2).</param>
internal sealed class ClassicPackSource(string folder, bool ui) : IClassicAssetSource
{
    private GameInstall Install => GameInstall.At(folder) ?? throw new InvalidDataException($"There's no pazzleboy bundle in {folder}.");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, byte[]> ReadTextAssets() => Install.ReadTextAssets("pazzleboy");

    /// <inheritdoc />
    public ClassicAssets ReadAssets()
    {
        var install = Install;
        Dictionary<string, TextureData> common;
        string? problem = null;
        try
        {
            // One pass over common, the 79 MB bundle, reads the interface's sheets with the board's.
            common = install.ReadTextures("common", ui ? ["BG_pzl", "tone2", "pzlUI01", "pzlUI_font01_ja"] : ["BG_pzl", "tone2"]);
        }
        catch (Exception e) when (ui)
        {
            // The board may still play without the interface's sheets; if not, this read throws for it.
            problem = $"{e.GetType().Name}: {e.Message}";
            common = install.ReadTextures("common", "BG_pzl", "tone2");
        }

        var board = new ClassicAssets(install.ReadTexture("pazzleboy", "sample1"), common["BG_pzl"], install.ReadSounds("pazzleboy"), install.ReadCharacter(common["tone2"]));
        if (!ui || problem is not null)
        {
            return board with { UiProblem = problem };
        }

        try
        {
            var sheets = new UiSheets(common["pzlUI01"], install.ReadTexture("ui_tex_en", "pzlUI_font01_en"), common["pzlUI_font01_ja"],
                install.ReadTexture("ui_tex_keyboard_en", "buttonUI01_en_keyboard"), ClassicUi.Blank, ClassicUi.Blank);
            return board with { Ui = new ClassicUi(sheets, install.ReadText()) };
        }
        catch (Exception e)
        {
            // The bundle reader throws all sorts for a damaged bundle; the board plays on with Community's interface.
            return board with { UiProblem = $"{e.GetType().Name}: {e.Message}" };
        }
    }
}

/// <summary>
/// The decoded assets of the classic pack read last, kept for the session: decoding takes a couple of seconds, and an
/// import has just done it.
/// </summary>
internal static class ClassicAssetCache
{
    // A reference, so threads never see half of one.
    private static Tuple<string, ClassicAssets>? _last;

    /// <summary>Keeps <paramref name="assets"/>, decoded from <paramref name="pack"/>.</summary>
    public static void Remember(ClassicPack pack, ClassicAssets assets) => _last = Tuple.Create(Key(pack), assets);

    /// <summary>
    /// The assets of <paramref name="pack"/>, decoded through <paramref name="open"/> (the engine's reader by default,
    /// reading the interface when the pack has it) unless they're kept already. Throws what
    /// <see cref="IClassicAssetSource.ReadAssets"/> throws.
    /// </summary>
    public static ClassicAssets Load(ClassicPack pack, Func<string, IClassicAssetSource>? open = null)
    {
        if (_last is { } last && last.Item1 == Key(pack))
        {
            return last.Item2;
        }

        var assets = (open ?? (f => new ClassicPackSource(f, pack.HasUi)))(pack.Folder).ReadAssets();
        Remember(pack, assets);
        return assets;
    }

    // Packs with the same files decode the same, wherever they are.
    private static string Key(ClassicPack pack) => string.Join(',', pack.Files.Select(f => f.Sha256));
}
