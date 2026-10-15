using System.Globalization;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.App;

/// <summary>
/// The settings screen's rows: what each one says and how left, right and confirm change it. The last three rows are
/// actions the game carries out. Plain logic, so it's tested without a window.
/// </summary>
internal static class SettingsMenu
{
    /// <summary>The row that opens the optional classic import.</summary>
    public const int ImportRow = 8;

    /// <summary>The row that resets every setting, after a confirmation.</summary>
    public const int ResetRow = 9;

    /// <summary>The row that goes back.</summary>
    public const int BackRow = 10;

    /// <summary>The rows' labels, in order.</summary>
    public static readonly string[] Labels =
    [
        "Display", "Window size", "UI scale", "Music volume", "Sound volume", "Mute", "Reduce motion", "High contrast",
        "Import Classic Content", "Reset to defaults", "Back",
    ];

    // Volumes in tenths, 0 to 1.
    private static readonly float[] Volumes = [.. Enumerable.Range(0, 11).Select(i => i / 10f)];

    /// <summary>True for the rows shown as a 0 to 100% bar.</summary>
    public static bool IsSlider(int row) => row is 3 or 4;

    /// <summary>True for the rows that hold a value; the rest are actions.</summary>
    public static bool HasValue(int row) => row < ImportRow;

    /// <summary>What row <paramref name="row"/> shows for <paramref name="settings"/>, or an empty string for an action.</summary>
    public static string Value(GameSettings settings, int row) => row switch
    {
        0 => settings.Fullscreen ? "Fullscreen" : "Windowed",
        1 => string.Create(CultureInfo.InvariantCulture, $"{1280 * settings.WindowScale:0} × {720 * settings.WindowScale:0}"),
        2 => Percent(settings.UiScale),
        3 => Percent(settings.MusicVolume),
        4 => Percent(settings.SfxVolume),
        5 => OnOff(settings.Mute),
        6 => OnOff(settings.ReduceMotion),
        7 => OnOff(settings.HighContrast),
        _ => "",
    };

    /// <summary>
    /// Moves row <paramref name="row"/>'s value <paramref name="by"/> steps: a switch flips, a choice or a volume moves
    /// one option (volumes by 10%) and holds at the ends, or wraps when <paramref name="wrap"/>, as confirm does.
    /// Returns true when the value changed.
    /// </summary>
    public static bool Adjust(GameSettings settings, int row, int by, bool wrap = false)
    {
        var before = Value(settings, row);
        switch (row)
        {
            case 0:
                settings.Fullscreen = !settings.Fullscreen;
                break;
            case 1:
                settings.WindowScale = Choose(GameSettings.WindowScales, settings.WindowScale, by, wrap);
                break;
            case 2:
                settings.UiScale = Choose(GameSettings.UiScales, settings.UiScale, by, wrap);
                break;
            case 3:
                settings.MusicVolume = Choose(Volumes, settings.MusicVolume, by, wrap);
                break;
            case 4:
                settings.SfxVolume = Choose(Volumes, settings.SfxVolume, by, wrap);
                break;
            case 5:
                settings.Mute = !settings.Mute;
                break;
            case 6:
                settings.ReduceMotion = !settings.ReduceMotion;
                break;
            case 7:
                settings.HighContrast = !settings.HighContrast;
                break;
        }

        return Value(settings, row) != before;
    }

    private static float Choose(float[] options, float current, int by, bool wrap)
    {
        var next = GameSettings.Step(options, current, by);
        return wrap && next == current ? options[by > 0 ? 0 : ^1] : next;
    }

    private static string Percent(float value) => string.Create(CultureInfo.InvariantCulture, $"{value * 100:0}%");

    private static string OnOff(bool value) => value ? "On" : "Off";
}
