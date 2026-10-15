using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Nocturne;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>
/// Handmade stand-ins for the player's bundles: files with a plausible UnityFS header and a JSON payload, which
/// <see cref="Open"/> reads in place of the engine's bundle reader. No Nocturne data is ever involved.
/// </summary>
internal static class ClassicFixture
{
    // The header Bundle writes: the signature, version and two version strings (30 bytes), then three sizes (16).
    private const int HeaderBytes = 46;

    /// <summary>A file the header check accepts: <c>UnityFS</c>, declared sizes that fit, then <paramref name="payload"/>.</summary>
    public static byte[] Bundle(string payload)
    {
        var header = new List<byte>();
        header.AddRange("UnityFS\0"u8.ToArray());
        header.AddRange(new byte[] { 0, 0, 0, 6 });
        header.AddRange("5.x.x\0"u8.ToArray());
        header.AddRange("2019.4.40f1\0"u8.ToArray());
        var body = Encoding.UTF8.GetBytes(payload);
        var size = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(size, header.Count + 16 + body.Length);
        header.AddRange(size);
        return [.. header, .. body];
    }

    /// <summary>A stand-in <c>pazzleboy</c> holding <paramref name="stages"/> by file number, plus a non-stage asset as the real one has.</summary>
    public static byte[] Pazzleboy(IReadOnlyDictionary<int, byte[]> stages)
    {
        var assets = new JsonObject { ["sample.txt"] = Convert.ToBase64String("not a stage"u8) };
        foreach (var (file, bytes) in stages)
        {
            assets[ClassicStages.AssetName(file)] = Convert.ToBase64String(bytes);
        }

        return Bundle(assets.ToJsonString());
    }

    /// <summary>Every classic stage file, each <paramref name="stage"/>.</summary>
    public static Dictionary<int, byte[]> AllStages(Stage stage) =>
        ClassicStages.Original.Concat(ClassicStages.Bonus).ToDictionary(f => f, _ => stage.ToFile());

    /// <summary>
    /// Writes the bundles into <paramref name="bundleFolder"/>: <c>pazzleboy</c> with <paramref name="stages"/>, and
    /// stand-ins for the rest; only those of a schema 1 pack when <paramref name="schema1"/>. Returns the folder.
    /// </summary>
    public static string WriteBundles(string bundleFolder, IReadOnlyDictionary<int, byte[]> stages, bool schema1 = false)
    {
        Directory.CreateDirectory(bundleFolder);
        File.WriteAllBytes(Path.Combine(bundleFolder, "pazzleboy"), Pazzleboy(stages));
        foreach (var bundle in (schema1 ? ClassicPack.BoardBundles : ClassicPack.Bundles).Where(b => b != "pazzleboy"))
        {
            File.WriteAllBytes(Path.Combine(bundleFolder, bundle), Bundle($"{{\"{bundle}\": \"\"}}"));
        }

        return bundleFolder;
    }

    /// <summary>
    /// A game folder under <paramref name="root"/>, laid out as the install is, with only a schema 1 pack's bundles when
    /// <paramref name="schema1"/>. Returns the game folder.
    /// </summary>
    public static string GameFolder(string root, IReadOnlyDictionary<int, byte[]> stages, bool schema1 = false)
    {
        var game = Path.Combine(root, "game");
        WriteBundles(Path.Combine(game, "smt3hd_Data", "StreamingAssets", "PC"), stages, schema1);
        File.WriteAllText(Path.Combine(game, "smt3hd.exe"), "");
        return game;
    }

    /// <summary>Imports a pack of <paramref name="stage"/> everywhere into <paramref name="dataDir"/>, as the game does.</summary>
    public static void ImportPack(string dataDir, Stage stage)
    {
        var source = Directory.CreateTempSubdirectory("puzzle-boy-community-source-").FullName;
        try
        {
            var result = ClassicImport.FromFolder(GameFolder(source, AllStages(stage)), dataDir, Open);
            Assert.True(result.Imported, result.Message);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// The stage-only import an earlier version stored: <c>classic/stages/pbMapNN.map</c> and an <c>index.json</c> with each
    /// file's SHA-256.
    /// </summary>
    public static void StoreLegacy(string dataDir, IReadOnlyDictionary<int, byte[]> files)
    {
        var folder = Path.Combine(ClassicStages.Folder(dataDir), "stages");
        Directory.CreateDirectory(folder);
        var hashes = new JsonObject();
        foreach (var (file, bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(folder, ClassicStages.AssetName(file)), bytes);
            hashes[ClassicStages.AssetName(file)] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        var index = new JsonObject { ["format"] = "puzzle-boy-classic-import", ["schemaVersion"] = 1, ["source"] = "handmade", ["importedAt"] = "2026-10-15T00:00:00Z", ["files"] = hashes };
        File.WriteAllText(Path.Combine(ClassicStages.Folder(dataDir), "index.json"), index.ToJsonString());
    }

    /// <summary>A one-texel texture.</summary>
    public static TextureData Pixel { get; } = new(1, 1, [new byte[4]]);

    /// <summary>A character with no nodes or meshes, which poses and draws nothing.</summary>
    public static CharacterData Character { get; } = new([], 0, 0, [], new Dictionary<string, TextureData>(), AnimationCurves.Parse([], 0, 0, []), [], 1, Pixel);

    /// <summary>A handmade font with only the glyphs <c>A</c> to <c>Z</c> and a space, each 40 units wide, on a blank atlas.</summary>
    public static FontData Font { get; } = new(85, 74.8f, -10.2f,
        " ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToDictionary(c => c, c => new FontGlyph(c == ' ' ? 0 : 36, 60, 2, 60, 40, new Microsoft.Xna.Framework.Rectangle(0, 0, c == ' ' ? 0 : 1, 1))),
        1, 1, [0]);

    /// <summary>The interface a stand-in schema 2 pack decodes to: blank sheets, <see cref="Font"/> and two strings.</summary>
    public static ClassicUi Ui { get; } = new(
        new PuzzleBoy.Rendering.UiSheets(Pixel, Pixel, Pixel, Pixel, ClassicUi.Blank, ClassicUi.Blank),
        new TextData(Font, new Dictionary<string, TextMaterial>(), new Dictionary<string, string> { ["PBHELP_L0046"] = "UNDO", ["GUIDE_L0012"] = "MOVE" }));

    /// <summary>Reads a pack of stand-in bundles, as the engine reads real ones.</summary>
    public static IClassicAssetSource Open(string folder) => new Source(folder);

    private sealed class Source(string folder) : IClassicAssetSource
    {
        public IReadOnlyDictionary<string, byte[]> ReadTextAssets()
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, ClassicSource.GameBundleFolder, "pazzleboy"));
            var json = Encoding.UTF8.GetString(bytes[HeaderBytes..]);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!.ToDictionary(e => e.Key, e => Convert.FromBase64String(e.Value));
        }

        public ClassicAssets ReadAssets()
        {
            // Each bundle must be there and look like one, as the engine's reader would insist. A broken board bundle
            // throws; a broken interface bundle leaves the interface out with the reason, as the real reader does.
            bool Bundle(string bundle) => File.Exists(Path.Combine(folder, ClassicSource.GameBundleFolder, bundle))
                && File.ReadAllBytes(Path.Combine(folder, ClassicSource.GameBundleFolder, bundle)).AsSpan().StartsWith("UnityFS\0"u8);
            if (ClassicPack.BoardBundles.FirstOrDefault(b => !Bundle(b)) is { } board)
            {
                throw new InvalidDataException($"{board} isn't a bundle");
            }

            var assets = new ClassicAssets(Pixel, Pixel, new Dictionary<string, SoundData>(), Character);
            if (!ClassicPack.UiBundles.Any(b => File.Exists(Path.Combine(folder, ClassicSource.GameBundleFolder, b))))
            {
                return assets;
            }

            return ClassicPack.UiBundles.FirstOrDefault(b => !Bundle(b)) is { } broken
                ? assets with { UiProblem = $"InvalidDataException: {broken} isn't a bundle" }
                : assets with { Ui = Ui };
        }
    }
}
