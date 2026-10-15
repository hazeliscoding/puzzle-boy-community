using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.Classic;

/// <summary>Which theme play uses: the setting, weighed against the classic pack the player has.</summary>
internal static class ClassicTheme
{
    /// <summary>
    /// True when Community's screens should try the Nocturne-styled interface: the setting allows the Classic theme and
    /// a classic pack is there (it's checked when loaded). The Default setting never touches a pack.
    /// </summary>
    /// <param name="setting">One of <see cref="Themes"/>.</param>
    /// <param name="packPresent">True when the pack the game would use holds a <c>classic-pack.json</c>.</param>
    public static bool UsesInterface(string setting, bool packPresent) => setting != Themes.Default && packPresent;

    /// <summary>
    /// True when play should use the Classic theme: the setting is <c>auto</c> or <c>classic</c> and the classic pack passed
    /// its check. Otherwise play uses the Default theme, and <paramref name="notice"/> says why when the player should
    /// know: they chose Classic and have no pack, or their pack is there but broken. Auto with no pack is silent.
    /// </summary>
    /// <param name="setting">One of <see cref="Themes"/>.</param>
    /// <param name="packPresent">True when <c>&lt;data dir&gt;/classic</c> holds a <c>classic-pack.json</c>.</param>
    /// <param name="packProblem">Why the pack failed its check, or null when it passed (or there's none).</param>
    /// <param name="notice">A sentence for the player, or null.</param>
    public static bool Choose(string setting, bool packPresent, string? packProblem, out string? notice)
    {
        notice = null;
        if (setting == Themes.Default)
        {
            return false;
        }

        if (!packPresent)
        {
            notice = setting == Themes.Classic ? "The Classic theme needs your own classic pack: import one in Settings, Classic Nocturne. Playing with the Default theme." : null;
            return false;
        }

        if (packProblem is not null)
        {
            notice = $"Your classic pack can't be used, so this plays with the Default theme. {packProblem}";
            return false;
        }

        return true;
    }
}
