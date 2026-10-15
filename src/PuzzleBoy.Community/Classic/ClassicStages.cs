using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The classic stages a player imported from their own copy of Nocturne, kept in <c>&lt;data dir&gt;/classic</c>: the raw
/// stage files in <c>stages/</c> and an <c>index.json</c> with where they came from, when, and each file's SHA-256. It
/// only ever reads and writes the data folder, never the game. Nothing here throws for a bad file.
/// </summary>
internal static class ClassicStages
{
    /// <summary>The pack id of the original's campaign. It has a slash, so no imported pack can take it.</summary>
    public const string OriginalPackId = "classic/original";

    /// <summary>The pack id of the solved unused stages.</summary>
    public const string BonusPackId = "classic/bonus";

    /// <summary>The original campaign's name in stage select.</summary>
    public const string OriginalTitle = "Classic: Original 20";

    /// <summary>The bonus stages' name in stage select.</summary>
    public const string BonusTitle = "Classic: Bonus";

    // A stage file is 828 bytes; anything far bigger isn't one.
    private const int MaxStageBytes = 4096;

    /// <summary>
    /// The stage files the original plays, in its order: puzzle-boy's <c>OriginalStageOrder</c> (its ROADMAP.md,
    /// "M0 findings").
    /// </summary>
    public static IReadOnlyList<int> Original { get; } = [2, 4, 32, 8, 38, 36, 37, 48, 42, 49, 40, 20, 22, 13, 21, 24, 14, 10, 25, 26];

    /// <summary>
    /// The unused stage files that a solution clears, ordered by the pushes and turns it takes, file number breaking ties:
    /// puzzle-boy's ROADMAP.md M4, and <c>PlayOrder.Bonus</c> at its <c>pre-split-baseline</c> tag.
    /// </summary>
    public static IReadOnlyList<int> Bonus { get; } = [1, 30, 31, 0, 3, 9, 35, 43, 5, 6, 33, 7, 19, 47, 39, 41, 45, 34, 12, 46, 15, 11, 44];

    /// <summary>The TextAsset name of stage file <paramref name="file"/>, also its file name in the classic folder.</summary>
    public static string AssetName(int file) => $"pbMap{file:D2}.map";

    /// <summary>The classic folder in <paramref name="dataDir"/>.</summary>
    public static string Folder(string dataDir) => Path.Combine(dataDir, "classic");

    /// <summary>
    /// Replaces the classic folder with <paramref name="files"/> (stage file number to its raw bytes) and an index naming
    /// <paramref name="source"/>. Everything is written to a scratch folder beside it first. Returns null, or why it failed.
    /// </summary>
    public static string? Store(string dataDir, string source, IReadOnlyDictionary<int, byte[]> files)
    {
        var folder = Folder(dataDir);
        var temp = folder + ".tmp";
        try
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }

            Directory.CreateDirectory(Path.Combine(temp, "stages"));
            var hashes = new JsonObject();
            foreach (var (file, bytes) in files.OrderBy(f => f.Key))
            {
                File.WriteAllBytes(Path.Combine(temp, "stages", AssetName(file)), bytes);
                hashes[AssetName(file)] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            }

            var index = new JsonObject
            {
                ["format"] = "puzzle-boy-classic-import",
                ["schemaVersion"] = 1,
                ["source"] = source,
                ["importedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["files"] = hashes,
            };
            File.WriteAllText(Path.Combine(temp, "index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (Remove(dataDir) is { } error)
            {
                Directory.Delete(temp, recursive: true);
                return error;
            }

            Directory.Move(temp, folder);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"The classic stages couldn't be saved in {folder}: {e.Message}";
        }
    }

    /// <summary>
    /// The imported campaigns, <see cref="OriginalTitle"/> then <see cref="BonusTitle"/>, each in its documented order:
    /// none when nothing was imported. A stage file that's missing, changed since the import or unreadable is left out,
    /// with a sentence in <paramref name="problems"/>; the rest still load. A campaign with no stage is left out.
    /// </summary>
    public static List<StagePack> Load(string dataDir, List<string> problems)
    {
        var folder = Folder(dataDir);
        var indexPath = Path.Combine(folder, "index.json");
        if (!File.Exists(indexPath))
        {
            return [];
        }

        if (ContentFile.IsLink(folder))
        {
            problems.Add("classic: the folder is a link, so its stages aren't loaded. Remove the classic stages and import them again.");
            return [];
        }

        if (ContentFile.ReadJson(indexPath, 64 * 1024, "classic/index.json", problems) is not { } index
            || !ContentFile.CheckHeader(index, "puzzle-boy-classic-import", "classic/index.json", problems))
        {
            return [];
        }

        var hashes = index.TryGetProperty("files", out var files) ? files : default;
        var packs = new List<StagePack>();
        foreach (var (packId, title, prefix, order) in new[] { (OriginalPackId, OriginalTitle, "Stage", Original), (BonusPackId, BonusTitle, "Bonus", Bonus) })
        {
            var stages = new List<PackStage>();
            for (var i = 0; i < order.Count; i++)
            {
                var name = AssetName(order[i]);
                var label = $"classic/stages/{name}";
                if (ContentFile.Resolve(folder, $"stages/{name}", label, problems) is not { } path
                    || ContentFile.Read(path, MaxStageBytes, label, problems) is not { } bytes)
                {
                    continue;
                }

                if (ContentFile.String(hashes, name) != Convert.ToHexStringLower(SHA256.HashData(bytes)))
                {
                    problems.Add($"{label}: the file changed since the import.");
                    continue;
                }

                try
                {
                    stages.Add(new PackStage($"{packId}/{order[i]:D2}", $"{prefix} {i + 1}", Stage.Parse(bytes)));
                }
                catch (Exception e) when (e is InvalidDataException or FormatException)
                {
                    problems.Add($"{label}: it can't be read as a stage ({e.Message}).");
                }
            }

            if (stages.Count > 0)
            {
                packs.Add(new StagePack(packId, title, "ATLUS", "NOASSERTION", stages));
            }
        }

        return packs;
    }

    /// <summary>Deletes the classic folder, if there is one. Progress on its stages stays in the save. Returns null, or why it failed.</summary>
    public static string? Remove(string dataDir)
    {
        var folder = Folder(dataDir);
        try
        {
            if (ContentFile.IsLink(folder))
            {
                // Only the link goes; whatever it points at is left alone.
                Directory.Delete(folder);
            }
            else if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"The classic stages couldn't be removed from {folder}: {e.Message}";
        }
    }
}
