using System.Text;
using System.Text.Json;

namespace PuzzleBoy.Community.Content;

/// <summary>
/// Safe reads of content and pack files. Every path a file names is relative, uses <c>/</c> and stays inside its folder,
/// with no links on the way, so a pack can't read anything its folder doesn't hold. Problems are added to a list as
/// sentences; nothing here throws for bad input.
/// </summary>
internal static class ContentFile
{
    /// <summary>The full path of <paramref name="relative"/> inside <paramref name="root"/>, or null after adding a problem.</summary>
    /// <param name="root">The folder the path must stay inside.</param>
    /// <param name="relative">A path from a content file, such as <c>stages/first-push.json</c>.</param>
    /// <param name="label">Where the path came from, to start each problem with.</param>
    /// <param name="problems">Receives a sentence for each thing wrong.</param>
    public static string? Resolve(string root, string? relative, string label, List<string> problems)
    {
        if (string.IsNullOrEmpty(relative))
        {
            problems.Add($"{label}: the path is missing.");
            return null;
        }

        // A colon is a drive letter or an NTFS stream; a backslash would only work on Windows.
        if (relative.Contains('\\') || relative.Contains(':') || Path.IsPathRooted(relative) || relative.StartsWith('/'))
        {
            problems.Add($"{label}: \"{relative}\" must be a relative path with / between folders.");
            return null;
        }

        var parts = relative.Split('/');
        if (parts.Any(p => p is "" or "." or ".."))
        {
            problems.Add($"{label}: \"{relative}\" must stay inside its folder, without empty, . or .. parts.");
            return null;
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            problems.Add($"{label}: \"{relative}\" leads outside its folder.");
            return null;
        }

        // A link inside the folder could still point anywhere, so none is followed.
        var path = fullRoot;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
            if (IsLink(path))
            {
                problems.Add($"{label}: \"{relative}\" goes through a link, which content can't use.");
                return null;
            }
        }

        return full;
    }

    /// <summary>True when <paramref name="path"/> is a symbolic link, junction or other reparse point.</summary>
    public static bool IsLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.Exists && (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    /// <summary>The bytes of the file at <paramref name="full"/>, or null after adding a problem.</summary>
    /// <param name="full">A path from <see cref="Resolve"/>.</param>
    /// <param name="maxBytes">The largest file accepted.</param>
    /// <param name="label">What the file is, to start each problem with.</param>
    /// <param name="problems">Receives a sentence for each thing wrong.</param>
    public static byte[]? Read(string full, long maxBytes, string label, List<string> problems)
    {
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                problems.Add($"{label}: the file is missing.");
                return null;
            }

            if (info.Length > maxBytes)
            {
                problems.Add($"{label}: the file is over {maxBytes / 1024} KiB.");
                return null;
            }

            return File.ReadAllBytes(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{label}: the file can't be read ({e.Message}).");
            return null;
        }
    }

    /// <summary>The root of the JSON file at <paramref name="full"/>, or null after adding a problem.</summary>
    /// <remarks>Strict JSON, as the engine reads stages: no comments, no trailing commas, no duplicate properties.</remarks>
    public static JsonElement? ReadJson(string full, long maxBytes, string label, List<string> problems)
    {
        if (Read(full, maxBytes, label, problems) is not { } bytes)
        {
            return null;
        }

        try
        {
            // Decoding drops a byte order mark's meaning and replaces invalid UTF-8, which JsonDocument reports less clearly.
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            problems.Add($"{label}: the file isn't valid JSON.");
            return null;
        }
    }

    /// <summary>
    /// The string property <paramref name="name"/> of <paramref name="element"/>, or null when it's missing, not a string,
    /// empty or not valid Unicode.
    /// </summary>
    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? Text(value) : null;

    /// <summary>The text of <paramref name="value"/>, or null when it isn't a string, is empty or isn't valid Unicode.</summary>
    public static string? Text(JsonElement value)
    {
        try
        {
            return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
        }
        catch (InvalidOperationException)
        {
            // JsonDocument accepts an escaped lone surrogate; reading it throws.
            return null;
        }
    }

    /// <summary>
    /// Adds a problem unless <paramref name="root"/> is an object with the expected <c>format</c> and <c>schemaVersion</c> 1.
    /// Returns whether it is.
    /// </summary>
    public static bool CheckHeader(JsonElement root, string format, string label, List<string> problems)
    {
        if (String(root, "format") != format)
        {
            problems.Add($"{label}: this isn't a {format} file (its \"format\" should be \"{format}\").");
            return false;
        }

        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number < 1)
        {
            problems.Add($"{label}: \"schemaVersion\" should be 1.");
            return false;
        }

        if (number > 1)
        {
            problems.Add($"{label}: this file was made by a newer version of the game (schema version {number}).");
            return false;
        }

        return true;
    }
}
