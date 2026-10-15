using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Classic;

/// <summary>What a classic import did.</summary>
/// <param name="Message">A sentence for the player: the counts, or what went wrong and what to do about it.</param>
/// <param name="Original">How many of the original 20 stages were imported; 0 when nothing was.</param>
/// <param name="Bonus">How many of the 23 bonus stages were imported; 0 when nothing was.</param>
internal sealed record ClassicImportResult(string Message, int Original, int Bonus)
{
    /// <summary>True when stages were imported; false means the data folder was left as it was.</summary>
    public bool Imported => Original + Bonus > 0;
}

/// <summary>
/// Copies the Puzzle Boy stage files from the player's own copy of Nocturne into <c>&lt;data dir&gt;/classic</c>. It runs
/// only when the player chooses it, from the Import Classic screen or <c>--import-classic</c>. Nothing here throws: a
/// missing or damaged copy becomes a message, and the data folder is touched only once a stage has been read.
/// </summary>
internal static class ClassicImport
{
    private const string Verify = "In Steam, verify the game's files (Properties, Installed Files), then try again.";

    /// <summary>Looks for the game beside this one and in the Steam libraries, and imports from it.</summary>
    public static ClassicImportResult FromSteam(string dataDir)
    {
        ClassicInstall? install;
        try
        {
            install = ClassicInstall.Find(ClassicInstall.UsualPlaces());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            install = null;
        }

        return Import(install, dataDir, folder: null);
    }

    /// <summary>Imports from <paramref name="folder"/>, as the player typed it.</summary>
    public static ClassicImportResult FromFolder(string folder, string dataDir)
    {
        folder = folder.Trim().Trim('"').Trim();
        if (folder.Length == 0 || folder.Contains('\0'))
        {
            return Fail("Type the folder that holds smt3hd.exe.");
        }

        return Directory.Exists(folder)
            ? Import(ClassicInstall.At(folder), dataDir, folder)
            : Fail($"There's no folder at {folder}. Type the folder that holds smt3hd.exe, such as Steam's steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}smt3hd.");
    }

    /// <summary>
    /// Reads every stage both classic lists use from <paramref name="install"/>, checks each with the engine and stores
    /// the ones that parse. <paramref name="folder"/> is what the player typed, or null for a search.
    /// </summary>
    internal static ClassicImportResult Import(ClassicInstall? install, string dataDir, string? folder)
    {
        if (install is null)
        {
            var bundle = Path.Combine("smt3hd_Data", "StreamingAssets", "PC", ClassicInstall.Bundle);
            return Fail(folder is null
                ? "Couldn't find Shin Megami Tensei III Nocturne HD Remaster beside this game or in your Steam libraries. Choose Enter Folder Path and type the folder that holds smt3hd.exe."
                : $"{folder} has no Puzzle Boy stages: there's no {bundle} in it. Type the folder that holds smt3hd.exe, or a copy of its smt3hd_Data folder.");
        }

        Dictionary<string, byte[]> assets;
        try
        {
            assets = install.ReadTextAssets();
        }
        catch (Exception e)
        {
            // AssetsTools.NET throws all sorts for a file that isn't a bundle; any of them means the same to the player.
            // That includes OutOfMemoryException: it allocates what a damaged header declares, and the failed allocation
            // leaves nothing behind.
            return Fail($"The Puzzle Boy bundle at {install.BundlePath} can't be read ({e.GetType().Name}), so nothing was imported. {Verify}");
        }

        var stages = new Dictionary<int, byte[]>();
        var left = new List<string>();
        foreach (var file in ClassicStages.Original.Concat(ClassicStages.Bonus))
        {
            var name = ClassicStages.AssetName(file);
            if (!assets.TryGetValue(name, out var bytes))
            {
                left.Add($"{name} (missing)");
                continue;
            }

            try
            {
                Stage.Parse(bytes);
                stages[file] = bytes;
            }
            catch (Exception e) when (e is InvalidDataException or FormatException)
            {
                left.Add($"{name} (damaged)");
            }
        }

        if (stages.Count == 0)
        {
            return Fail($"The bundle at {install.BundlePath} holds none of the Puzzle Boy stages, so nothing was imported. {Verify}");
        }

        if (ClassicStages.Store(dataDir, install.Folder, stages) is { } error)
        {
            return Fail(error);
        }

        var original = ClassicStages.Original.Count(stages.ContainsKey);
        var bonus = ClassicStages.Bonus.Count(stages.ContainsKey);
        var message = $"Imported {stages.Count} classic stages from {install.Folder}: {original} of {ClassicStages.Original.Count} in {ClassicStages.OriginalTitle} and {bonus} of {ClassicStages.Bonus.Count} in {ClassicStages.BonusTitle}.";
        return new ClassicImportResult(left.Count == 0 ? message : $"{message} Left out: {string.Join(", ", left)}. {Verify}", original, bonus);
    }

    private static ClassicImportResult Fail(string message) => new(message, 0, 0);
}
