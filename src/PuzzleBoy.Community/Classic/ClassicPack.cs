using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PuzzleBoy.Community.Content;

namespace PuzzleBoy.Community.Classic;

/// <summary>A file of a classic pack.</summary>
/// <param name="Path">Its path inside the pack, with <c>/</c>, such as <c>smt3hd_Data/StreamingAssets/PC/common</c>.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">Its SHA-256, 64 lowercase hex digits.</param>
internal sealed record ClassicPackFile(string Path, long Size, string Sha256);

/// <summary>
/// A classic asset pack: the bundles the Classic theme reads, copied byte for byte from the player's own copy of Nocturne
/// (origin <c>local-import</c>) or from a licensed package (origin <c>licensed</c>), under
/// <c>smt3hd_Data/StreamingAssets/PC/</c> so the engine's <c>GameInstall.At</c> reads the folder as a game, and
/// <c>classic-pack.json</c> listing each file's size and SHA-256. The player's own pack lives in
/// <c>&lt;data dir&gt;/classic</c>; nothing here ever ships with the game.
/// </summary>
/// <param name="Folder">The pack's folder.</param>
/// <param name="Origin"><see cref="LocalImport"/> or <see cref="Licensed"/>.</param>
/// <param name="CreatedFrom">The folder a local import read, or null.</param>
/// <param name="Created">When it was made, as written (ISO 8601), or null.</param>
/// <param name="Licensor">Who licensed a licensed pack; null for a local import.</param>
/// <param name="License">A licensed pack's license; null for a local import.</param>
/// <param name="Evidence">A URL or text showing a licensed pack's rights; null for a local import.</param>
/// <param name="Files">The bundles, one per name in <see cref="Bundles"/>.</param>
internal sealed record ClassicPack(
    string Folder, string Origin, string? CreatedFrom, string? Created, string? Licensor, string? License, string? Evidence,
    IReadOnlyList<ClassicPackFile> Files)
{
    /// <summary>The manifest's <c>format</c>.</summary>
    public const string Format = "puzzle-boy-classic-pack";

    /// <summary>The manifest's file name, at the pack's top.</summary>
    public const string ManifestName = "classic-pack.json";

    /// <summary>The origin of a pack built from the player's own install.</summary>
    public const string LocalImport = "local-import";

    /// <summary>The origin of a properly licensed package the player points the game at.</summary>
    public const string Licensed = "licensed";

    /// <summary>What an exported pack says about itself.</summary>
    public const string Note = "Made from your own copy of Nocturne for your own computers. Don't share it.";

    /// <summary>The largest bundle a pack may hold; <c>common</c>, the largest, is 79 MB.</summary>
    public const long MaxBundleBytes = 128L * 1024 * 1024;

    /// <summary>The most a pack may hold in all; the three bundles are about 86 MB.</summary>
    public const long MaxPackBytes = 160L * 1024 * 1024;

    private const string Again = "Import your classic pack again, from your Nocturne install or the pack you exported.";

    // The last pack whose hashes were checked, so a session hashes its 86 MB once. A reference, so threads never see half of one.
    private static Tuple<string, ClassicPack>? _verified;

    /// <summary>
    /// The bundles the Classic theme reads: <c>pazzleboy</c> (stages, board atlas, effects and music), <c>common</c>
    /// (backdrop and toon ramp) and <c>dvl0x150</c> (Jack-o'-Lantern). No text bundle: the Manikin Kid's dialogue is in
    /// <c>pazzleboy_en</c>, which no pack holds.
    /// </summary>
    public static IReadOnlyList<string> Bundles { get; } = ["pazzleboy", "common", "dvl0x150"];

    /// <summary>All the files' sizes, in bytes.</summary>
    public long Size => Files.Sum(f => f.Size);

    /// <summary>A bundle's path inside a pack.</summary>
    public static string PathOf(string bundle) => $"smt3hd_Data/StreamingAssets/PC/{bundle}";

    /// <summary>True when <paramref name="folder"/> holds a classic pack manifest, whatever state it's in.</summary>
    public static bool IsIn(string folder) => File.Exists(Path.Combine(folder, ManifestName));

    /// <summary>
    /// Reads the pack in <paramref name="folder"/>: its manifest and each file's size, and with <paramref name="checkHashes"/>
    /// each file's SHA-256 too. Returns null, with <paramref name="problem"/> saying which file is wrong, how, and what to
    /// do. Nothing here throws for a bad pack.
    /// </summary>
    public static ClassicPack? Open(string folder, bool checkHashes, out string? problem)
    {
        var pack = Read(folder, out problem);
        if (pack is null || !checkHashes)
        {
            return pack;
        }

        try
        {
            var key = Key(pack);
            if (_verified is { } verified && verified.Item1 == key)
            {
                return pack;
            }

            foreach (var file in pack.Files)
            {
                using var stream = File.OpenRead(Path.Combine(folder, file.Path));
                if (Convert.ToHexStringLower(SHA256.HashData(stream)) != file.Sha256)
                {
                    problem = $"{file.Path} doesn't match its SHA-256 in {ManifestName}: the file is damaged or was changed. {Again}";
                    return null;
                }
            }

            _verified = Tuple.Create(key, pack);
            return pack;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"The classic pack in {folder} can't be read ({e.Message}). Close anything using it and try again.";
            return null;
        }
    }

    /// <summary>
    /// The manifest for <paramref name="files"/>, as written to <see cref="ManifestName"/>: a local import names the folder
    /// it read and today's date.
    /// </summary>
    public static string LocalManifest(string createdFrom, IEnumerable<ClassicPackFile> files)
    {
        var manifest = new JsonObject
        {
            ["format"] = Format,
            ["schemaVersion"] = 1,
            ["origin"] = LocalImport,
            ["createdFrom"] = createdFrom,
            ["created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["note"] = Note,
            ["files"] = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["size"] = f.Size, ["sha256"] = f.Sha256 })]),
        };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    /// <summary>
    /// Checks the manifest of the pack in <paramref name="folder"/> and that each file it lists is there with its size.
    /// Returns null with the first problem.
    /// </summary>
    private static ClassicPack? Read(string folder, out string? problem)
    {
        problem = null;
        var manifestPath = Path.Combine(folder, ManifestName);
        if (ContentFile.IsLink(folder) || ContentFile.IsLink(manifestPath))
        {
            problem = $"The classic pack at {folder} is a link, which the game doesn't follow. {Again}";
            return null;
        }

        if (!File.Exists(manifestPath))
        {
            problem = $"There's no {ManifestName} in {folder}, so it isn't a classic pack. {Again}";
            return null;
        }

        var problems = new List<string>();
        if (ContentFile.ReadJson(manifestPath, 64 * 1024, ManifestName, problems) is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            problem = $"{(problems.Count > 0 ? problems[0] : $"{ManifestName}: it isn't a JSON object.")} {Again}";
            return null;
        }

        if (ContentFile.String(root, "format") != Format)
        {
            problem = $"{ManifestName} isn't a Puzzle Boy classic pack manifest: its \"format\" should be \"{Format}\". {Again}";
            return null;
        }

        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema < 1)
        {
            problem = $"{ManifestName} has no valid \"schemaVersion\"; it should be 1. {Again}";
            return null;
        }

        if (schema > 1)
        {
            problem = $"This classic pack was made by a newer version of Puzzle Boy Community Edition (schema version {schema}). Update the game, or import from your own Nocturne install again.";
            return null;
        }

        var origin = ContentFile.String(root, "origin");
        if (origin is not (LocalImport or Licensed))
        {
            problem = $"{ManifestName}'s \"origin\" is {(origin is null ? "missing" : $"\"{origin}\"")}; it must be \"{LocalImport}\" or \"{Licensed}\". {Again}";
            return null;
        }

        var (licensor, license, evidence) = (ContentFile.String(root, "licensor"), ContentFile.String(root, "license"), ContentFile.String(root, "evidence"));
        if (origin == Licensed && (licensor is null || license is null || evidence is null))
        {
            var missing = new[] { ("licensor", licensor), ("license", license), ("evidence", evidence) }.Where(p => p.Item2 is null).Select(p => $"\"{p.Item1}\"");
            problem = $"This licensed classic pack doesn't give its {string.Join(" or ", missing)}, so it isn't used. A licensed pack must name its licensor, its license and the evidence of the rights; ask whoever licensed it for a complete pack.";
            return null;
        }

        if (!root.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            problem = $"{ManifestName} has no \"files\" list. {Again}";
            return null;
        }

        var allowed = Bundles.Select(PathOf).ToList();
        var files = new List<ClassicPackFile>();
        foreach (var entry in list.EnumerateArray())
        {
            var path = ContentFile.String(entry, "path");
            if (path is null || !allowed.Contains(path))
            {
                problem = $"{ManifestName} lists {(path is null ? "a file with no path" : $"\"{path}\"")}, which isn't one of the files a classic pack holds ({string.Join(", ", Bundles)} under smt3hd_Data/StreamingAssets/PC/). {Again}";
                return null;
            }

            if (files.Any(f => f.Path == path))
            {
                problem = $"{ManifestName} lists {path} twice. {Again}";
                return null;
            }

            var sha = ContentFile.String(entry, "sha256");
            if (!entry.TryGetProperty("size", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out var size) || size <= 0 || size > MaxBundleBytes
                || sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigitLower))
            {
                problem = $"{ManifestName} gives {path} no valid \"size\" (1 byte to {MaxBundleBytes / 1024 / 1024} MiB) and \"sha256\" (64 lowercase hex digits). {Again}";
                return null;
            }

            files.Add(new ClassicPackFile(path, size, sha));
        }

        if (allowed.FirstOrDefault(path => files.All(f => f.Path != path)) is { } absent)
        {
            problem = $"{ManifestName} doesn't list {absent}, which the Classic theme needs. {Again}";
            return null;
        }

        foreach (var file in files)
        {
            if (ContentFile.Resolve(folder, file.Path, file.Path, problems) is not { } full)
            {
                problem = $"{problems[^1]} {Again}";
                return null;
            }

            var info = new FileInfo(full);
            if (!info.Exists)
            {
                problem = $"The classic pack is missing {file.Path}. {Again}";
                return null;
            }

            if (info.Length != file.Size)
            {
                problem = $"{file.Path} is {info.Length:N0} bytes, but {ManifestName} says {file.Size:N0}: the file is damaged or was changed. {Again}";
                return null;
            }
        }

        return new ClassicPack(folder, origin, ContentFile.String(root, "createdFrom"), ContentFile.String(root, "created"), licensor, license, evidence, files);
    }

    // What makes a checked pack the same pack: its folder, its manifest and each file's size and time.
    private static string Key(ClassicPack pack) =>
        string.Join('|', new[] { Path.GetFullPath(pack.Folder), File.ReadAllText(Path.Combine(pack.Folder, ManifestName)) }
            .Concat(pack.Files.Select(f => $"{f.Path} {File.GetLastWriteTimeUtc(Path.Combine(pack.Folder, f.Path)).Ticks}")));
}
