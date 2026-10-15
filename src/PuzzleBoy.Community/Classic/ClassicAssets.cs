using PuzzleBoy.Nocturne;

namespace PuzzleBoy.Community.Classic;

/// <summary>Everything the Classic theme draws and plays, decoded from a classic pack.</summary>
/// <param name="Atlas">The board atlas, <c>sample1</c> from <c>pazzleboy</c>.</param>
/// <param name="Background">The backdrop, <c>BG_pzl</c> from <c>common</c>.</param>
/// <param name="Sounds">The minigame's effects and music by clip name, from <c>pazzleboy</c>.</param>
/// <param name="Character">Jack-o'-Lantern, from <c>dvl0x150</c> with <c>common</c>'s toon ramp.</param>
internal sealed record ClassicAssets(TextureData Atlas, TextureData Background, IReadOnlyDictionary<string, SoundData> Sounds, CharacterData Character)
{
    /// <summary>What was decoded, in a line for the smoke test.</summary>
    public string Summary => $"atlas {Atlas.Width}x{Atlas.Height}, backdrop {Background.Width}x{Background.Height}, {Sounds.Count} sounds, Jack-o'-Lantern with {Character.Meshes.Length} meshes";
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

    /// <summary>Decodes the Classic theme's assets: a couple of seconds' work. Throws as <see cref="ReadTextAssets"/> does.</summary>
    ClassicAssets ReadAssets();
}

/// <summary>
/// A classic pack folder read through the engine's <see cref="GameInstall"/>, the same reader puzzle-boy uses, and the
/// same textures and sounds it loads (its <c>GameAssets</c>) minus the HUD sheets and the text.
/// </summary>
/// <param name="folder">The pack's folder, laid out as a game folder.</param>
internal sealed class ClassicPackSource(string folder) : IClassicAssetSource
{
    private GameInstall Install => GameInstall.At(folder) ?? throw new InvalidDataException($"There's no pazzleboy bundle in {folder}.");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, byte[]> ReadTextAssets() => Install.ReadTextAssets("pazzleboy");

    /// <inheritdoc />
    public ClassicAssets ReadAssets()
    {
        var install = Install;
        var common = install.ReadTextures("common", "BG_pzl", "tone2");
        return new ClassicAssets(install.ReadTexture("pazzleboy", "sample1"), common["BG_pzl"], install.ReadSounds("pazzleboy"), install.ReadCharacter(common["tone2"]));
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
    /// The assets of <paramref name="pack"/>, decoded through <paramref name="open"/> (the engine's reader by default)
    /// unless they're kept already. Throws what <see cref="IClassicAssetSource.ReadAssets"/> throws.
    /// </summary>
    public static ClassicAssets Load(ClassicPack pack, Func<string, IClassicAssetSource>? open = null)
    {
        if (_last is { } last && last.Item1 == Key(pack))
        {
            return last.Item2;
        }

        var assets = (open ?? (f => new ClassicPackSource(f)))(pack.Folder).ReadAssets();
        Remember(pack, assets);
        return assets;
    }

    // Packs with the same files decode the same, wherever they are.
    private static string Key(ClassicPack pack) => string.Join(',', pack.Files.Select(f => f.Sha256));
}
