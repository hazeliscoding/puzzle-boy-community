using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;

namespace PuzzleBoy.Community.Tests;

public class AppTests
{
    [Fact]
    public void The_game_opens_on_the_title_and_back_returns_through_each_screen_opened()
    {
        var nav = new Navigator();
        Assert.Equal(Screen.Title, nav.Current);
        nav.Open(Screen.Settings);
        nav.Open(Screen.ImportClassic);
        Assert.True(nav.Back());
        Assert.Equal(Screen.Settings, nav.Current);
        Assert.True(nav.Back());
        Assert.Equal(Screen.Title, nav.Current);
    }

    [Fact]
    public void Back_on_the_title_stays_on_the_title()
    {
        var nav = new Navigator();
        Assert.False(nav.Back());
        Assert.Equal([Screen.Title], nav.Stack);
    }

    [Fact]
    public void Quitting_to_the_title_from_a_pause_clears_the_screens_between()
    {
        var nav = new Navigator();
        nav.Open(Screen.StageSelect);
        nav.Open(Screen.Playing);
        nav.Open(Screen.Paused);
        nav.Open(Screen.Title);
        Assert.Equal([Screen.Title], nav.Stack);
    }

    [Fact]
    public void Finishing_a_pack_returns_to_stage_select_even_when_play_started_from_the_title()
    {
        var fromSelect = new Navigator();
        fromSelect.Open(Screen.StageSelect);
        fromSelect.Open(Screen.Playing);
        fromSelect.ReturnTo(Screen.StageSelect);
        Assert.Equal([Screen.Title, Screen.StageSelect], fromSelect.Stack);

        var fromTitle = new Navigator();
        fromTitle.Open(Screen.Playing);
        fromTitle.ReturnTo(Screen.StageSelect);
        Assert.Equal([Screen.Title, Screen.StageSelect], fromTitle.Stack);
    }

    [Fact]
    public void Menu_focus_wraps_at_both_ends()
    {
        var menu = new MenuList(6);
        menu.Move(-1);
        Assert.Equal(5, menu.Focus);
        menu.Move(1);
        Assert.Equal(0, menu.Focus);
        menu.Move(13);
        Assert.Equal(1, menu.Focus);
    }

    [Fact]
    public void Setting_the_focus_reports_only_a_real_change()
    {
        var menu = new MenuList(3);
        Assert.False(menu.Set(0));
        Assert.True(menu.Set(2));
        Assert.False(menu.Set(3));
        Assert.False(menu.Set(-1));
        Assert.Equal(2, menu.Focus);
    }

    [Theory]
    [InlineData(100, 299, -1)]
    [InlineData(100, 300, 0)]
    [InlineData(100, 353, 0)]
    [InlineData(100, 354, 1)]
    [InlineData(495, 600, 5)]
    [InlineData(496, 600, -1)]
    [InlineData(100, 624, -1)]
    [InlineData(95, 320, -1)]
    public void The_pointer_hits_the_item_under_it(float x, float y, int expected) =>
        Assert.Equal(expected, new MenuList(6).HitTest(new Vector2(x, y), 96, 300, 400, 54));

    [Theory]
    [InlineData(Keys.Up, nameof(Command.Up))]
    [InlineData(Keys.W, nameof(Command.Up))]
    [InlineData(Keys.D, nameof(Command.Right))]
    [InlineData(Keys.Left, nameof(Command.Left))]
    [InlineData(Keys.S, nameof(Command.Down))]
    [InlineData(Keys.Q, nameof(Command.TurnLeft))]
    [InlineData(Keys.E, nameof(Command.TurnRight))]
    [InlineData(Keys.V, nameof(Command.Overhead))]
    [InlineData(Keys.C, nameof(Command.Switch))]
    [InlineData(Keys.Z, nameof(Command.Undo))]
    [InlineData(Keys.Back, nameof(Command.Undo))]
    [InlineData(Keys.R, nameof(Command.Restart))]
    [InlineData(Keys.Escape, nameof(Command.Pause))]
    [InlineData(Keys.P, nameof(Command.Pause))]
    [InlineData(Keys.Enter, nameof(Command.Confirm))]
    [InlineData(Keys.Space, nameof(Command.Confirm))]
    [InlineData(Keys.Escape, nameof(Command.Back))]
    public void Each_key_reads_as_its_command(Keys key, string command) =>
        Assert.True(Controls.FromKeys([key]).Pressed(Enum.Parse<Command>(command)));

    [Fact]
    public void A_held_key_presses_once_and_stays_held()
    {
        var held = Controls.FromKeys([Keys.Z], [Keys.Z]);
        Assert.False(held.Pressed(Command.Undo));
        Assert.True(held.Held(Command.Undo));
    }

    [Fact]
    public void A_pad_button_and_the_stick_read_as_commands()
    {
        var pad = new GamePadState(new GamePadThumbSticks(new Vector2(0, -0.9f), Vector2.Zero), default, new GamePadButtons(Buttons.Start), default);
        var controls = new Controls(default, default, pad, default);
        Assert.True(controls.Pressed(Command.Pause));
        Assert.Equal(2, controls.HeldDirection());
    }

    [Fact]
    public void Nothing_held_gives_no_direction() => Assert.Null(Controls.FromKeys([]).HeldDirection());

    [Fact]
    public void A_held_menu_direction_fires_at_once_then_repeats_every_third_tick_after_nine()
    {
        var repeat = new KeyRepeat();
        var fired = Enumerable.Range(1, 18).Where(_ => repeat.Fires(true)).ToList();
        Assert.Equal([1, 12, 15, 18], fired);
        Assert.False(repeat.Fires(false));
        Assert.True(repeat.Fires(true));
    }

    [Theory]
    [InlineData(1280, 720, 0, 0, 0, 0)]
    [InlineData(2560, 1440, 1280, 720, 640, 360)]
    [InlineData(1920, 720, 320, 0, 0, 0)]
    public void The_virtual_screen_fits_centered_in_any_window(int width, int height, int windowX, int windowY, float virtualX, float virtualY) =>
        Assert.Equal(new Vector2(virtualX, virtualY), VirtualScreen.ToVirtual(new Point(windowX, windowY), width, height));

    [Fact]
    public void The_gui_smoke_check_saves_the_stage_beside_the_title_with_a_suffix() =>
        Assert.Equal(Path.GetFullPath(Path.Combine("shots", "run-stage.png")), new SmokeGui(Path.Combine("shots", "run.png")).StagePng);

    [Fact]
    public void The_title_offers_the_six_options_in_order() =>
        Assert.Equal(["Play", "Stage Select", "Level Editor", "Settings", "Credits", "Quit"], CommunityGame.TitleItems);

    [Fact]
    public void The_credits_carry_the_trademark_and_affiliation_notice()
    {
        Assert.Contains("Puzzle Boy, Shin Megami Tensei and Nocturne are trademarks of ATLUS and SEGA.", CommunityGame.Notice);
        Assert.Contains("not affiliated with or endorsed by either company", CommunityGame.Notice);
        Assert.Contains("It contains no ATLUS or SEGA assets.", CommunityGame.Notice);
    }
}
