using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.Tests;

/// <summary>Choosing the theme from the setting and the classic pack.</summary>
public class ThemeTests
{
    private const string Broken = "The classic pack is missing smt3hd_Data/StreamingAssets/PC/common.";

    [Theory]
    [InlineData(Themes.Auto, true, null, true, null)]
    [InlineData(Themes.Classic, true, null, true, null)]
    [InlineData(Themes.Default, true, null, false, null)]
    [InlineData(Themes.Auto, false, null, false, null)]
    [InlineData(Themes.Default, false, null, false, null)]
    [InlineData(Themes.Classic, false, null, false, "The Classic theme needs your own classic pack: import one in Settings, Classic Nocturne. Playing with the Default theme.")]
    [InlineData(Themes.Auto, true, Broken, false, "Your classic pack can't be used, so this plays with the Default theme. " + Broken)]
    [InlineData(Themes.Classic, true, Broken, false, "Your classic pack can't be used, so this plays with the Default theme. " + Broken)]
    [InlineData(Themes.Default, true, Broken, false, null)]
    public void The_theme_is_classic_only_with_a_pack_that_passed_its_check_and_a_fallback_says_why_when_it_matters(string setting, bool present, string? problem, bool classic, string? notice)
    {
        Assert.Equal(classic, ClassicTheme.Choose(setting, present, problem, out var said));
        Assert.Equal(notice, said);
    }

    [Fact]
    public void A_fresh_profile_starts_on_auto() =>
        Assert.Equal(Themes.Auto, new GameSettings().Theme);
}
