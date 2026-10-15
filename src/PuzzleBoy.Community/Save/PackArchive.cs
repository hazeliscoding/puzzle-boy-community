using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Save;

/// <summary>What to do when an imported pack's id is already taken by an imported pack.</summary>
internal enum ImportChoice
{
    /// <summary>Import nothing and report the conflict, so the player can choose.</summary>
    Ask,

    /// <summary>Replace the imported pack of that id; progress on stages with the same ids stays.</summary>
    Replace,

    /// <summary>Keep both: the new pack gets the id with <c>-2</c>, <c>-3</c> and so on.</summary>
    KeepBoth,
}

/// <summary>The outcome of <see cref="PackArchive.Import"/>.</summary>
/// <param name="Message">A sentence for the player: what was imported, or why nothing was.</param>
/// <param name="PackId">The imported pack's id, or null when nothing was imported.</param>
/// <param name="Conflict">True when nothing was imported only because the pack's id is taken; ask, then import again with a choice.</param>
/// <param name="Warnings">Files left out of the pack, such as a readme, one sentence each.</param>
internal sealed record ImportResult(string Message, string? PackId, bool Conflict, IReadOnlyList<string> Warnings);

/// <summary>
/// Portable level packs: a <c>.zip</c> holding a <c>pack.json</c> (the built-in packs' format) and its stages under
/// <c>stages/</c>. Export writes the player's own stages as one to <c>&lt;data dir&gt;/exports</c>. Import checks every
/// entry's name, kind and size, then every stage, in a scratch folder outside the data folder, and only then moves the
/// pack into <c>&lt;data dir&gt;/packs/&lt;packId&gt;</c> in one rename. Nothing in a pack is ever run, and nothing
/// here throws for a bad archive: every refusal is a sentence.
/// </summary>
internal static partial class PackArchive
{
    /// <summary>The largest archive accepted.</summary>
    public const long MaxZipBytes = 2 * 1024 * 1024;

    /// <summary>The most entries an archive may hold, folders included.</summary>
    public const int MaxEntries = 256;

    /// <summary>The largest file an entry may unpack to, the same as a stage or a <c>pack.json</c>.</summary>
    public const int MaxEntryBytes = 64 * 1024;

    /// <summary>The most all entries together may unpack to.</summary>
    public const long MaxTotalBytes = 4 * 1024 * 1024;

    /// <summary>What import says of a stage whose fingerprint is a known Nocturne layout.</summary>
    public const string KnownLayoutMessage = "This layout matches a stage from the original game, so it can't be imported as a shared pack.";

    /// <summary>The licenses export offers, as SPDX identifiers; the first is the default.</summary>
    public static readonly string[] Licenses = ["CC0-1.0", "CC-BY-4.0", "CC-BY-SA-4.0", "Apache-2.0"];

    /// <summary>The known-layout list beside the game, which the build copies there from <c>scripts/</c>.</summary>
    public static string KnownListPath => Path.Combine(AppContext.BaseDirectory, "known-atlus-layouts.txt");

    /// <summary>Where exported packs are written.</summary>
    public static string ExportsFolder(string dataDir) => Path.Combine(dataDir, "exports");

    /// <summary>Where the player puts packs to import; stage select lists the <c>.zip</c> files in it.</summary>
    public static string ImportsFolder(string dataDir) => Path.Combine(dataDir, "imports");

    /// <summary>Where imported packs live, one folder each, named by pack id.</summary>
    public static string PacksFolder(string dataDir) => Path.Combine(dataDir, "packs");

    /// <summary>The pack ids an import can't take: the built-in packs', My Stages' and the editor's playtest.</summary>
    public static HashSet<string> Reserved(IEnumerable<StagePack> builtIn) => [.. builtIn.Select(p => p.PackId), CustomStages.PackId, "playtest"];

    /// <summary>The known-layout fingerprints beside the game, or null when the list can't be read.</summary>
    public static HashSet<string>? ReadKnown()
    {
        try
        {
            return KnownLayouts.Read(KnownListPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="stages"/> as a pack to <c>exports/&lt;packId&gt;.zip</c>, the pack id made from
    /// <paramref name="title"/> as stage names are. An export of the same title replaces the last one, through a temp
    /// file, so a failed write never leaves half an archive. Returns the archive's full path, or null with
    /// <paramref name="message"/> saying why; on success <paramref name="message"/> names the path.
    /// </summary>
    /// <param name="dataDir">The data folder.</param>
    /// <param name="stages">The stages, in play order; each must have a <see cref="CustomStage.Stage"/>.</param>
    /// <param name="title">The pack's title.</param>
    /// <param name="creator">Who made it.</param>
    /// <param name="license">One of <see cref="Licenses"/>.</param>
    /// <param name="known">The known-layout fingerprints, or null when they couldn't be read, which refuses the export.</param>
    /// <param name="reserved">Pack ids an import can't take (see <see cref="Reserved"/>); a title that would get one is refused.</param>
    /// <param name="message">What happened, for the player.</param>
    public static string? Export(string dataDir, IReadOnlyList<CustomStage> stages, string title, string creator, string license, IReadOnlySet<string>? known, IReadOnlyCollection<string> reserved, out string message)
    {
        (title, creator) = (title.Trim(), creator.Trim());
        var packId = CustomStages.SafeName(title);
        message = stages.Count is 0 or > StagePack.MaxStages ? $"Choose 1 to {StagePack.MaxStages} stages to export."
            : title.Length == 0 ? "The pack needs a title."
            : reserved.Contains(packId) ? $"“{title}” would get the id \"{packId}\", which the game's own stages use. Choose another title."
            : creator.Length == 0 ? "The pack needs a creator."
            : !Licenses.Contains(license) ? $"The license should be one of {string.Join(", ", Licenses)}."
            : known is null ? "The list of the original game's layouts is missing beside the game, so stages can't be checked for sharing."
            : stages.FirstOrDefault(s => s.Stage is null) is { } broken ? $"“{broken.Title}” can't be read as a stage: {broken.Problem}"
            : stages.FirstOrDefault(s => KnownLayouts.Matches(known, s.Stage!).Any()) is { } copy ? $"“{copy.Title}” matches a stage from the original game, so it can't be shared in a pack."
            : "";
        if (message.Length > 0)
        {
            return null;
        }

        var pack = new JsonObject
        {
            ["format"] = "puzzle-boy-pack",
            ["schemaVersion"] = 1,
            ["packId"] = packId,
            ["title"] = title,
            ["creator"] = creator,
            ["license"] = license,
            ["stages"] = new JsonArray([.. stages.Select(s => new JsonObject { ["id"] = s.Name, ["title"] = s.Title, ["file"] = $"stages/{s.Name}.json" })]),
        };

        var path = Path.GetFullPath(Path.Combine(ExportsFolder(dataDir), $"{packId}.zip"));
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(ExportsFolder(dataDir));
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Add(zip, "pack.json", pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                foreach (var stage in stages)
                {
                    Add(zip, $"stages/{stage.Name}.json", new StageDocument(stage.Stage!, stage.Title).ToJson());
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            TryDelete(temp);
            message = $"The pack couldn't be exported: {e.Message}";
            return null;
        }

        message = $"Exported {stages.Count} {(stages.Count == 1 ? "stage" : "stages")} to {path}";
        return path;

        static void Add(ZipArchive zip, string name, string text)
        {
            using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            entry.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
        }
    }

    /// <summary>
    /// Imports the pack in the archive at <paramref name="zipPath"/> into <c>packs/&lt;packId&gt;</c> under
    /// <paramref name="dataDir"/>. The data folder is touched only once everything has passed.
    /// </summary>
    /// <param name="zipPath">The archive.</param>
    /// <param name="dataDir">The data folder.</param>
    /// <param name="known">The known-layout fingerprints, or null when they couldn't be read, which refuses the import.</param>
    /// <param name="reserved">Pack ids an import can't take: the built-in packs' and the game's own.</param>
    /// <param name="choice">What to do when an imported pack already has the id.</param>
    public static ImportResult Import(string zipPath, string dataDir, IReadOnlySet<string>? known, IReadOnlyCollection<string> reserved, ImportChoice choice = ImportChoice.Ask)
    {
        var warnings = new List<string>();
        var name = Path.GetFileName(zipPath);
        if (known is null)
        {
            return Refuse("The list of the original game's layouts is missing beside the game, so packs can't be checked.");
        }

        if (ReadEntries(zipPath, warnings, out var files) is { } problem)
        {
            return Refuse($"{name}: {problem}");
        }

        if (CheckPack(files, known, out var pack) is { } bad)
        {
            return Refuse($"{name}: {bad}");
        }

        var (packId, title, creator, count) = (pack!.PackId, pack.Title, pack.Creator, pack.Stages.Count);
        if (reserved.Contains(packId))
        {
            return Refuse($"{name}: its id \"{packId}\" belongs to the game's own stages, so it can't be imported. Its maker can export it under another title.");
        }

        var packs = PacksFolder(dataDir);
        var target = Path.Combine(packs, packId);
        var replace = Path.Exists(target);
        if (replace && choice == ImportChoice.Ask)
        {
            return new ImportResult($"A pack with the id \"{packId}\" is already imported. Replace it, or keep both?", null, true, warnings);
        }

        if (replace && choice == ImportChoice.KeepBoth)
        {
            for (var n = 2; replace; n++)
            {
                var suffix = $"-{n}";
                packId = $"{pack.PackId[..Math.Min(pack.PackId.Length, CustomStages.MaxName - suffix.Length)]}{suffix}";
                target = Path.Combine(packs, packId);
                replace = Path.Exists(target) || reserved.Contains(packId);
            }

            var node = JsonNode.Parse(PackText(files))!;
            node["packId"] = packId;
            files["pack.json"] = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        var incoming = Path.Combine(packs, $".incoming-{Guid.NewGuid():N}");
        var old = Path.Combine(packs, $".replaced-{Guid.NewGuid():N}");
        try
        {
            Write(incoming, files);
            if (replace)
            {
                Directory.Move(target, old);
            }

            Directory.Move(incoming, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (replace && Directory.Exists(old) && !Path.Exists(target))
            {
                Directory.Move(old, target);
            }

            TryDeleteFolder(incoming);
            return Refuse($"The pack couldn't be saved: {e.Message}");
        }

        TryDeleteFolder(old);
        return new ImportResult($"Imported “{title}” by {creator}: {count} {(count == 1 ? "stage" : "stages")}, as packs/{packId}.", packId, false, warnings);

        ImportResult Refuse(string message) => new(message, null, false, warnings);
    }

    /// <summary>
    /// The imported packs in <c>packs/</c>, by folder name. A folder whose <c>pack.json</c> names another id, or an id in
    /// <paramref name="reserved"/>, is left out with a problem, as are links and the hidden work folders import uses.
    /// </summary>
    public static List<StagePack> LoadImported(string dataDir, IReadOnlyCollection<string> reserved, List<string> problems)
    {
        var packs = new List<StagePack>();
        try
        {
            var folder = PacksFolder(dataDir);
            if (!Directory.Exists(folder))
            {
                return packs;
            }

            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var dir in Directory.EnumerateDirectories(folder, "*", options).Order(StringComparer.Ordinal))
            {
                var id = Path.GetFileName(dir);
                if (!IdPattern().IsMatch(id))
                {
                    continue;
                }

                if (StagePack.Load(dir, $"packs/{id}", problems) is not { } pack)
                {
                    continue;
                }

                if (pack.PackId != id || reserved.Contains(id))
                {
                    problems.Add($"packs/{id}: its pack.json says \"{pack.PackId}\", which isn't its folder's name or is the game's own.");
                    continue;
                }

                packs.Add(pack);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"packs: the folder can't be read ({e.Message}).");
        }

        return packs;
    }

    /// <summary>The <c>.zip</c> files in <c>imports/</c>, by name; empty when there's no such folder.</summary>
    public static List<string> ListImports(string dataDir)
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Directory };
            return Directory.Exists(ImportsFolder(dataDir))
                ? [.. Directory.EnumerateFiles(ImportsFolder(dataDir), "*.zip", options).Order(StringComparer.OrdinalIgnoreCase)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads every entry of the archive into <paramref name="files"/>, by name, after checking its name, kind and size.
    /// Returns the first problem, or null. Readmes and text files are left out with a warning.
    /// </summary>
    private static string? ReadEntries(string zipPath, List<string> warnings, out Dictionary<string, byte[]> files)
    {
        files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            var info = new FileInfo(zipPath);
            if (!info.Exists)
            {
                return "the file is missing.";
            }

            if (info.Length > MaxZipBytes)
            {
                return $"the archive is over {MaxZipBytes / 1024 / 1024} MiB.";
            }

            using var stream = info.OpenRead();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries)
            {
                return $"the archive holds {zip.Entries.Count} entries; a pack may hold {MaxEntries}.";
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (CheckName(name) is { } bad)
                {
                    return $"\"{name}\" {bad}";
                }

                // The Unix file type sits in the top 16 bits; 0xA000 is a symbolic link. 0x400 is a Windows reparse point.
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                {
                    return $"\"{name}\" is a link, which a pack can't hold.";
                }

                if (!seen.Add(name))
                {
                    return $"\"{name}\" is in the archive twice (names are compared ignoring case).";
                }

                if (name.EndsWith('/'))
                {
                    if (name != "stages/")
                    {
                        return $"\"{name}\" is a folder; a pack holds only pack.json and stages/*.json.";
                    }

                    continue;
                }

                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is ".md" or ".txt")
                {
                    warnings.Add($"\"{name}\" was left out: a pack holds only pack.json and stages/*.json.");
                    continue;
                }

                if (extension is ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".jar")
                {
                    return $"\"{name}\" is another archive, which a pack can't hold.";
                }

                if (name != "pack.json" && !StageEntry().IsMatch(name))
                {
                    return $"\"{name}\" isn't part of a pack, which holds only pack.json and stages/*.json.";
                }

                // The sizes in the archive can lie, so the read itself stops past the cap.
                if (entry.Length > MaxEntryBytes || Read(entry) is not { } bytes)
                {
                    return $"\"{name}\" unpacks to more than {MaxEntryBytes / 1024} KiB.";
                }

                if ((total += bytes.Length) > MaxTotalBytes)
                {
                    return $"the archive unpacks to more than {MaxTotalBytes / 1024 / 1024} MiB.";
                }

                if (bytes is [(byte)'P', (byte)'K', 3, 4, ..])
                {
                    return $"\"{name}\" is another archive, which a pack can't hold.";
                }

                files[name] = bytes;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"it isn't a zip archive, or it's damaged ({e.Message.TrimEnd('.')}).";
        }

        return files.ContainsKey("pack.json") ? null : "the archive has no pack.json at its top.";

        static byte[]? Read(ZipArchiveEntry entry)
        {
            using var source = entry.Open();
            var buffer = new byte[MaxEntryBytes + 1];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = source.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
            }

            return length > MaxEntryBytes ? null : buffer[..length];
        }
    }

    /// <summary>Why an entry name can't be in a pack, or null when it's a plain relative path.</summary>
    private static string? CheckName(string name) =>
        name.Length == 0 ? "has no name."
        : name.StartsWith("//", StringComparison.Ordinal) || name.StartsWith(@"\\", StringComparison.Ordinal) ? "is a network (UNC) path."
        : name.Contains('\\') ? "uses backslashes; a pack's paths use /."
        : name.Length >= 2 && name[1] == ':' ? "names a drive."
        : name.Contains(':') ? "has a colon, which a pack's paths can't."
        : name.StartsWith('/') ? "is an absolute path."
        : name.TrimEnd('/').Split('/').Any(p => p is "" or "." or "..") ? "leads outside the pack (an empty, . or .. part)."
        : name.Any(char.IsControl) ? "has a control character."
        : null;

    /// <summary>
    /// Checks <c>pack.json</c> and every stage, in a scratch folder outside the data folder, and fingerprints each stage.
    /// Returns the first problem, or null with the loaded <paramref name="pack"/>.
    /// </summary>
    private static string? CheckPack(Dictionary<string, byte[]> files, IReadOnlySet<string> known, out StagePack? pack)
    {
        pack = null;
        var problems = new List<string>();
        string? scratch = null;
        try
        {
            using (var document = JsonDocument.Parse(PackText(files), new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false }))
            {
                var root = document.RootElement;
                if (!ContentFile.CheckHeader(root, "puzzle-boy-pack", "pack.json", problems))
                {
                    return problems[0];
                }

                if (root.TryGetProperty("stages", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    var number = 0;
                    foreach (var entry in list.EnumerateArray())
                    {
                        number++;
                        if (ContentFile.String(entry, "file") is not { } file || !StageEntry().IsMatch(file))
                        {
                            return $"pack.json, stage {number}: its \"file\" must be a .json file directly in stages/.";
                        }
                    }
                }
            }

            scratch = Directory.CreateTempSubdirectory("puzzle-boy-pack-").FullName;
            Write(scratch, files);
            var loaded = StagePack.Load(scratch, "pack", problems);
            if (problems.Count > 0 || loaded is null)
            {
                return problems.Count > 0 ? problems[0] : "pack.json can't be read.";
            }

            if (loaded.Stages.FirstOrDefault(s => KnownLayouts.Matches(known, s.Stage).Any()) is { } copy)
            {
                return $"“{copy.Title}”: {KnownLayoutMessage}";
            }

            pack = loaded;
            return null;
        }
        catch (JsonException)
        {
            return "pack.json isn't valid JSON.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the pack couldn't be checked: {e.Message}";
        }
        finally
        {
            if (scratch is not null)
            {
                TryDeleteFolder(scratch);
            }
        }
    }

    /// <summary><c>pack.json</c> as text, without the byte order mark an editor such as Notepad may write.</summary>
    private static string PackText(Dictionary<string, byte[]> files) => Encoding.UTF8.GetString(files["pack.json"]).TrimStart('﻿');

    /// <summary>Writes <paramref name="files"/>, whose names <see cref="ReadEntries"/> has checked, under a new <paramref name="folder"/>.</summary>
    private static void Write(string folder, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in files)
        {
            if (ContentFile.Resolve(folder, name, name, []) is not { } path)
            {
                throw new IOException($"\"{name}\" leads outside the pack.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is replaced by the next export.
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A hidden leftover is skipped by LoadImported and harmless.
        }
    }

    // \z, not $, which would also accept a trailing newline.
    [GeneratedRegex(@"^stages/[^/]+\.json\z")]
    private static partial Regex StageEntry();

    [GeneratedRegex(@"^[a-z0-9-]{1,48}\z")]
    private static partial Regex IdPattern();
}
