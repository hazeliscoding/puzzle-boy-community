using PuzzleBoy.Community.Content;

namespace PuzzleBoy.Community.Save;

/// <summary>A stage's record: whether it was cleared, and the best clear.</summary>
internal sealed class StageProgress
{
    /// <summary>True once the stage has been cleared.</summary>
    public bool Completed { get; set; }

    /// <summary>The fewest steps a clear took, or null before the first clear.</summary>
    public int? BestMoves { get; set; }

    /// <summary>The fastest clear in seconds, from the stage starting to the clear, or null before the first clear.</summary>
    public double? BestTime { get; set; }
}

/// <summary>The player's preferences. Every value is kept in range by <see cref="Normalize"/>.</summary>
internal sealed class GameSettings
{
    /// <summary>The window sizes offered, as multiples of 1280×720.</summary>
    public static readonly float[] WindowScales = [0.75f, 1f, 1.25f, 1.5f, 2f];

    /// <summary>The body-text sizes offered, as multiples of the designed size.</summary>
    public static readonly float[] UiScales = [0.9f, 1f, 1.1f, 1.2f];

    /// <summary>True for fullscreen, false for a window.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>The window's size as a multiple of 1280×720, one of <see cref="WindowScales"/>.</summary>
    public float WindowScale { get; set; } = 1f;

    /// <summary>How big body text is drawn, one of <see cref="UiScales"/>.</summary>
    public float UiScale { get; set; } = 1f;

    /// <summary>The music's volume, 0 to 1.</summary>
    public float MusicVolume { get; set; } = 0.7f;

    /// <summary>The sound effects' volume, 0 to 1.</summary>
    public float SfxVolume { get; set; } = 0.8f;

    /// <summary>True silences everything without changing the volumes.</summary>
    public bool Mute { get; set; }

    /// <summary>True stops decorative motion and makes the view turn at once.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>True makes panels opaque, text full strength and the board's floor, walls and pits starker.</summary>
    public bool HighContrast { get; set; }

    /// <summary>
    /// The look of play, one of <see cref="Themes"/>: <c>auto</c> (Classic when a valid classic pack is there, else
    /// Default), <c>classic</c> or <c>default</c>. A string, so a value from a newer version reads as <c>auto</c> instead
    /// of failing the whole save.
    /// </summary>
    public string Theme { get; set; } = Themes.Auto;

    /// <summary>
    /// The option in <paramref name="options"/> <paramref name="by"/> places from the one nearest <paramref name="current"/>,
    /// held at the ends.
    /// </summary>
    public static float Step(float[] options, float current, int by)
    {
        var nearest = Array.IndexOf(options, options.MinBy(o => Math.Abs(o - current)));
        return options[Math.Clamp(nearest + by, 0, options.Length - 1)];
    }

    /// <summary>
    /// The window size for <paramref name="scale"/> on a display of <paramref name="displayWidth"/>×<paramref name="displayHeight"/>:
    /// 1280×720 times the scale, shrunk to 90% of the display when it wouldn't fit there, so the title bar and taskbar
    /// stay clear.
    /// </summary>
    public static (int Width, int Height) WindowSize(float scale, int displayWidth, int displayHeight)
    {
        var fit = Math.Min(scale, Math.Min(displayWidth * 0.9f / 1280f, displayHeight * 0.9f / 720f));
        return ((int)MathF.Round(1280 * fit), (int)MathF.Round(720 * fit));
    }

    /// <summary>Puts every value back in range, as a save file edited by hand may not be.</summary>
    public void Normalize()
    {
        Theme = Themes.All.Contains(Theme) ? Theme : Themes.Auto;
        WindowScale = Step(WindowScales, WindowScale, 0);
        UiScale = Step(UiScales, UiScale, 0);
        MusicVolume = Volume(MusicVolume);
        SfxVolume = Volume(SfxVolume);
    }

    // Volumes move in tenths; NaN reads as silence.
    private static float Volume(float value) => float.IsNaN(value) ? 0 : MathF.Round(Math.Clamp(value, 0, 1) * 10) / 10;
}

/// <summary>The values of <see cref="GameSettings.Theme"/>, in the order the setting steps through them.</summary>
internal static class Themes
{
    /// <summary>Classic when a valid classic pack is there, else Default.</summary>
    public const string Auto = "auto";

    /// <summary>The original minigame's look and sound, from the player's classic pack.</summary>
    public const string Classic = "classic";

    /// <summary>Community's own drawn graphics and synthesized sound, which need nothing else.</summary>
    public const string Default = "default";

    /// <summary>Every value, in order.</summary>
    public static readonly string[] All = [Auto, Classic, Default];
}

/// <summary>
/// Everything the game keeps between runs: the settings and each stage's progress by its pack-scoped id. Written by
/// <see cref="SaveStore"/> as JSON with a <see cref="SchemaVersion"/>.
/// </summary>
internal sealed class SaveData
{
    /// <summary>The schema this build writes. Schema 2 added the theme setting.</summary>
    public const int CurrentVersion = 2;

    /// <summary>The schema the data was written in; always <see cref="CurrentVersion"/> once loaded.</summary>
    public int SchemaVersion { get; set; } = CurrentVersion;

    /// <summary>The preferences.</summary>
    public GameSettings Settings { get; set; } = new();

    /// <summary>Progress by stage id, <c>pack-id/stage-id</c>. A stage never played has no entry.</summary>
    public Dictionary<string, StageProgress> Stages { get; set; } = [];

    /// <summary>True once stage <paramref name="id"/> has been cleared.</summary>
    public bool IsCompleted(string id) => Stages.TryGetValue(id, out var progress) && progress.Completed;

    /// <summary>
    /// True when stage <paramref name="index"/> of <paramref name="stages"/> can be played: the first stage of a pack,
    /// a stage cleared before, and the two stages after a cleared one, so one hard stage never blocks the rest. The
    /// player's own stages (<c>custom/…</c>) are always open.
    /// </summary>
    public bool IsUnlocked(IReadOnlyList<PackStage> stages, int index) =>
        index == 0 || stages[index].Id.StartsWith($"{CustomStages.PackId}/", StringComparison.Ordinal) || IsCompleted(stages[index].Id) || IsCompleted(stages[index - 1].Id) || (index >= 2 && IsCompleted(stages[index - 2].Id));

    /// <summary>
    /// Records a clear of stage <paramref name="id"/> in <paramref name="moves"/> steps and <paramref name="seconds"/>.
    /// Returns true when it beat the best moves or time, or was the first clear.
    /// </summary>
    public bool Record(string id, int moves, double seconds)
    {
        if (!Stages.TryGetValue(id, out var progress))
        {
            Stages[id] = progress = new StageProgress();
        }

        // A clear with no best recorded, as a migrated save holds, sets the first best.
        var better = !progress.Completed || progress.BestMoves is null || progress.BestTime is null || moves < progress.BestMoves || seconds < progress.BestTime;
        progress.Completed = true;
        progress.BestMoves = Math.Min(moves, progress.BestMoves ?? int.MaxValue);
        progress.BestTime = Math.Min(Math.Round(seconds, 2), progress.BestTime ?? double.MaxValue);
        return better;
    }

    /// <summary>
    /// The stage Play starts: the first unlocked stage not yet cleared, in pack order, or the very last stage when every
    /// one is cleared. <paramref name="packs"/> must hold at least one stage.
    /// </summary>
    public (StagePack Pack, int Index) NextToPlay(IReadOnlyList<StagePack> packs)
    {
        foreach (var pack in packs)
        {
            for (var i = 0; i < pack.Stages.Count; i++)
            {
                if (!IsCompleted(pack.Stages[i].Id) && IsUnlocked(pack.Stages, i))
                {
                    return (pack, i);
                }
            }
        }

        var last = packs.Last(p => p.Stages.Count > 0);
        return (last, last.Stages.Count - 1);
    }

    /// <summary>Puts every value back in range and drops records that make no sense, as a hand-edited file may hold.</summary>
    public void Normalize()
    {
        Settings ??= new GameSettings();
        Settings.Normalize();
        Stages ??= [];
        foreach (var (id, progress) in Stages.ToList())
        {
            // An infinite time loads (1e400 reads as one) but can't be written back.
            if (progress is null || progress.BestMoves < 0 || progress.BestTime is { } time && (time < 0 || !double.IsFinite(time)))
            {
                Stages.Remove(id);
            }
        }
    }
}
