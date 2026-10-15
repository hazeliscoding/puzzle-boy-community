using System.Text.Json;
using System.Text.Json.Nodes;

namespace PuzzleBoy.Community.Save;

/// <summary>
/// Reads and writes <see cref="SaveData"/> as <c>save.json</c> in one folder. A write goes to <c>save.json.tmp</c> first
/// and then replaces <c>save.json</c>, keeping the old one as <c>save.json.bak</c>, so a crash mid-write leaves a
/// whole file behind. A damaged file falls back to the backup, then to defaults, with a <see cref="Notice"/>; nothing
/// here throws for a bad or missing file.
/// </summary>
internal sealed class SaveStore
{
    /// <summary>The save file's name.</summary>
    public const string FileName = "save.json";

    // A save is a few kilobytes; anything far bigger isn't one.
    private const int MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // True when save.json didn't parse, so the next write mustn't make it the backup.
    private bool _mainDamaged;

    private SaveStore(string folder)
    {
        Folder = folder;
    }

    /// <summary>The folder holding the save.</summary>
    public string Folder { get; }

    /// <summary>The save file's path.</summary>
    public string MainPath => System.IO.Path.Combine(Folder, FileName);

    /// <summary>The backup's path: the save before the last write.</summary>
    public string BackupPath => MainPath + ".bak";

    /// <summary>The path a write goes to before it replaces the save.</summary>
    public string TempPath => MainPath + ".tmp";

    /// <summary>The settings and progress in use.</summary>
    public SaveData Data { get; private set; } = new();

    /// <summary>
    /// Something the player should know about the save, such as a damaged file or a failed write, to show without
    /// stopping play; null when all is well. <see cref="ClearNotice"/> drops it once shown.
    /// </summary>
    public string? Notice { get; private set; }

    /// <summary>
    /// True when the save came from a newer version of the game or couldn't be read at all, such as while another
    /// program held it. Writes are skipped so this run never overwrites it.
    /// </summary>
    public bool ReadOnly { get; private set; }

    /// <summary>Called after the temp file is written and before it replaces the save. Tests throw here to simulate a crash.</summary>
    internal Action? BeforeReplace { get; set; }

    /// <summary>
    /// The save folder for this OS: <c>%APPDATA%\PuzzleBoyCommunity</c> on Windows,
    /// <c>~/Library/Application Support/PuzzleBoyCommunity</c> on macOS, and <c>$XDG_DATA_HOME/puzzle-boy-community</c>
    /// (or <c>~/.local/share/puzzle-boy-community</c>) elsewhere.
    /// </summary>
    public static string DefaultFolder() => DefaultFolder(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"));

    /// <summary>The save folder for the given OS, folders and <c>XDG_DATA_HOME</c>, which counts only when absolute.</summary>
    internal static string DefaultFolder(bool windows, bool mac, string appData, string home, string? xdgDataHome)
    {
        if (windows)
        {
            return System.IO.Path.Combine(appData, "PuzzleBoyCommunity");
        }

        if (mac)
        {
            return System.IO.Path.Combine(home, "Library", "Application Support", "PuzzleBoyCommunity");
        }

        // The XDG spec says a relative value is invalid and must be ignored.
        var data = !string.IsNullOrEmpty(xdgDataHome) && System.IO.Path.IsPathRooted(xdgDataHome) ? xdgDataHome : System.IO.Path.Combine(home, ".local", "share");
        return System.IO.Path.Combine(data, "puzzle-boy-community");
    }

    /// <summary>
    /// Opens the save in <paramref name="folder"/>: <c>save.json</c>, else its backup, else defaults. A temp file left
    /// by a crash is ignored; the save it was replacing is still whole.
    /// </summary>
    public static SaveStore Load(string folder)
    {
        var store = new SaveStore(System.IO.Path.GetFullPath(folder));
        var (main, mainError, unreadable) = store.Read(store.MainPath);
        if (main is not null)
        {
            store.Data = main;
            return store;
        }

        if (mainError is null)
        {
            // No save yet: a fresh profile. A backup without a save only happens if someone deleted the save.
            var (fresh, _, _) = store.Read(store.BackupPath);
            store.Data = fresh ?? new SaveData();
            return store;
        }

        if (unreadable)
        {
            // Antivirus, backup or sync software can hold the file for a moment. It isn't damaged, so this run mustn't
            // replace it with older progress.
            store.ReadOnly = true;
            var (old, _, _) = store.Read(store.BackupPath);
            store.Data = old ?? new SaveData();
            store.Notice = $"Your save couldn't be read ({mainError}), so {(old is null ? "the game starts fresh" : "the backup was loaded")} and nothing is saved until you restart. The file itself is left as it was.";
            return store;
        }

        store._mainDamaged = true;
        var kept = store.KeepDamaged() ? $" The damaged file was kept as {FileName}.damaged." : "";
        var (backup, backupError, _) = store.Read(store.BackupPath);
        if (backup is not null)
        {
            store.Data = backup;
            store.Notice = $"Your save was damaged ({mainError}), so the backup was loaded.{kept}";
            return store;
        }

        store.Notice = $"Your save was damaged ({mainError}) and {(backupError is null ? "there was no backup" : "so was the backup")}, so the game starts fresh.{kept}";
        return store;
    }

    /// <summary>
    /// Writes <see cref="Data"/> atomically. Returns false, with a <see cref="Notice"/>, when the folder can't be
    /// written; the game carries on either way.
    /// </summary>
    public bool Save()
    {
        if (ReadOnly)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            Data.SchemaVersion = SaveData.CurrentVersion;
            using (var temp = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(temp, Data, Json);
                temp.Flush(flushToDisk: true);
            }

            BeforeReplace?.Invoke();
            if (File.Exists(MainPath) && !_mainDamaged)
            {
                File.Replace(TempPath, MainPath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                // No good save to back up: the backup, if any, stays the last good one.
                File.Move(TempPath, MainPath, overwrite: true);
            }

            _mainDamaged = false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Notice = $"Progress couldn't be saved to {Folder}: {e.Message}";
            return false;
        }
    }

    /// <summary>Drops the notice once the player has seen it.</summary>
    public void ClearNotice() => Notice = null;

    /// <summary>
    /// Reads one save file. Returns the data, or null with null for a missing file, or null with a short reason for a
    /// damaged one, or for one that couldn't be read at all, which <c>Unreadable</c> marks. A file from a newer version
    /// loads defaults and makes the store <see cref="ReadOnly"/>.
    /// </summary>
    private (SaveData? Data, string? Error, bool Unreadable) Read(string path)
    {
        JsonNode? root;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (null, null, false);
            }

            if (info.Length > MaxBytes)
            {
                return (null, "it's too big", false);
            }

            // An editor may add a byte order mark when a player saves the file by hand.
            var bytes = File.ReadAllBytes(path).AsSpan();
            if (bytes.StartsWith("﻿"u8))
            {
                bytes = bytes[3..];
            }

            // A duplicate property would otherwise throw ArgumentException later, when the object is first read.
            root = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            return (null, "it isn't valid JSON", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, e.Message, true);
        }

        if (root is not JsonObject obj)
        {
            return (null, "it isn't a save", false);
        }

        try
        {
            var version = obj["schemaVersion"] is { } v ? v.GetValue<int>() : 0;
            SaveData? data;
            if (version > SaveData.CurrentVersion)
            {
                ReadOnly = true;
                Notice = "Your save was made by a newer version of the game. It's left untouched, and progress in this version won't be saved.";
                data = new SaveData();
            }
            else
            {
                data = version switch
                {
                    0 => MigrateV0(obj),
                    1 => obj.Deserialize<SaveData>(Json),
                    _ => null,
                };
            }

            if (data is null)
            {
                return (null, "it isn't a save", false);
            }

            data.Normalize();
            return (data, null, false);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return (null, "it isn't a save", false);
        }
    }

    /// <summary>
    /// The legacy shape, schema 0, which had no <c>schemaVersion</c>: <c>{ "cleared": [ids], "volume": 0..1 }</c>. Its
    /// cleared stages become completed with no best, and its one volume sets both music and effects.
    /// </summary>
    private static SaveData MigrateV0(JsonObject legacy)
    {
        var data = new SaveData();
        if (legacy["cleared"] is JsonArray cleared)
        {
            foreach (var id in cleared.Select(c => c?.GetValue<string>()).OfType<string>())
            {
                data.Stages[id] = new StageProgress { Completed = true };
            }
        }

        if (legacy["volume"] is { } volume)
        {
            data.Settings.MusicVolume = data.Settings.SfxVolume = volume.GetValue<float>();
        }

        return data;
    }

    // A copy of a damaged save for the player, since the next write replaces it. False when it couldn't be made.
    private bool KeepDamaged()
    {
        try
        {
            File.Copy(MainPath, MainPath + ".damaged", overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keeping a copy is a courtesy; the game goes on without it.
            return false;
        }
    }
}
