using System.Text.RegularExpressions;
using AssetsTools.NET.Extra;
using Microsoft.Win32;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The player's own copy of Shin Megami Tensei III Nocturne HD Remaster, looked for only when the player asks for a
/// classic import. It's the one place that reads the game's files, and the only code that uses AssetsTools.NET.
/// </summary>
/// <remarks>
/// Adapted from hazeliscoding/puzzle-boy GameInstall (same author, Apache-2.0), keeping only finding the game and
/// reading TextAssets from its <c>pazzleboy</c> bundle.
/// </remarks>
internal sealed partial class ClassicInstall
{
    /// <summary>The name of the bundle that holds the Puzzle Boy stage files.</summary>
    public const string Bundle = "pazzleboy";

    // Where the bundle can be under a folder the player names: the game's own folder, a copy of smt3hd_Data, or a
    // folder the bundles were copied into.
    private static readonly string[] BundleFolders = [Path.Combine("smt3hd_Data", "StreamingAssets", "PC"), Path.Combine("StreamingAssets", "PC"), ""];

    private ClassicInstall(string folder, string bundlePath) => (Folder, BundlePath) = (folder, bundlePath);

    /// <summary>The folder the game was found in.</summary>
    public string Folder { get; }

    /// <summary>The full path of the <c>pazzleboy</c> bundle.</summary>
    public string BundlePath { get; }

    /// <summary>
    /// The game at <paramref name="folder"/>: one holding <c>smt3hd.exe</c> and <c>smt3hd_Data</c>, a copy of
    /// <c>smt3hd_Data</c>, or a folder holding copied bundles. Null when the <c>pazzleboy</c> bundle is in none of those.
    /// </summary>
    public static ClassicInstall? At(string folder) =>
        BundleFolders.Select(relative => Path.Combine(folder, relative, Bundle)).Where(File.Exists).Select(bundle => new ClassicInstall(folder, Path.GetFullPath(bundle))).FirstOrDefault();

    /// <summary>The first of <paramref name="places"/> that holds the game, or null.</summary>
    public static ClassicInstall? Find(IEnumerable<string> places) => places.Select(At).FirstOrDefault(install => install is not null);

    /// <summary>Where a copy usually is: beside this game, then in every Steam library on this machine.</summary>
    public static IEnumerable<string> UsualPlaces() =>
        SteamRoots()
            .SelectMany(root => LibraryFolders(root).Prepend(root))
            .Select(library => Path.Combine(library, "steamapps", "common", "smt3hd"))
            .Prepend(AppContext.BaseDirectory);

    /// <summary>The library paths listed in Steam's <c>libraryfolders.vdf</c>.</summary>
    public static IEnumerable<string> LibraryPaths(string libraryFoldersVdf) =>
        PathEntry().Matches(libraryFoldersVdf).Select(match => match.Groups[1].Value.Replace(@"\\", @"\"));

    /// <summary>
    /// Every TextAsset in the <c>pazzleboy</c> bundle, by name. Throws whatever AssetsTools.NET throws for a file that
    /// isn't a readable bundle, so a caller must catch broadly.
    /// </summary>
    public Dictionary<string, byte[]> ReadTextAssets()
    {
        // The stream is ours: when AssetsTools.NET throws on a damaged file, UnloadAll doesn't close it, and the game's
        // own file would stay locked until this game quits.
        using var stream = new FileStream(BundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manager = new AssetsManager();
        try
        {
            var bundleFile = manager.LoadBundleFile(stream, BundlePath, true);
            var assets = manager.LoadAssetsFileFromBundle(bundleFile, 0, false);
            return assets.file.GetAssetsOfType(AssetClassID.TextAsset)
                .Select(info => manager.GetBaseField(assets, info))
                .ToDictionary(field => field["m_Name"].AsString, field => field["m_Script"].AsByteArray);
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static IEnumerable<string> LibraryFolders(string steamRoot)
    {
        try
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            return File.Exists(vdf) ? [.. LibraryPaths(File.ReadAllText(vdf))] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable list only means fewer places to look.
            return [];
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steamPath)
            {
                yield return steamPath;
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
        else
        {
            // Native, the Debian package's symlink, and Flatpak. The game itself runs under Proton there.
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex PathEntry();
}
