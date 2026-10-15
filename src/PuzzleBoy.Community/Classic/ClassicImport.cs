using System.IO.Compression;
using System.Security.Cryptography;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.Classic;

/// <summary>What a classic import did.</summary>
/// <param name="Message">A sentence for the player: the counts, or what went wrong and what to do about it.</param>
/// <param name="Original">How many of the original 20 stages the new pack holds; 0 when nothing was imported.</param>
/// <param name="Bonus">How many of the 23 bonus stages the new pack holds; 0 when nothing was imported.</param>
internal sealed record ClassicImportResult(string Message, int Original, int Bonus)
{
    /// <summary>True when a classic pack was imported; false means the data folder was left as it was.</summary>
    public bool Imported => Original + Bonus > 0;
}

/// <summary>
/// Makes the player's classic pack in <c>&lt;data dir&gt;/classic</c>: from their own copy of Nocturne, from a pack folder,
/// or from a zip that <see cref="Export"/> made on another computer. It runs only when the player chooses it, from the
/// Classic screen or <c>--import-classic</c>. Every import is built and read in a scratch folder beside the pack, and
/// replaces it with a rename only once it reads as the game will read it. Nothing here throws: a missing or damaged copy
/// becomes a message, and a failed import leaves the data folder as it was.
/// </summary>
internal static class ClassicImport
{
    /// <summary>The name of an exported pack, in <c>&lt;data dir&gt;/exports</c>.</summary>
    public const string ExportName = "puzzle-boy-classic-pack.zip";

    /// <summary>The most entries a pack zip may hold: the manifest, the note, eight bundles and their three folders.</summary>
    public const int MaxZipEntries = 16;

    private const string Verify = "In Steam, verify the game's files (Properties, Installed Files), then try again.";

    private const string ReadMe = "README.txt";

    /// <summary>Looks for the game beside this one and in the Steam libraries, and imports from it.</summary>
    public static ClassicImportResult FromSteam(string dataDir, Func<string, IClassicAssetSource>? open = null)
    {
        ClassicSource? source;
        try
        {
            source = ClassicSource.Find(ClassicSource.UsualPlaces());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            source = null;
        }

        return Import(source, dataDir, folder: null, open);
    }

    /// <summary>
    /// Imports from <paramref name="path"/>, as the player typed it: a zip made by <see cref="Export"/>, a folder holding a
    /// classic pack, or the game's folder.
    /// </summary>
    public static ClassicImportResult FromFolder(string path, string dataDir, Func<string, IClassicAssetSource>? open = null)
    {
        path = path.Trim().Trim('"').Trim();
        if (path.Length == 0 || path.Contains('\0'))
        {
            return Fail("Type the folder that holds smt3hd.exe, or the path of a classic pack.");
        }

        if (File.Exists(path))
        {
            return FromZip(path, dataDir, open);
        }

        if (!Directory.Exists(path))
        {
            return Fail($"There's no folder at {path}. Type the folder that holds smt3hd.exe, such as Steam's steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}smt3hd, or the path of a classic pack.");
        }

        return ClassicPack.IsIn(path) ? FromPackFolder(path, dataDir, open) : Import(ClassicSource.At(path), dataDir, path, open);
    }

    /// <summary>
    /// Builds the pack from the player's own copy, <paramref name="source"/>: copies each bundle the Classic theme reads,
    /// after checking its header, and records its size and SHA-256. <paramref name="folder"/> is what the player typed, or
    /// null for a search.
    /// </summary>
    internal static ClassicImportResult Import(ClassicSource? source, string dataDir, string? folder, Func<string, IClassicAssetSource>? open = null)
    {
        if (source is null)
        {
            var bundle = Path.Combine(ClassicSource.GameBundleFolder, ClassicSource.Bundle);
            return Fail(folder is null
                ? "Couldn't find Shin Megami Tensei III Nocturne HD Remaster beside this game or in your Steam libraries. Choose Import from a Folder or Zip and type the folder that holds smt3hd.exe."
                : $"{folder} has no Puzzle Boy stages: there's no {bundle} in it. Type the folder that holds smt3hd.exe, or a copy of its smt3hd_Data folder.");
        }

        // A missing bundle is found before anything is written.
        if (ClassicPack.Bundles.FirstOrDefault(b => !File.Exists(Path.Combine(source.BundleFolder, b))) is { } absent)
        {
            return Fail($"{source.Folder} has the Puzzle Boy stages but not the {absent} bundle the Classic theme needs ({Path.Combine(source.BundleFolder, absent)}). {Verify}");
        }

        var files = new List<ClassicPackFile>();
        return Install(dataDir, open, temp =>
        {
            foreach (var bundle in ClassicPack.Bundles)
            {
                var from = Path.Combine(source.BundleFolder, bundle);
                if (Copy(from, temp, bundle, expected: null, out var file) is { } problem)
                {
                    return (null, $"The {bundle} bundle at {from} can't be read ({problem}), so nothing was imported. {Verify}");
                }

                files.Add(file!);
            }

            return (ClassicPack.LocalManifest(source.Folder, files), null);
        }, $"from {source.Folder}");
    }

    /// <summary>Copies the classic pack in <paramref name="folder"/>, checking every file against its manifest.</summary>
    internal static ClassicImportResult FromPackFolder(string folder, string dataDir, Func<string, IClassicAssetSource>? open = null)
    {
        if (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(ClassicStages.Folder(dataDir)).TrimEnd(Path.DirectorySeparatorChar))
        {
            return Fail("That's the classic pack you already have.");
        }

        if (ClassicPack.Open(folder, checkHashes: false, out var problem) is not { } pack)
        {
            return Fail($"Nothing was imported. {problem}");
        }

        return Install(dataDir, open, temp =>
        {
            foreach (var expected in pack.Files)
            {
                var from = Path.Combine(folder, expected.Path);
                if (Copy(from, temp, Path.GetFileName(expected.Path), expected, out _) is { } bad)
                {
                    return (null, $"Nothing was imported: {expected.Path} in {folder} {bad}.");
                }
            }

            return (File.ReadAllText(Path.Combine(folder, ClassicPack.ManifestName)), null);
        }, $"from {folder}");
    }

    /// <summary>
    /// Imports a pack zip, as <see cref="Export"/> writes one, with the same care as a level pack: a size cap on the zip,
    /// each entry and the whole; only the manifest, the note and the three bundles, at their exact paths; no links and no
    /// name twice. Every file is then checked against the manifest.
    /// </summary>
    internal static ClassicImportResult FromZip(string zipPath, string dataDir, Func<string, IClassicAssetSource>? open = null)
    {
        var allowed = ClassicPack.Bundles.Select(ClassicPack.PathOf).Append(ClassicPack.ManifestName).Append(ReadMe).ToHashSet(StringComparer.Ordinal);
        string[] folders = ["smt3hd_Data/", "smt3hd_Data/StreamingAssets/", "smt3hd_Data/StreamingAssets/PC/"];
        try
        {
            var info = new FileInfo(zipPath);
            if (info.Length > ClassicPack.MaxPackBytes)
            {
                return Fail($"Nothing was imported: {zipPath} is over {ClassicPack.MaxPackBytes / 1024 / 1024} MiB, more than a classic pack holds.");
            }

            using var stream = info.OpenRead();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries)
            {
                return Fail($"Nothing was imported: {zipPath} holds {zip.Entries.Count} entries; a classic pack holds at most {MaxZipEntries}.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                var bad = PackArchive.CheckName(name) is { } why ? $"\"{name}\" {why}"
                    : ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0 ? $"\"{name}\" is a link, which a classic pack can't hold."
                    : !seen.Add(name) ? $"\"{name}\" is in the zip twice (names are compared ignoring case)."
                    : PackArchive.IsFinderData(name) || allowed.Contains(name) || folders.Contains(name) ? null
                    : $"\"{name}\" isn't part of a classic pack, which holds only {ClassicPack.ManifestName}, {ReadMe} and the {string.Join(", ", ClassicPack.Bundles)} bundles under smt3hd_Data/StreamingAssets/PC/.";
                if (bad is not null)
                {
                    return Fail($"Nothing was imported from {zipPath}: {bad}");
                }
            }

            if (zip.GetEntry(ClassicPack.ManifestName) is null)
            {
                return Fail($"Nothing was imported: {zipPath} has no {ClassicPack.ManifestName} at its top. Choose the zip that Export My Classic Pack made.");
            }

            return Install(dataDir, open, temp =>
            {
                long total = 0;
                foreach (var entry in zip.Entries.Where(e => allowed.Contains(e.FullName) && e.FullName != ReadMe))
                {
                    var target = Path.Combine(temp, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var from = entry.Open();
                    using var to = File.Create(target);

                    // The sizes a zip declares can lie, so the copy itself stops past the caps.
                    var cap = Math.Min(ClassicPack.MaxBundleBytes, ClassicPack.MaxPackBytes - total);
                    var copied = CopyAtMost(from, to, cap);
                    if (copied > cap)
                    {
                        return (null, $"Nothing was imported: \"{entry.FullName}\" unpacks to more than a classic pack holds ({ClassicPack.MaxBundleBytes / 1024 / 1024} MiB a bundle, {ClassicPack.MaxPackBytes / 1024 / 1024} MiB in all).");
                    }

                    total += copied;
                }

                var manifest = Path.Combine(temp, ClassicPack.ManifestName);
                if (ClassicPack.Open(temp, checkHashes: true, out var problem) is not { } pack)
                {
                    return (null, $"Nothing was imported from {zipPath}. {problem}");
                }

                if (pack.Files.FirstOrDefault(f => CheckHeader(Path.Combine(temp, f.Path)) is not null) is { } broken)
                {
                    return (null, $"Nothing was imported: {broken.Path} in {zipPath} isn't a readable bundle ({CheckHeader(Path.Combine(temp, broken.Path))}).");
                }

                // The manifest goes last: it's moved aside and written back once the bundles have been read.
                var text = File.ReadAllText(manifest);
                File.Delete(manifest);
                return (text, null);
            }, $"from {zipPath}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail($"Nothing was imported: {zipPath} isn't a zip, or it's damaged ({e.Message.TrimEnd('.')}).");
        }
    }

    /// <summary>
    /// Writes the player's classic pack, checked against its manifest first, to <c>&lt;data dir&gt;/exports</c> as one zip
    /// with a note that it's theirs alone. Returns the zip's path, or null, with a message either way.
    /// </summary>
    public static string? Export(string dataDir, out string message)
    {
        var folder = ClassicStages.PackFolder(dataDir);
        if (!ClassicPack.IsIn(folder))
        {
            message = "There's no classic pack to export yet. Import one from your Nocturne install first.";
            return null;
        }

        if (ClassicPack.Open(folder, checkHashes: true, out var problem) is not { } pack)
        {
            message = $"Your classic pack wasn't exported. {problem}";
            return null;
        }

        var target = Path.Combine(PackArchive.ExportsFolder(dataDir), ExportName);
        var temp = target + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var zip = new ZipArchive(File.Create(temp), ZipArchiveMode.Create))
            {
                using (var note = new StreamWriter(zip.CreateEntry(ReadMe).Open()))
                {
                    note.Write($"Puzzle Boy Community Edition classic pack.\n\n{ClassicPack.Note}\n\nTo use it on another computer, choose Settings, Classic Nocturne, Import from a Folder or Zip, and type this zip's path, or run the game with --import-classic <this zip>.\n");
                }

                // The bundles are compressed already; storing them keeps the export quick.
                zip.CreateEntryFromFile(Path.Combine(folder, ClassicPack.ManifestName), ClassicPack.ManifestName, CompressionLevel.Optimal);
                foreach (var file in pack.Files)
                {
                    zip.CreateEntryFromFile(Path.Combine(folder, file.Path), file.Path, CompressionLevel.NoCompression);
                }
            }

            File.Move(temp, target, overwrite: true);
            message = $"Exported your classic pack to {target}. {ClassicPack.Note}";
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // A stray .tmp in the exports folder is harmless; the next export replaces it.
            }

            message = $"Your classic pack wasn't exported: {e.Message}";
            return null;
        }
    }

    /// <summary>
    /// Builds a new pack in a scratch folder with <paramref name="fill"/> (which copies the bundles and returns the
    /// manifest's text, or a problem), reads it as the game will, writes the manifest last and swaps it in with renames.
    /// On any failure the scratch folder goes, and the data folder too when this created it.
    /// </summary>
    private static ClassicImportResult Install(string dataDir, Func<string, IClassicAssetSource>? open, Func<string, (string? Manifest, string? Problem)> fill, string from)
    {
        var folder = ClassicStages.Folder(dataDir);
        var temp = folder + ".tmp";
        var old = folder + ".old";
        var created = !Directory.Exists(dataDir);
        try
        {
            ClassicSource.DeleteScratch(temp);
            Directory.CreateDirectory(temp);
            var (manifest, problem) = fill(temp);
            if (manifest is null)
            {
                return Fail(Cleanup(problem!));
            }

            var problems = new List<string>();
            List<Content.StagePack> campaigns;
            ClassicAssets assets;
            try
            {
                // A schema 1 pack (from a folder or zip) has no interface bundles; anything else must decode them too.
                var ui = ClassicPack.UiBundles.All(b => File.Exists(Path.Combine(temp, ClassicPack.PathOf(b))));
                var source = (open ?? (f => new ClassicPackSource(f, ui)))(temp);
                campaigns = ClassicStages.FromTextAssets(source.ReadTextAssets(), problems);
                assets = source.ReadAssets();
                if (assets.UiProblem is { } broken)
                {
                    return Fail(Cleanup($"The interface bundles {from} can't be read ({broken}), so nothing was imported. {Verify}"));
                }
            }
            catch (Exception e)
            {
                // The bundle reader throws all sorts for a damaged bundle; any of them means the same to the player.
                return Fail(Cleanup($"The bundles {from} can't be read ({e.GetType().Name}), so nothing was imported. {Verify}"));
            }

            if (campaigns.Count == 0)
            {
                return Fail(Cleanup($"The pazzleboy bundle {from} holds none of the Puzzle Boy stages, so nothing was imported. {Verify}"));
            }

            File.WriteAllText(Path.Combine(temp, ClassicPack.ManifestName), manifest);
            if (ClassicPack.Open(temp, checkHashes: false, out var written) is not { } pack)
            {
                return Fail(Cleanup($"Nothing was imported. {written}"));
            }

            ClassicSource.DeleteScratch(old);
            if (Directory.Exists(folder))
            {
                Directory.Move(folder, old);
            }

            Directory.Move(temp, folder);
            ClassicSource.DeleteScratch(old);
            ClassicAssetCache.Remember(pack, assets);

            int Count(string id) => campaigns.FirstOrDefault(c => c.PackId == id)?.Stages.Count ?? 0;
            var (original, bonus) = (Count(ClassicStages.OriginalPackId), Count(ClassicStages.BonusPackId));
            var licensed = pack.Origin == ClassicPack.Licensed ? $" Licensed by {pack.Licensor} under {pack.License}." : "";
            var message = $"Imported your classic pack {from}: the Classic theme and {original + bonus} classic stages, {original} of {ClassicStages.Original.Count} in {ClassicStages.OriginalTitle} and {bonus} of {ClassicStages.Bonus.Count} in {ClassicStages.BonusTitle}.{licensed}";
            return new ClassicImportResult(problems.Count == 0 ? message : $"{message} Left out: {string.Join(" ", problems)}", original, bonus);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(folder) && Directory.Exists(old))
            {
                // The swap failed halfway: the earlier pack goes back.
                Directory.Move(old, folder);
            }

            return Fail(Cleanup($"The classic pack couldn't be saved in {folder}: {e.Message}"));
        }

        string Cleanup(string message)
        {
            ClassicSource.DeleteScratch(temp);
            if (created && Directory.Exists(dataDir) && !Directory.EnumerateFileSystemEntries(dataDir).Any())
            {
                Directory.Delete(dataDir);
            }

            return message;
        }
    }

    /// <summary>
    /// Copies the bundle at <paramref name="from"/> into the pack in <paramref name="temp"/> after checking its header,
    /// hashing it on the way, and against <paramref name="expected"/> when given. Returns null, or what's wrong with it.
    /// </summary>
    private static string? Copy(string from, string temp, string bundle, ClassicPackFile? expected, out ClassicPackFile? file)
    {
        file = null;
        var path = ClassicPack.PathOf(bundle);
        try
        {
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read);
            ClassicSource.CheckHeader(source);
            if (source.Length > ClassicPack.MaxBundleBytes)
            {
                return $"it's {source.Length:N0} bytes, more than a classic pack's bundle";
            }

            var target = Path.Combine(temp, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var to = File.Create(target))
            {
                source.CopyTo(to);
            }

            source.Position = 0;
            file = new ClassicPackFile(path, source.Length, Convert.ToHexStringLower(SHA256.HashData(source)));
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            return e is InvalidDataException ? e.Message : "it isn't a UnityFS bundle";
        }

        return expected is null ? null
            : file.Size != expected.Size ? $"is {file.Size:N0} bytes, but {ClassicPack.ManifestName} says {expected.Size:N0}: the file is damaged or was changed"
            : file.Sha256 != expected.Sha256 ? $"doesn't match its SHA-256 in {ClassicPack.ManifestName}: the file is damaged or was changed"
            : null;
    }

    /// <summary>Why the file at <paramref name="path"/> isn't a plausible bundle, or null.</summary>
    private static string? CheckHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            ClassicSource.CheckHeader(stream);
            return null;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            return e is InvalidDataException ? e.Message : "it isn't a UnityFS bundle";
        }
    }

    /// <summary>Copies at most <paramref name="cap"/> + 1 bytes, so a result over the cap means there was more.</summary>
    private static long CopyAtMost(Stream from, Stream to, long cap)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while (copied <= cap && (read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, cap + 1 - copied))) > 0)
        {
            to.Write(buffer, 0, read);
            copied += read;
        }

        return copied;
    }

    private static ClassicImportResult Fail(string message) => new(message, 0, 0);
}
