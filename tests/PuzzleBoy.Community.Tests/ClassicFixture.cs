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
    /// Writes the three bundles into <paramref name="bundleFolder"/>: <c>pazzleboy</c> with <paramref name="stages"/>, and
    /// stand-ins for <c>common</c> and <c>dvl0x150</c>. Returns the folder.
    /// </summary>
    public static string WriteBundles(string bundleFolder, IReadOnlyDictionary<int, byte[]> stages)
    {
        Directory.CreateDirectory(bundleFolder);
        File.WriteAllBytes(Path.Combine(bundleFolder, "pazzleboy"), Pazzleboy(stages));
        File.WriteAllBytes(Path.Combine(bundleFolder, "common"), Bundle("{\"common\": \"\"}"));
        File.WriteAllBytes(Path.Combine(bundleFolder, "dvl0x150"), Bundle("{\"dvl0x150\": \"\"}"));
        return bundleFolder;
    }

    /// <summary>A game folder under <paramref name="root"/>, laid out as the install is. Returns the game folder.</summary>
    public static string GameFolder(string root, IReadOnlyDictionary<int, byte[]> stages)
    {
        var game = Path.Combine(root, "game");
        WriteBundles(Path.Combine(game, "smt3hd_Data", "StreamingAssets", "PC"), stages);
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
            // Each bundle must be there and look like one, as the engine's reader would insist.
            foreach (var bundle in ClassicPack.Bundles)
            {
                if (!File.ReadAllBytes(Path.Combine(folder, ClassicSource.GameBundleFolder, bundle)).AsSpan().StartsWith("UnityFS\0"u8))
                {
                    throw new InvalidDataException($"{bundle} isn't a bundle");
                }
            }

            return new ClassicAssets(Pixel, Pixel, new Dictionary<string, SoundData>(), Character);
        }
    }
}
