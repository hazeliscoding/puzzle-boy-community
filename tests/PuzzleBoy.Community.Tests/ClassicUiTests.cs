using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rendering;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>The Nocturne-styled interface's logic: the original HUD's timing, the key prompts, the □ menu and when it shows.</summary>
public class ClassicUiTests
{
    private static PlaySession Session(string drawing) => new([new PackStage("t/ui", "UI", SolverTests.Draw(drawing))], 0);

    /// <summary>Ticks the session and the HUD together, as the game does, <paramref name="ticks"/> times.</summary>
    private static List<HudState> Run(PlaySession session, ClassicHud hud, CameraRig camera, int ticks, ClassicOverlay overlay = ClassicOverlay.None)
    {
        var states = new List<HudState>();
        for (var i = 0; i < ticks; i++)
        {
            session.Tick();
            hud.Tick(session, camera, overlay);
            states.Add(hud.State);
        }

        return states;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(19, 20)]
    [InlineData(98, 99)]
    [InlineData(99, 99)]
    [InlineData(199, 99)]
    public void The_counter_shows_the_stage_number_up_to_its_two_digits(int index, int shown) =>
        Assert.Equal(shown, ClassicHud.Counter(index));

    [Fact]
    public void Undo_shows_for_thirty_ticks_from_the_tick_it_happens_as_the_original_shows_it()
    {
        var (session, hud, camera) = (Session("1b..G"), new ClassicHud(), new CameraRig());
        Run(session, hud, camera, 5);
        Assert.Equal(StepResult.Pushed, session.Step(Direction.PlusX));
        Run(session, hud, camera, 20);
        Assert.True(session.Undo());
        hud.Undid();
        var states = Run(session, hud, camera, 40);
        Assert.Equal(ClassicHud.ShowTicks, states.Count(s => s.Undo));
        Assert.All(states.Take(ClassicHud.ShowTicks), s => Assert.True(s.Undo));
    }

    [Fact]
    public void Complete_shows_for_thirty_ticks_once_the_last_step_lands()
    {
        var (session, hud, camera) = (Session("1G"), new ClassicHud(), new CameraRig());
        Run(session, hud, camera, 5);
        Assert.Equal(StepResult.Walked, session.Step(Direction.PlusX));
        var states = Run(session, hud, camera, 60);
        var first = states.FindIndex(s => s.Clear);
        Assert.Equal(ClassicHud.ShowTicks, states.Count(s => s.Clear));
        Assert.Equal(1, session.ClearTicks - (states.Count - 1 - first));
    }

    [Fact]
    public void The_bar_shows_the_keys_for_play_scroll_the_menu_and_help_and_fades_in_again_when_they_change()
    {
        var (session, hud, camera) = (Session("1..G"), new ClassicHud(), new CameraRig());
        Assert.Equal(BarSet.Play, Run(session, hud, camera, 10)[^1].Bar);
        Assert.Equal(9, hud.State.BarTicks);

        Assert.Equal((BarSet.Menu, 0), (Run(session, hud, camera, 1, ClassicOverlay.Menu)[^1].Bar, hud.State.BarTicks));
        Assert.Equal((BarSet.Help, 0), (Run(session, hud, camera, 1, ClassicOverlay.Help)[^1].Bar, hud.State.BarTicks));
        session.ToggleScroll();
        Assert.Equal((BarSet.Scroll, 3), (Run(session, hud, camera, 4)[^1].Bar, hud.State.BarTicks));
        Assert.Equal(3, hud.State.ScrollTicks);

        // A restart starts the stage over, and the bar fades in again.
        session.Restart();
        Assert.Equal((BarSet.Play, 0), (Run(session, hud, camera, 1)[^1].Bar, hud.State.BarTicks));
    }

    [Fact]
    public void The_scroll_arrows_show_each_screen_direction_the_cursor_can_still_move()
    {
        var camera = new CameraRig();
        Assert.Equal(0, ClassicHud.Arrows(null, camera));
        Assert.Equal(15, ClassicHud.Arrows((10, 10), camera));

        // At a corner two directions are left, whichever way the view has turned.
        var corner = ClassicHud.Arrows((0, 0), camera);
        Assert.Equal(2, int.PopCount(corner));
        Assert.True(camera.TurnLeft());
        for (var i = 0; i < 20; i++)
        {
            camera.Tick();
        }

        Assert.NotEqual(corner, ClassicHud.Arrows((0, 0), camera));
        Assert.Equal(2, int.PopCount(ClassicHud.Arrows((0, 0), camera)));
    }

    [Theory]
    [InlineData("W", 28, 60)]
    [InlineData("A", 28 + (72 * 0), 60)]
    [InlineData("D", 244, 60)]
    [InlineData("F", 388, 60)]
    [InlineData("V", 1540, 60)]
    [InlineData("Z", 1828, 60)]
    [InlineData("Tab", 460, 108)]
    [InlineData("Space", 532, 108)]
    [InlineData("Backspace", 316, 108)]
    [InlineData("Ctrl", 28, 108)]
    public void Keys_with_an_icon_on_the_keyboard_sheet_use_it(string key, int x, int y)
    {
        // W is 22 letters on from A; the sheet's letters run 72 texels apart.
        var expected = key == "W" ? new Rectangle(28 + (72 * 22), 60, 48, 48) : new Rectangle(x, y, 48, 48);
        Assert.Equal(expected, ClassicSkin.KeyIcon(key));
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("Esc")]
    [InlineData("Arrows")]
    [InlineData("1-9")]
    [InlineData("a")]
    public void Keys_the_sheet_has_no_icon_for_get_a_drawn_cap(string key) => Assert.Null(ClassicSkin.KeyIcon(key));

    [Fact]
    public void The_hint_keys_the_sheet_has_icons_for_match_the_shared_HUDs_own()
    {
        // The shared HUD's PC keys, at its own rects: W, A, S, D, Q, E, F, V, Tab, Space, Backspace.
        Assert.Equal(new Rectangle(1612, 60, 48, 48), ClassicSkin.KeyIcon("W"));
        Assert.Equal(new Rectangle(1324, 60, 48, 48), ClassicSkin.KeyIcon("S"));
        Assert.Equal(new Rectangle(1180, 60, 48, 48), ClassicSkin.KeyIcon("Q"));
        Assert.Equal(new Rectangle(316, 60, 48, 48), ClassicSkin.KeyIcon("E"));
    }

    [Theory]
    [InlineData("Up Down", "Up")]
    [InlineData("Arrows", "Down")]
    [InlineData("Left Right", "Left")]
    [InlineData("Enter", "Confirm")]
    [InlineData("Esc", "Back")]
    public void The_original_prompts_in_menus_name_keys_that_do_the_same(string community, string command)
    {
        var original = CommunityGame.OriginalPrompt(community);
        Assert.NotEqual(community, original);
        var keys = original.Split(' ').Select(k => k == "Backspace" ? Keys.Back : Enum.Parse<Keys>(k)).ToList();
        var bound = Controls.KeyMap.Where(e => keys.Any(k => e.Value.Contains(k))).Select(e => e.Key).ToList();
        Assert.Contains(Enum.Parse<Command>(command), bound);
    }

    [Fact]
    public void The_original_menu_keeps_its_order_with_Settings_before_Quit_and_no_Resume()
    {
        Assert.Equal([PauseAction.Undo, PauseAction.Restart, PauseAction.Help, PauseAction.Settings, PauseAction.Quit], CommunityGame.ClassicPauseActions);
        Assert.Equal([PauseAction.Resume, PauseAction.Undo, PauseAction.Restart, PauseAction.Help, PauseAction.Settings, PauseAction.Quit], CommunityGame.PauseActions);
        Assert.Equal(CommunityGame.PauseItems.Length, CommunityGame.PauseActions.Length);

        // Both menus put Quit two below Help and last, so Up from the first item reaches it.
        foreach (var menu in new[] { CommunityGame.ClassicPauseActions, CommunityGame.PauseActions })
        {
            Assert.Equal(Array.IndexOf(menu, PauseAction.Help) + 2, Array.IndexOf(menu, PauseAction.Quit));
            Assert.Equal(PauseAction.Quit, menu[^1]);
        }
    }

    [Fact]
    public void The_font_check_finds_missing_characters_after_swapping_in_look_alikes()
    {
        Assert.True(ClassicSkin.Covers(ClassicFixture.Font, "STAGE CLEAR"));
        Assert.False(ClassicSkin.Covers(ClassicFixture.Font, "Stage"));
        Assert.Equal("+ - zoom", ClassicSkin.Normalize("+ − zoom"));
    }

    [Theory]
    [InlineData("TitleBold", "TMC12")]
    [InlineData("Title", "TMC24")]
    [InlineData("Body", "TMC22")]
    [InlineData("BodyBold", "TMC22")]
    public void Each_font_role_takes_one_of_the_originals_materials(string font, string material) =>
        Assert.Equal(material, ClassicSkin.MaterialFor(Enum.Parse<Font>(font)));

    [Theory]
    [InlineData(Themes.Default, true, false)]
    [InlineData(Themes.Default, false, false)]
    [InlineData(Themes.Auto, false, false)]
    [InlineData(Themes.Auto, true, true)]
    [InlineData(Themes.Classic, true, true)]
    public void Only_a_theme_that_allows_Classic_with_a_pack_tries_the_Nocturne_styled_interface(string setting, bool pack, bool uses) =>
        Assert.Equal(uses, ClassicTheme.UsesInterface(setting, pack));
}
