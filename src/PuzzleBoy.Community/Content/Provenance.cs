using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PuzzleBoy.Community.Content;

/// <summary>
/// Checks <c>content/provenance.json</c> against the files beside it: every file is an allowed type and is covered by an
/// entry, every entry is complete and covers a file, and a file from any creator but <see cref="OwnCreator"/> has its own
/// entry and matches its recorded SHA-256.
/// </summary>
internal static partial class Provenance
{
    /// <summary>The file types content may hold. Anything else, such as code or a game's asset bundle, is refused.</summary>
    public static readonly string[] AllowedExtensions = [".json", ".md", ".txt", ".ttf", ".png", ".wav", ".ogg"];

    /// <summary>The properties every entry needs.</summary>
    public static readonly string[] RequiredFields = ["path", "creator", "source", "category", "license", "modified", "redistribution"];

    /// <summary>The creator of this repository's own content; every other creator's file needs a recorded SHA-256.</summary>
    public const string OwnCreator = "Puzzle Boy Community authors";

    private const int MaxBytes = 256 * 1024;

    /// <summary>The game format whose first bytes the file at <paramref name="path"/> has, as check-content.sh knows them, or null.</summary>
    private static string? GameFormat(string path)
    {
        Span<byte> head = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        head = head[..stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false)];
        return head.StartsWith("UnityFS"u8) ? "a Unity asset bundle"
            : head.StartsWith("FSB5"u8) ? "an FMOD sound bank"
            : head.StartsWith(new byte[] { 0xaf, 0x1b, 0xb1, 0xfa }) ? "IL2CPP metadata"
            : head.Length == 12 && head[8..].SequenceEqual("FLW0"u8) ? "an Atlus flow script (FLW0)"
            : null;
    }

    /// <summary>Every problem with the content in <paramref name="contentFolder"/>; empty when it's fit to ship. Nothing throws.</summary>
    public static List<string> Check(string contentFolder)
    {
        const string Where = "provenance.json";
        var problems = new List<string>();
        var files = new List<string>();
        try
        {
            foreach (var entry in new DirectoryInfo(contentFolder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(contentFolder, entry.FullName).Replace('\\', '/');
                if (ContentFile.IsLink(entry.FullName))
                {
                    problems.Add($"{relative}: content can't hold links.");
                }
                else if (entry is FileInfo)
                {
                    files.Add(relative);
                    if (!AllowedExtensions.Contains(Path.GetExtension(relative).ToLowerInvariant()))
                    {
                        problems.Add($"{relative}: content may only hold {string.Join(", ", AllowedExtensions)} files.");
                    }

                    // A classic pack is the player's own, made from their copy of Nocturne; no name or extension disguises one.
                    if (Path.GetFileName(relative).Equals("classic-pack.json", StringComparison.OrdinalIgnoreCase) || relative.Contains("smt3hd_Data/", StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add($"{relative}: part of a classic pack, which is made on the player's computer from their own copy of Nocturne and never ships.");
                    }
                    else if (GameFormat(entry.FullName) is { } format)
                    {
                        problems.Add($"{relative}: {format}, which content never holds.");
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"content can't be listed ({e.Message}).");
            return problems;
        }

        if (ContentFile.Resolve(contentFolder, Where, Where, problems) is not { } file
            || ContentFile.ReadJson(file, MaxBytes, Where, problems) is not { } root)
        {
            return problems;
        }

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{Where}: \"entries\" should be a list.");
            return problems;
        }

        var covered = new HashSet<string>();
        var number = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            number++;
            var path = ContentFile.String(entry, "path");
            var where = $"{Where}, entry {number}" + (path is null ? "" : $" ({path})");
            var missing = RequiredFields.Where(f => f == "modified"
                ? entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(f, out var m) || m.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                : ContentFile.String(entry, f) is null).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{where}: missing {string.Join(", ", missing)}.");
            }

            if (ContentFile.String(entry, "redistribution") is { } redistribution && redistribution != "allowed")
            {
                problems.Add($"{where}: only content whose redistribution is \"allowed\" can ship.");
            }

            if (path is null)
            {
                continue;
            }

            var pattern = Glob(path);
            var matches = files.Where(f => pattern.IsMatch(f)).ToList();
            if (matches.Count == 0)
            {
                problems.Add($"{where}: no file matches it.");
            }

            covered.UnionWith(matches);

            // Third-party files are pinned to the exact bytes whose license was checked, wherever they came from.
            var hash = ContentFile.String(entry, "sha256");
            if (ContentFile.String(entry, "creator") is { } creator && creator != OwnCreator && (hash is null || path.Contains('*')))
            {
                problems.Add($"{where}: a file from elsewhere needs its own entry with a \"sha256\".");
            }
            else if (hash is not null && matches.Count == 1
                && ContentFile.Read(Path.Combine(contentFolder, matches[0]), 16 * 1024 * 1024, where, problems) is { } bytes)
            {
                var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (actual != hash)
                {
                    problems.Add($"{where}: the file's SHA-256 is {actual}, not the recorded {hash}.");
                }
            }
        }

        problems.AddRange(files.Except(covered).Select(f => $"{f}: no entry in {Where} covers this file."));
        return problems;
    }

    /// <summary>A path pattern: <c>*</c> matches within a folder, <c>**</c> across folders.</summary>
    private static Regex Glob(string pattern)
    {
        var parts = DoubleStar().Split(pattern).Select(p => Regex.Escape(p).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]"));
        return new Regex($"^{string.Join(".*", parts)}\\z", RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\*\*")]
    private static partial Regex DoubleStar();
}
