using Microsoft.Xna.Framework;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Graphics;

namespace PuzzleBoy.Community;

/// <summary>The menu screens: title, stage select, pause, settings, credits and the placeholders.</summary>
internal sealed partial class CommunityGame
{
    /// <summary>The title menu, in order.</summary>
    internal static readonly string[] TitleItems = ["Play", "Stage Select", "Level Editor", "Settings", "Credits", "Quit"];

    /// <summary>The pause menu, in order.</summary>
    internal static readonly string[] PauseItems = ["Resume", "Undo", "Restart", "Help", "Settings", "Quit to Title"];

    /// <summary>The settings menu while it's a placeholder, in order.</summary>
    internal static readonly string[] SettingsItems = ["Import Classic Content", "Back"];

    /// <summary>
    /// The trademark and affiliation notice, as the credits show it. A shorter form sits on the title screen.
    /// </summary>
    internal const string Notice = "Inspired by Puzzle Boy (Atlus) and its 3D version in Shin Megami Tensei III: Nocturne. Puzzle Boy, Shin Megami Tensei and Nocturne are trademarks of ATLUS and SEGA. This is an independent fan project, not affiliated with or endorsed by either company. It contains no ATLUS or SEGA assets.";

    private static readonly MenuLayout TitleMenu = new(96, 300, 400, 54);
    private static readonly MenuLayout PauseMenu = new(470, 236, 340, 50);
    private static readonly MenuLayout SettingsMenu = new(440, 360, 400, 54);
    private static readonly MenuLayout SelectMenu = new(340, 190, 600, 54);
    private static readonly MenuLayout BackButton = new(540, 600, 200, 54);

    private void UpdateTitle(Controls controls)
    {
        if (Cancelled(controls))
        {
            // Back on the title only points at Quit; it never quits by itself.
            if (_titleMenu.Set(TitleItems.Length - 1))
            {
                _sounds.Play(Sfx.MenuMove);
            }

            return;
        }

        switch (RunMenu(_titleMenu, controls, TitleMenu))
        {
            case 0:
                var (pack, index) = _save.Data.NextToPlay(_manifest.Packs);
                StartStage(pack, index);
                break;
            case 1:
                _nav.Open(Screen.StageSelect);
                break;
            case 2:
                _nav.Open(Screen.Editor);
                break;
            case 3:
                _nav.Open(Screen.Settings);
                break;
            case 4:
                _nav.Open(Screen.Credits);
                break;
            case 5:
                Exit();
                break;
        }
    }

    private void UpdateStageSelect(Controls controls)
    {
        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        if (RunMenu(_selectMenu, controls, SelectMenu) is { } choice && choice < _selectable.Count)
        {
            var (pack, index) = _selectable[choice];
            StartStage(pack, index);
        }
    }

    private void UpdatePaused(Controls controls)
    {
        if (_helpOpen)
        {
            if (Cancelled(controls) || controls.Pressed(Command.Confirm) || _clicked)
            {
                _helpOpen = false;
                _sounds.Play(Sfx.Cancel);
            }

            return;
        }

        if (Cancelled(controls) || controls.Pressed(Command.Pause))
        {
            GoBack();
            return;
        }

        switch (RunMenu(_pauseMenu, controls, PauseMenu))
        {
            case 0:
                _nav.Back();
                break;
            case 1:
                // The menu stays open so several undos can follow.
                if (!_session!.Undo())
                {
                    _sounds.Play(Sfx.Buzz);
                }

                break;
            case 2:
                _session!.Restart();
                _nav.Back();
                break;
            case 3:
                _helpOpen = true;
                break;
            case 4:
                _nav.Open(Screen.Settings);
                break;
            case 5:
                _session = null;
                _nav.ReturnTo(Screen.Title);
                break;
        }
    }

    private void UpdateSettings(Controls controls)
    {
        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        switch (RunMenu(_settingsMenu, controls, SettingsMenu))
        {
            case 0:
                _nav.Open(Screen.ImportClassic);
                break;
            case 1:
                _nav.Back();
                break;
        }
    }

    private void DrawTitle()
    {
        DrawDrift(0.7f);
        DrawTitleJack();
        _ui.Begin();
        _ui.ShadowText(Font.TitleBold, 104, "PUZZLE BOY", new Vector2(88, 64), _ui["cream"], _ui["pumpkinShade"], spacing: 2, depth: 7);
        _ui.Text(Font.Title, 30, "COMMUNITY EDITION", new Vector2(96, 196), _ui["bandOnDark"], spacing: 5);
        DrawMenu(_titleMenu, TitleItems, TitleMenu, 30);
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Select"), ("Esc", "Back")], 640, 636, _ui["cream"], 17);
        _ui.Text(Font.Body, 15, "A fan project inspired by Puzzle Boy. Not affiliated with or endorsed by ATLUS or SEGA.", new Vector2(640, 690), _ui["cream"] * 0.6f, 0.5f);
        _ui.End();
    }

    private void DrawStageSelect()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("STAGE SELECT");

        // shortcut: no scrolling yet; the list fits 8 stages, and C2 replaces this screen with the real stage select.
        var labels = _selectable.Select(s => $"{s.Pack.Title}  ·  {s.Index + 1}   {s.Pack.Stages[s.Index].Title}").ToArray();
        DrawMenu(_selectMenu, labels, SelectMenu, 26);
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Play"), ("Esc", "Back")], 640, 660, _ui["cream"], 17);
        _ui.End();
    }

    private void DrawSettings()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("SETTINGS");
        _ui.Text(Font.Body, 24, "Volume, display and accessibility options arrive with saving, in a later update.", new Vector2(640, 260), _ui["cream"], 0.5f);
        DrawMenu(_settingsMenu, SettingsItems, SettingsMenu, 28);
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Select"), ("Esc", "Back")], 640, 660, _ui["cream"], 17);
        _ui.End();
    }

    private void DrawCredits()
    {
        DrawDrift(0.4f);
        _ui.Begin();
        Heading("CREDITS");
        var (x, width) = (220f, 840f);
        var y = 170f;
        void Section(string title, string text)
        {
            _ui.Text(Font.Title, 18, title, new Vector2(x, y), _ui["ember"], spacing: 2);
            y += 30;
            y += _ui.Wrapped(Font.Body, 21, text, new Vector2(x, y), width, _ui["cream"]) + 16;
        }

        Section("PUZZLE BOY COMMUNITY EDITION", "Game, stages, Pixel Jack, music and sound by the Puzzle Boy Community authors. Code and original content under the Apache License 2.0.");
        Section("FONTS", "Atkinson Hyperlegible by the Braille Institute of America (Elliott Scott, Megan Eiswerth, Linus Boman, Theodore Petrosky).\nSilkscreen by Jason Kottke. Both under the SIL Open Font License 1.1.");
        Section("COLORS", "Block colors from the Okabe-Ito colorblind-safe palette.");
        Section("INSPIRATION", Notice);
        DrawMenu(_backMenu, ["Back"], BackButton, 26);
        _ui.End();
    }

    private void DrawPlaceholder(string title, string text)
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading(title);
        _ui.Wrapped(Font.Body, 26, text, new Vector2(290, 280), 700, _ui["cream"]);
        DrawMenu(_backMenu, ["Back"], BackButton, 26);
        _ui.End();
    }

    private void DrawPaused()
    {
        _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
        if (_helpOpen)
        {
            DrawHelp();
            return;
        }

        _ui.Panel(430, 140, 420, 430, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 40, "PAUSED", new Vector2(640, 170), _ui["cream"], 0.5f, 2);
        DrawMenu(_pauseMenu, PauseItems, PauseMenu, 26);
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Select"), ("Esc", "Resume")], 640, 680, _ui["cream"], 16);
    }

    private void DrawHelp()
    {
        _ui.Panel(200, 70, 880, 580, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 36, "HOW TO PLAY", new Vector2(640, 96), _ui["cream"], 0.5f, 2);
        var height = _ui.Wrapped(Font.Body, 21, "Lead Pixel Jack to the glowing goal. Walk into a block to push it; it moves only if all of it can. Push a block entirely over holes and it drops in, making floor. Push a swivel's arm and it turns around its center. Undo takes back your last push or turn.", new Vector2(250, 156), 780, _ui["cream"]);
        (string Keys, string Pad, string Action)[] rows =
        [
            ("Arrows / WASD", "D-pad / stick", "Move (hold to keep walking)"),
            ("Q  E", "LB  RB", "Turn the view"),
            ("V", "Y", "Overhead view"),
            ("Z / Backspace", "B", "Undo"),
            ("R", "Back", "Restart"),
            ("C", "X", "Switch character"),
            ("Esc / P", "Start", "Pause"),
        ];
        var y = 156 + height + 10;
        _ui.Text(Font.Title, 15, "KEYBOARD", new Vector2(250, y), _ui["ember"], spacing: 2);
        _ui.Text(Font.Title, 15, "CONTROLLER", new Vector2(480, y), _ui["ember"], spacing: 2);
        y += 26;
        foreach (var (keys, pad, action) in rows)
        {
            _ui.Text(Font.BodyBold, 20, keys, new Vector2(250, y), _ui["cream"]);
            _ui.Text(Font.BodyBold, 20, pad, new Vector2(480, y), _ui["cream"]);
            _ui.Text(Font.Body, 20, action, new Vector2(680, y), _ui["cream"] * 0.85f);
            y += 30;
        }

        if (_session?.Stage.Hint is { } hint)
        {
            _ui.Text(Font.Title, 15, "HINT", new Vector2(250, y + 8), _ui["ember"], spacing: 2);
            _ui.Wrapped(Font.Body, 20, hint, new Vector2(330, y + 4), 700, _ui["cream"]);
        }

        _ui.Hints([("Enter", "Close")], 640, 680, _ui["cream"], 16);
    }

    private void Heading(string title)
    {
        _ui.ShadowText(Font.TitleBold, 56, title, new Vector2(640, 72), _ui["cream"], _ui["pumpkinShade"], 0.5f, 2, 5);
    }

    /// <summary>Draws a vertical menu with an ember bar behind the focused item.</summary>
    private void DrawMenu(MenuList menu, string[] labels, MenuLayout layout, float size)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            var top = layout.Top + (i * layout.Spacing);
            var focused = i == menu.Focus;
            if (focused)
            {
                var glow = 0.85f + (0.15f * MathF.Sin(_ticks * 0.15f));
                _ui.Panel(layout.Left - 6, top + 4, layout.Width + 12, layout.Spacing - 8, _ui["pumpkinShade"] * glow);
                _ui.Panel(layout.Left, top + 4, layout.Width, layout.Spacing - 8, _ui["ember"]);
                _ui.Rect(layout.Left + 14, top + (layout.Spacing / 2) - 5, 10, 10, _ui["ink"]);
            }

            var color = focused ? _ui["ink"] : _ui["cream"] * 0.9f;
            _ui.Text(Font.BodyBold, size, labels[i], new Vector2(layout.Left + 36, top + ((layout.Spacing - (size * 1.2f)) / 2)), color);
        }
    }

    /// <summary>Blocks drifting slowly through the dark, behind the menus; <paramref name="strength"/> dims them.</summary>
    private void DrawDrift(float strength)
    {
        var mesh = new Mesh();
        for (var i = 0; i < 16; i++)
        {
            var hash = (uint)((i + 3) * 2246822519u);
            var x = ((hash % 1000) / 1000f * 15) - 7.5f;
            var z = -4 - ((hash / 1000 % 1000) / 1000f * 10);
            var speed = 0.006f + (hash % 7 * 0.002f);
            var y = ((((hash % 997) / 997f * 12) + (_ticks * speed)) % 12) - 6;
            var spin = Matrix.CreateRotationX((_ticks * speed * 0.7f) + i) * Matrix.CreateRotationY((_ticks * speed) + (i * 2));
            var group = i + 1;
            mesh.AddBox(new(-0.4f, -0.4f, -0.4f), new(0.4f, 0.4f, 0.4f), _board.GroupColor(group) * strength, BoardRenderer.GroupPattern(group), BoardRenderer.GroupPattern(group), spin * Matrix.CreateTranslation(x, y, z));
        }

        var view = Matrix.CreateLookAt(new Vector3(0, 0.45f, 4.2f), new Vector3(0, 0.45f, 0), Vector3.Up);
        _board.DrawMesh(mesh, Matrix.Identity, view, Projection(40), new Color(0.75f, 0.75f, 0.8f), _ui["night"]);
    }

    private void DrawTitleJack()
    {
        var view = Matrix.CreateLookAt(new Vector3(0, 0.45f, 4.2f), new Vector3(0, 0.45f, 0), Vector3.Up);
        var bob = MathF.Sin(_ticks * 0.08f) * 0.06f;
        var place = Matrix.CreateScale(1.85f) * Matrix.CreateRotationY(MathF.Sin(_ticks * 0.025f) * 0.5f) * Matrix.CreateTranslation(1.55f, -0.95f + bob, 0);
        var stand = new Mesh();
        stand.AddBox(new(-0.55f, -0.22f, -0.55f), new(0.55f, 0, 0.55f), Color.Lerp(_ui["hatOnDark"], _ui["cream"], 0.25f), Atlas.Plain, Atlas.Plain, Matrix.Identity, _ui["band"]);
        _board.DrawMesh(stand, place, view, Projection(40), Color.White);
        _board.DrawMesh(_board.Jack, Matrix.CreateScale(1, 1 + (bob * 0.3f), 1) * place, view, Projection(40), Color.White);
    }

    private Matrix Projection(float fieldOfView) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(fieldOfView), GraphicsDevice.Viewport.AspectRatio, 0.05f, 60f);
}
