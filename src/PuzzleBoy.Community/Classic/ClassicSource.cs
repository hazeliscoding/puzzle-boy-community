using System.Buffers.Binary;
using PuzzleBoy.Nocturne;

namespace PuzzleBoy.Community.Classic;

/// <summary>
/// The player's own copy of Shin Megami Tensei III Nocturne HD Remaster, or a folder of its bundles, looked for only when
/// the player asks for a classic import. Reading goes through the engine's <see cref="GameInstall"/>, on a copy.
/// </summary>
internal sealed class ClassicSource
{
    /// <summary>The name of the bundle that holds the Puzzle Boy stage files.</summary>
    public const string Bundle = "pazzleboy";

    /// <summary>Where a bundle sits under a game folder, which is where <see cref="GameInstall.At"/> reads it.</summary>
    public static readonly string GameBundleFolder = Path.Combine("smt3hd_Data", "StreamingAssets", "PC");

    // The largest bundle the Classic theme reads, common, is 79 MB; anything far bigger isn't one of them.
    private const long MaxBundleBytes = 256L * 1024 * 1024;

    // Where the bundles can be under a folder the player names: the game's own folder, a copy of smt3hd_Data, or a
    // folder the bundles were copied into.
    private static readonly string[] BundleFolders = [GameBundleFolder, Path.Combine("StreamingAssets", "PC"), ""];

    private ClassicSource(string folder, string bundleFolder) => (Folder, BundleFolder) = (folder, bundleFolder);

    /// <summary>The folder the game was found in.</summary>
    public string Folder { get; }

    /// <summary>The full path of the folder holding the bundles.</summary>
    public string BundleFolder { get; }

    /// <summary>The full path of the <c>pazzleboy</c> bundle.</summary>
    public string BundlePath => Path.Combine(BundleFolder, Bundle);

    /// <summary>
    /// The game at <paramref name="folder"/>: one holding <c>smt3hd.exe</c> and <c>smt3hd_Data</c>, a copy of
    /// <c>smt3hd_Data</c>, or a folder holding copied bundles. Null when the <c>pazzleboy</c> bundle is in none of those.
    /// </summary>
    public static ClassicSource? At(string folder) =>
        BundleFolders.Select(relative => Path.Combine(folder, relative)).Where(f => File.Exists(Path.Combine(f, Bundle)))
            .Select(f => new ClassicSource(folder, Path.GetFullPath(f))).FirstOrDefault();

    /// <summary>The first of <paramref name="places"/> that holds the game, or null.</summary>
    public static ClassicSource? Find(IEnumerable<string> places) => places.Select(At).FirstOrDefault(source => source is not null);

    /// <summary>
    /// Where a copy usually is: beside this game, then the first Steam library holding it (the engine's search). Lazy, and
    /// the Steam search can throw an <see cref="IOException"/> for an unreadable library list.
    /// </summary>
    public static IEnumerable<string> UsualPlaces()
    {
        yield return AppContext.BaseDirectory;
        if (GameInstall.Find() is { } steam)
        {
            yield return steam.Directory;
        }
    }

    /// <summary>
    /// Every TextAsset in the <c>pazzleboy</c> bundle, by name. The bundle is checked, copied to a scratch folder and read
    /// there, so a damaged file never stays locked. Throws whatever AssetsTools.NET throws for a file that isn't a
    /// readable bundle, so a caller must catch broadly.
    /// </summary>
    public Dictionary<string, byte[]> ReadTextAssets()
    {
        var scratch = Directory.CreateTempSubdirectory("puzzle-boy-community-classic-");
        try
        {
            var copy = Path.Combine(scratch.FullName, GameBundleFolder, Bundle);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            using (var source = new FileStream(BundlePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                CheckHeader(source);
                using var target = File.Create(copy);
                source.CopyTo(target);
            }

            return GameInstall.At(scratch.FullName)!.ReadTextAssets(Bundle);
        }
        finally
        {
            DeleteScratch(scratch.FullName);
        }
    }

    /// <summary>
    /// Deletes a scratch folder. When AssetsTools.NET throws on a damaged file it leaves its stream open until the
    /// collector finalizes it, which on Windows keeps the file from being deleted.
    /// </summary>
    internal static void DeleteScratch(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // A scratch folder left in the temp folder is harmless; the system cleans it up.
            }
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidDataException"/> unless <paramref name="stream"/> is a plausibly sized UnityFS bundle whose
    /// header sizes fit the file, then rewinds it. AssetsTools.NET allocates whatever the header declares, so a damaged
    /// header would otherwise ask for gigabytes.
    /// </summary>
    internal static void CheckHeader(Stream stream)
    {
        if (stream.Length > MaxBundleBytes)
        {
            throw new InvalidDataException($"it's {stream.Length} bytes, far more than a Puzzle Boy bundle");
        }

        var reader = new BinaryReader(stream);
        if (ReadString(reader) != "UnityFS")
        {
            throw new InvalidDataException("it isn't a UnityFS bundle");
        }

        reader.ReadBytes(4); // format version
        ReadString(reader); // generation
        ReadString(reader); // engine version
        var size = BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(8));
        var packedInfo = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var info = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        if (size != stream.Length || packedInfo > stream.Length || info > MaxBundleBytes)
        {
            throw new InvalidDataException($"its header declares sizes ({size}, {packedInfo}, {info}) that don't fit its {stream.Length} bytes");
        }

        stream.Position = 0;

        // Reads a NUL-terminated ASCII string of at most 64 characters; anything longer isn't a bundle header.
        static string ReadString(BinaryReader reader)
        {
            var text = new System.Text.StringBuilder();
            for (var b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            {
                if (text.Length == 64)
                {
                    throw new InvalidDataException("it isn't a UnityFS bundle");
                }

                text.Append((char)b);
            }

            return text.ToString();
        }
    }
}
