using System.Security.Cryptography;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The classic stages, read from the player's classic pack in <c>&lt;data dir&gt;/classic</c> (its <c>pazzleboy</c> bundle),
/// or from the stage-only import an earlier version stored there: the raw stage files in <c>stages/</c> and an
/// <c>index.json</c> with each file's SHA-256. It only ever reads the data folder, never the game. Nothing here throws
/// for a bad file.
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

    // The campaigns last read from a pack, by the pack's folder and hashes: reading its bundle takes a moment. A reference,
    // so threads never see half of one.
    private static Tuple<string, List<StagePack>, List<string>>? _cache;

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

    /// <summary>The TextAsset name of stage file <paramref name="file"/>, also its file name in a stage-only import.</summary>
    public static string AssetName(int file) => $"pbMap{file:D2}.map";

    /// <summary>The classic folder in <paramref name="dataDir"/>, which holds the classic pack.</summary>
    public static string Folder(string dataDir) => Path.Combine(dataDir, "classic");

    /// <summary>
    /// The imported campaigns, <see cref="OriginalTitle"/> then <see cref="BonusTitle"/>, each in its documented order:
    /// none when nothing was imported. A classic pack is checked against its manifest first. A stage that's missing,
    /// changed since a stage-only import or unreadable is left out, with a sentence in <paramref name="problems"/>; the rest
    /// still load. A campaign with no stage is left out. <paramref name="open"/> reads a pack's folder; the default is
    /// the engine's reader.
    /// </summary>
    public static List<StagePack> Load(string dataDir, List<string> problems, Func<string, IClassicAssetSource>? open = null)
    {
        var folder = Folder(dataDir);
        if (ClassicPack.IsIn(folder))
        {
            return FromPack(folder, problems, open ?? (f => new ClassicPackSource(f, ui: false)));
        }

        // A stage-only import from an earlier version still plays, with the same stage ids, until a pack replaces it.
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
        return Campaigns(problems, (file, label) =>
        {
            if (ContentFile.Resolve(folder, $"stages/{AssetName(file)}", label, problems) is not { } path
                || ContentFile.Read(path, MaxStageBytes, label, problems) is not { } bytes)
            {
                return null;
            }

            if (ContentFile.String(hashes, AssetName(file)) != Convert.ToHexStringLower(SHA256.HashData(bytes)))
            {
                problems.Add($"{label}: the file changed since the import.");
                return null;
            }

            return bytes;
        });
    }

    /// <summary>
    /// The campaigns in the TextAssets of a <c>pazzleboy</c> bundle, by name; a stage missing from them or unreadable is
    /// left out with a sentence in <paramref name="problems"/>.
    /// </summary>
    public static List<StagePack> FromTextAssets(IReadOnlyDictionary<string, byte[]> assets, List<string> problems) =>
        Campaigns(problems, (file, label) =>
        {
            if (assets.TryGetValue(AssetName(file), out var bytes))
            {
                return bytes;
            }

            problems.Add($"{label}: it isn't in the pazzleboy bundle.");
            return null;
        });

    /// <summary>
    /// Deletes the classic folder, pack or stage-only import, if there is one. Progress on its stages stays in the save.
    /// Returns null, or why it failed.
    /// </summary>
    public static string? Remove(string dataDir)
    {
        var folder = Folder(dataDir);
        _cache = null;
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
            return $"The classic pack couldn't be removed from {folder}: {e.Message}";
        }
    }

    /// <summary>The campaigns from the pack in <paramref name="folder"/>, checked against its manifest.</summary>
    private static List<StagePack> FromPack(string folder, List<string> problems, Func<string, IClassicAssetSource> open)
    {
        if (ClassicPack.Open(folder, checkHashes: true, out var problem) is not { } pack)
        {
            problems.Add($"classic: {problem}");
            return [];
        }

        var key = $"{Path.GetFullPath(folder)}|{string.Join(',', pack.Files.Select(f => f.Sha256))}";
        if (_cache is { } cached && cached.Item1 == key)
        {
            problems.AddRange(cached.Item3);
            return cached.Item2;
        }

        var found = new List<string>();
        List<StagePack> packs;
        try
        {
            packs = FromTextAssets(open(folder).ReadTextAssets(), found);
        }
        catch (Exception e)
        {
            // The bundle reader throws all sorts for a damaged bundle; any of them means the same to the player.
            found.Add($"classic: the pazzleboy bundle in your classic pack can't be read ({e.GetType().Name}). Import your classic pack again.");
            packs = [];
        }

        _cache = Tuple.Create(key, packs, found);
        problems.AddRange(found);
        return packs;
    }

    /// <summary>
    /// Both campaigns, each stage's bytes from <paramref name="read"/>, given the file number and a label for problems; a
    /// null leaves the stage out.
    /// </summary>
    private static List<StagePack> Campaigns(List<string> problems, Func<int, string, byte[]?> read)
    {
        var packs = new List<StagePack>();
        foreach (var (packId, title, prefix, order) in new[] { (OriginalPackId, OriginalTitle, "Stage", Original), (BonusPackId, BonusTitle, "Bonus", Bonus) })
        {
            var stages = new List<PackStage>();
            for (var i = 0; i < order.Count; i++)
            {
                var label = $"classic/stages/{AssetName(order[i])}";
                if (read(order[i], label) is not { } bytes)
                {
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
}
