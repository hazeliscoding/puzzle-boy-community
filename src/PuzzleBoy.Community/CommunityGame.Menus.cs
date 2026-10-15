using Microsoft.Xna.Framework;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>A stage's state in stage select.</summary>
internal enum StageStatus
{
    /// <summary>Can't be played until one of the two stages before it is cleared.</summary>
    Locked,

    /// <summary>Playable and not cleared yet.</summary>
    Open,

    /// <summary>Cleared at least once.</summary>
    Cleared,
}

/// <summary>The menu screens: title, stage select, pause, settings, credits and the placeholders.</summary>
internal sealed partial class CommunityGame
{
    /// <summary>The title menu, in order.</summary>
    internal static readonly string[] TitleItems = ["Play", "Stage Select", "Level Editor", "Settings", "Credits", "Quit"];

    /// <summary>The pause menu, in order.</summary>
    internal static readonly string[] PauseItems = ["Resume", "Undo", "Restart", "Help", "Settings", "Quit to Title"];

    /// <summary>
    /// The trademark and affiliation notice, as the credits show it. A shorter form sits on the title screen.
    /// </summary>
    internal const string Notice = "Inspired by Puzzle Boy (Atlus) and its 3D version in Shin Megami Tensei III: Nocturne. Puzzle Boy, Shin Megami Tensei and Nocturne are trademarks of ATLUS and SEGA. This is an independent fan project, not affiliated with or endorsed by either company. It contains no ATLUS or SEGA assets.";

    // Stage select shows this many rows and scrolls the rest.
    private const int SelectRows = 9;

    private static readonly MenuLayout TitleMenu = new(96, 300, 400, 54);
    private static readonly MenuLayout PauseMenu = new(470, 262, 340, 50);
    private static readonly MenuLayout SettingsLayout = new(240, 136, 800, 44);
    private static readonly MenuLayout ConfirmLayout = new(490, 380, 300, 54);
    private static readonly MenuLayout SelectMenu = new(72, 150, 580, 50);
    private static readonly MenuLayout BackButton = new(540, 600, 200, 54);

    // Credits run nearly to the bottom, so their Back sits lower.
    private static readonly MenuLayout CreditsBack = new(540, 652, 200, 54);

    /// <summary>Where stage <paramref name="index"/> of <paramref name="stages"/> stands in the player's progress.</summary>
    internal static StageStatus StatusOf(SaveData data, IReadOnlyList<Content.PackStage> stages, int index) =>
        data.IsCompleted(stages[index].Id) ? StageStatus.Cleared : data.IsUnlocked(stages, index) ? StageStatus.Open : StageStatus.Locked;

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
                FocusNextStage();
                _nav.Open(Screen.StageSelect);
                break;
            case 2:
                OpenEditor();
                break;
            case 3:
                OpenSettings();
                break;
            case 4:
                _nav.Open(Screen.Credits);
                break;
            case 5:
                Exit();
                break;
        }
    }

    /// <summary>Focuses the stage Play would start, so stage select opens where the player left off.</summary>
    private void FocusNextStage()
    {
        _selectMenu.Set(_selectable.IndexOf(_save.Data.NextToPlay(_manifest.Packs)));
        KeepFocusVisible();
    }

    private void KeepFocusVisible()
    {
        var focus = _selectMenu.Focus;
        _selectScroll = Math.Clamp(_selectScroll, Math.Max(0, focus - SelectRows + 1), focus);
        _selectScroll = Math.Clamp(_selectScroll, 0, Math.Max(0, _selectable.Count - SelectRows));
    }

    private void UpdateStageSelect(Controls controls)
    {
        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        var choice = RunMenu(_selectMenu, controls, SelectMenu, _selectScroll, SelectRows);
        KeepFocusVisible();
        if (choice is { } chosen && chosen < _selectable.Count)
        {
            var (pack, index) = _selectable[chosen];
            if (_save.Data.IsUnlocked(pack.Stages, index))
            {
                StartStage(pack, index);
            }
            else
            {
                _sounds.Play(Sfx.Buzz);
            }
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
                OpenSettings();
                break;
            case 5:
                _session = null;
                _nav.ReturnTo(Screen.Title);
                break;
        }
    }

    private void OpenSettings()
    {
        _settingsMenu.Set(0);
        _confirmReset = false;
        _nav.Open(Screen.Settings);
    }

    private void UpdateSettings(Controls controls)
    {
        if (_confirmReset)
        {
            UpdateResetConfirm(controls);
            return;
        }

        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        var row = _settingsMenu.Focus;
        if ((_menuLeft || _menuRight) && SettingsMenu.HasValue(row))
        {
            ChangeSetting(row, _menuLeft ? -1 : 1, wrap: false);
        }

        if (RunMenu(_settingsMenu, controls, SettingsLayout) is not { } chosen)
        {
            return;
        }

        if (SettingsMenu.HasValue(chosen))
        {
            // A click on the left half of the value steps it down; anything else steps it up, wrapping.
            var valueLeft = SettingsLayout.Left + SettingsLayout.Width - 330;
            var down = _clicked && _pointer.X >= valueLeft && _pointer.X < valueLeft + 165;
            ChangeSetting(chosen, down ? -1 : 1, wrap: true);
            return;
        }

        switch (chosen)
        {
            case SettingsMenu.ImportRow:
                _nav.Open(Screen.ImportClassic);
                break;
            case SettingsMenu.ResetRow:
                _confirmReset = true;
                _confirmMenu.Set(0);
                break;
            case SettingsMenu.BackRow:
                _nav.Back();
                break;
        }
    }

    private void UpdateResetConfirm(Controls controls)
    {
        if (Cancelled(controls))
        {
            _confirmReset = false;
            _sounds.Play(Sfx.Cancel);
            return;
        }

        switch (RunMenu(_confirmMenu, controls, ConfirmLayout))
        {
            case 0:
                _confirmReset = false;
                break;
            case 1:
                _save.Data.Settings = new GameSettings();
                ApplySettings(display: true);
                _save.Save();
                _confirmReset = false;
                break;
        }
    }

    /// <summary>Changes a setting, puts it into effect at once and saves it.</summary>
    private void ChangeSetting(int row, int by, bool wrap)
    {
        if (!SettingsMenu.Adjust(_save.Data.Settings, row, by, wrap))
        {
            _sounds.Play(Sfx.Buzz);
            return;
        }

        ApplySettings(display: row is 0 or 1);
        _save.Save();
        _sounds.Play(Sfx.MenuMove);
    }

    private void DrawTitle()
    {
        DrawDrift(0.7f);
        DrawTitleJack();
        _ui.Begin();
        _ui.ShadowText(Font.TitleBold, 104, "PUZZLE BOY", new Vector2(88, 64), _ui["cream"], _ui["pumpkinShade"], spacing: 2, depth: 7);
        _ui.Text(Font.Title, 30, "COMMUNITY EDITION", new Vector2(96, 196), _ui["bandOnDark"], spacing: 5);
        DrawMenu(_titleMenu, TitleItems, TitleMenu, 30);

        // Esc on the title only points at Quit, so the hints don't offer it as Back.
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Select")], 640, 636, _ui["cream"], 17);
        _ui.Text(Font.Body, 15, "A fan project inspired by Puzzle Boy. Not affiliated with or endorsed by ATLUS or SEGA.", new Vector2(640, 690), _ui["cream"] * 0.6f, 0.5f);
        _ui.End();
    }

    private void DrawStageSelect()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("STAGE SELECT");
        var layout = SelectMenu;
        for (var row = 0; row < SelectRows && _selectScroll + row < _selectable.Count; row++)
        {
            var i = _selectScroll + row;
            var (pack, index) = _selectable[i];
            var top = layout.Top + (row * layout.Spacing);
            var focused = i == _selectMenu.Focus;
            if (focused)
            {
                FocusBar(layout, top);
            }

            var status = StatusOf(_save.Data, pack.Stages, index);
            var ink = focused ? _ui["ink"] : status == StageStatus.Locked ? _ui["cream"] * 0.55f : _ui["cream"] * 0.92f;
            StatusIcon(status, layout.Left + 14, top + 13, 24, ink);
            var number = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ui.Text(Font.BodyBold, 22, number, new Vector2(layout.Left + 72, top + 12), ink, 1);
            _ui.Text(Font.BodyBold, 22, pack.Stages[index].Title, new Vector2(layout.Left + 86, top + 12), ink);
            _ui.Text(Font.Title, 16, StatusWord(status), new Vector2(layout.Left + layout.Width - 16, top + 16), ink, 1, 1);
        }

        // Chevrons say there's more above or below; the counter says where the focus is.
        var middle = layout.Left + (layout.Width / 2);
        if (_selectScroll > 0)
        {
            Chevron(middle, layout.Top - 10, up: true);
        }

        if (_selectScroll + SelectRows < _selectable.Count)
        {
            Chevron(middle, layout.Top + (SelectRows * layout.Spacing) + 6, up: false);
        }

        _ui.Text(Font.Title, 16, $"{_selectMenu.Focus + 1} / {_selectable.Count}", new Vector2(layout.Left + layout.Width, layout.Top + (SelectRows * layout.Spacing) + 4), _ui["cream"] * 0.7f, 1, 1);
        DrawStageDetails();
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Play"), ("Esc", "Back")], 640, 660, _ui["cream"], 17);
        _ui.End();
    }

    /// <summary>The focused stage's card: its state, a preview of how it starts and its best clear.</summary>
    private void DrawStageDetails()
    {
        var (pack, index) = _selectable[_selectMenu.Focus];
        var stage = pack.Stages[index];
        var status = StatusOf(_save.Data, pack.Stages, index);
        var (x, y, width, height) = (690f, 150f, 520f, 460f);
        _ui.Panel(x, y, width, height, _ui["ink"] * 0.78f);
        _ui.Text(Font.Title, 16, $"{pack.Title.ToUpperInvariant()}  ·  STAGE {index + 1} OF {pack.Stages.Count}", new Vector2(x + 24, y + 18), _ui["bandOnDark"], spacing: 1);
        _ui.Text(Font.BodyBold, 30, stage.Title, new Vector2(x + 24, y + 40), _ui["cream"]);
        StatusIcon(status, x + 24, y + 88, 22, _ui["cream"]);
        _ui.Text(Font.BodyBold, 19, status switch
        {
            StageStatus.Cleared => "Cleared",
            StageStatus.Open => "Not cleared yet",
            _ => "Locked",
        }, new Vector2(x + 54, y + 88), _ui["cream"]);

        var (px, py, pw, ph) = (x + 24, y + 124, width - 48, 260f);
        _ui.Panel(px, py, pw, ph, _ui["night"]);
        if (status == StageStatus.Locked)
        {
            StatusIcon(StageStatus.Locked, px + (pw / 2) - 24, py + (ph / 2) - 58, 48, _ui["cream"] * 0.6f);
            _ui.Text(Font.Body, 20, $"Clear “{pack.Stages[index - 1].Title}” to open it.", new Vector2(px + (pw / 2), py + (ph / 2) + 4), _ui["cream"] * 0.8f, 0.5f);
        }
        else
        {
            DrawPreview(stage.Stage, px + 8, py + 8, pw - 16, ph - 16);
        }

        var best = _save.Data.Stages.GetValueOrDefault(stage.Id);
        var line = best is { BestMoves: { } moves, BestTime: { } time }
            ? $"Best: {moves} {(moves == 1 ? "move" : "moves")}  ·  {FormatTime(time)}"
            : best is { Completed: true } ? "Best: not recorded" : "Best: none yet";
        _ui.Text(Font.BodyBold, 21, line, new Vector2(x + 24, y + 404), _ui["cream"]);
    }

    /// <summary>
    /// A top-down picture of how <paramref name="stage"/> starts, fitted into the box: walls, floor, holes, blocks in their
    /// group's color and pattern, swivels, the goal and the characters, with +y up as in play. It shows nothing of a
    /// solution.
    /// </summary>
    private void DrawPreview(Stage stage, float x, float y, float width, float height)
    {
        var board = new Board(stage);
        var (minX, minY, maxX, maxY) = BoardRenderer.PlayBounds(board);
        (minX, minY, maxX, maxY) = (Math.Max(0, minX - 1), Math.Max(0, minY - 1), Math.Min(Stage.Width - 1, maxX + 1), Math.Min(Stage.Height - 1, maxY + 1));
        var cell = MathF.Floor(Math.Min(Math.Min(width / (maxX - minX + 1), height / (maxY - minY + 1)), 30));
        var left = x + ((width - ((maxX - minX + 1) * cell)) / 2);
        var top = y + ((height - ((maxY - minY + 1) * cell)) / 2);
        var (floor, wall) = PreviewTones(_save.Data.Settings.HighContrast);
        Vector2 At(int cx, int cy) => new(left + ((cx - minX) * cell), top + ((maxY - cy) * cell));

        for (var cy = minY; cy <= maxY; cy++)
        {
            for (var cx = minX; cx <= maxX; cx++)
            {
                var p = At(cx, cy);
                DrawTile(Tile.FromCell(board[cx, cy]), p.X, p.Y, cell, floor, wall, _save.Data.Settings.HighContrast || (cx + cy) % 2 == 0);
            }
        }

        var goal = At(board.Goal.X, board.Goal.Y);
        DrawGoal(goal.X, goal.Y, cell);
        foreach (var character in board.Characters.Where(c => c.Visible))
        {
            var p = At(character.X, character.Y);
            DrawMiniJack(p.X, p.Y, cell);
        }
    }

    /// <summary>The floor and wall colors of a top-down picture of a stage, starker in high contrast.</summary>
    private (Color Floor, Color Wall) PreviewTones(bool highContrast) =>
        (Color.Lerp(_ui["hatOnDark"], _ui["cream"], highContrast ? 0.66f : 0.5f), highContrast ? _ui["ink"] : Color.Lerp(_ui["hat"], _ui["night"], 0.3f));

    /// <summary>
    /// <paramref name="tile"/> seen from above in a square <paramref name="cell"/> wide: wall, floor (the darker check
    /// unless <paramref name="plainFloor"/>), a hole, and a block or swivel part in its group's color and pattern. A part
    /// over a hole leaves a black rim.
    /// </summary>
    private void DrawTile(Tile tile, float x, float y, float cell, Color floor, Color wall, bool plainFloor)
    {
        if (tile.Kind == CellKind.Wall)
        {
            _ui.Rect(x, y, cell, cell, wall);
            return;
        }

        _ui.Rect(x, y, cell, cell, plainFloor ? floor : Color.Lerp(floor, _ui["hatOnDark"], 0.15f));
        if (tile.Kind == CellKind.Hole || tile.OverHole)
        {
            _ui.Rect(x + 2, y + 2, cell - 4, cell - 4, Color.Black);
        }

        var inset = tile.OverHole ? MathF.Round(cell * 0.16f) : 0;
        var color = _board.GroupColor(Math.Max(1, tile.Group));
        static Rectangle Source(int index) => new(index * Atlas.TileSize, 0, Atlas.TileSize, Atlas.TileSize);
        switch (tile.Kind)
        {
            case CellKind.Block:
                _ui.Sprite(_board.AtlasTexture, Source(BoardRenderer.GroupPattern(tile.Group)), x + 1 + inset, y + 1 + inset, cell - 2 - (2 * inset), cell - 2 - (2 * inset), color);
                break;
            case CellKind.SwivelArm:
                _ui.Sprite(_board.AtlasTexture, Source(BoardRenderer.GroupPattern(tile.Group)), x + 4 + inset, y + 4 + inset, cell - 8 - (2 * inset), cell - 8 - (2 * inset), color);
                break;
            case CellKind.SwivelCenter:
                _ui.Sprite(_board.AtlasTexture, Source(Atlas.Pivot), x + 2 + inset, y + 2 + inset, cell - 4 - (2 * inset), cell - 4 - (2 * inset), Color.Lerp(color, _ui["ink"], 0.3f));
                break;
        }
    }

    /// <summary>The goal seen from above in a square <paramref name="cell"/> wide.</summary>
    private void DrawGoal(float x, float y, float cell)
    {
        _ui.Panel(x + (cell * 0.15f), y + (cell * 0.15f), cell * 0.7f, cell * 0.7f, _ui["ember"]);
        _ui.Panel(x + (cell * 0.32f), y + (cell * 0.32f), cell * 0.36f, cell * 0.36f, _ui["pumpkinShade"]);
    }

    /// <summary>Pixel Jack in miniature, a pumpkin under a hat, in a square <paramref name="cell"/> wide.</summary>
    private void DrawMiniJack(float x, float y, float cell)
    {
        _ui.Panel(x + (cell * 0.18f), y + (cell * 0.36f), cell * 0.64f, cell * 0.52f, _ui["pumpkin"]);
        _ui.Rect(x + (cell * 0.3f), y + (cell * 0.12f), cell * 0.4f, cell * 0.26f, _ui["hatOnDark"]);
        _ui.Rect(x + (cell * 0.14f), y + (cell * 0.32f), cell * 0.72f, cell * 0.08f, _ui["band"]);
    }

    /// <summary>The word beside a stage's icon, so its state never depends on color or shape alone.</summary>
    private static string StatusWord(StageStatus status) => status switch
    {
        StageStatus.Cleared => "CLEARED",
        StageStatus.Open => "NEW",
        _ => "LOCKED",
    };

    /// <summary>A stage state's icon in a square of <paramref name="size"/>: a check, an open ring or a padlock.</summary>
    private void StatusIcon(StageStatus status, float x, float y, float size, Color color)
    {
        var s = size;
        switch (status)
        {
            case StageStatus.Cleared:
                _ui.Line(new Vector2(x + (0.1f * s), y + (0.55f * s)), new Vector2(x + (0.4f * s), y + (0.85f * s)), 0.16f * s, color);
                _ui.Line(new Vector2(x + (0.34f * s), y + (0.85f * s)), new Vector2(x + (0.92f * s), y + (0.18f * s)), 0.16f * s, color);
                break;
            case StageStatus.Open:
                for (var i = 0; i < 12; i++)
                {
                    var (a, b) = (i * MathF.Tau / 12, (i + 1) * MathF.Tau / 12);
                    var c = new Vector2(x + (s / 2), y + (s / 2));
                    _ui.Line(c + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * s * 0.36f), c + (new Vector2(MathF.Cos(b), MathF.Sin(b)) * s * 0.36f), 0.12f * s, color);
                }

                break;
            default:
                _ui.Rect(x + (0.26f * s), y + (0.08f * s), 0.12f * s, 0.4f * s, color);
                _ui.Rect(x + (0.62f * s), y + (0.08f * s), 0.12f * s, 0.4f * s, color);
                _ui.Rect(x + (0.26f * s), y + (0.04f * s), 0.48f * s, 0.12f * s, color);
                _ui.Rect(x + (0.14f * s), y + (0.44f * s), 0.72f * s, 0.52f * s, color);
                break;
        }
    }

    private void Chevron(float x, float y, bool up)
    {
        var (tip, foot) = up ? (y, y + 8) : (y + 8, y);
        _ui.Line(new Vector2(x - 12, foot), new Vector2(x, tip), 3, _ui["cream"] * 0.7f);
        _ui.Line(new Vector2(x, tip), new Vector2(x + 12, foot), 3, _ui["cream"] * 0.7f);
    }

    private void DrawSettings()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("SETTINGS");
        var settings = _save.Data.Settings;
        var layout = SettingsLayout;
        var mode = Microsoft.Xna.Framework.Graphics.GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        for (var row = 0; row < SettingsMenu.Labels.Length; row++)
        {
            var top = layout.Top + (row * layout.Spacing);
            var focused = row == _settingsMenu.Focus;
            if (focused)
            {
                FocusBar(layout, top);
            }

            var ink = focused ? _ui["ink"] : _ui["cream"] * (row == SettingsMenu.ImportRow ? 0.6f : 0.92f);
            var textTop = top + ((layout.Spacing - 26) / 2);
            _ui.Text(Font.BodyBold, 21, SettingsMenu.Labels[row], new Vector2(layout.Left + 36, textTop), ink);
            if (row == SettingsMenu.ImportRow)
            {
                // Classic import comes with C4.
                SoonTag(layout, row, ink);
            }

            if (!SettingsMenu.HasValue(row))
            {
                continue;
            }

            var right = layout.Left + layout.Width - 24;
            var value = SettingsMenu.Value(settings, row, (mode.Width, mode.Height));
            if (SettingsMenu.IsSlider(row))
            {
                var level = row == 3 ? settings.MusicVolume : settings.SfxVolume;
                var (barX, barY) = (right - 290, top + (layout.Spacing / 2) - 5);
                _ui.Panel(barX, barY, 200, 10, focused ? _ui["ink"] * 0.35f : _ui["cream"] * 0.25f);
                var fill = focused ? _ui["ink"] : settings.Mute ? _ui["cream"] * 0.5f : _ui["ember"];
                _ui.Panel(barX, barY, Math.Max(10, 200 * level), 10, fill);
            }

            _ui.Text(Font.BodyBold, 21, focused ? $"<  {value}  >" : value, new Vector2(right, textTop), ink, 1);
        }

        if (_sounds.Silent)
        {
            _ui.Text(Font.Body, 16, "No audio device was found, so the game is silent.", new Vector2(640, 626), _ui["ember"], 0.5f);
        }

        _ui.Hints([("Up Down", "Choose"), ("Left Right", "Change"), ("Enter", "Select"), ("Esc", "Back")], 640, 660, _ui["cream"], 17);
        if (_confirmReset)
        {
            _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
            _ui.Panel(400, 250, 480, 260, _ui["hat"] * 0.97f);
            _ui.Text(Font.TitleBold, 28, "RESET SETTINGS?", new Vector2(640, 276), _ui["cream"], 0.5f, 2);
            _ui.Text(Font.Body, 19, "Every setting goes back to its default.", new Vector2(640, 326), _ui["cream"], 0.5f);
            _ui.Text(Font.Body, 19, "Your progress is kept.", new Vector2(640, 350), _ui["cream"], 0.5f);
            DrawMenu(_confirmMenu, ["Cancel", "Reset"], ConfirmLayout, 24);
        }

        _ui.End();
    }

    private void DrawCredits()
    {
        DrawDrift(0.4f);
        _ui.Begin();
        Heading("CREDITS");
        // Wide lines and tight sections leave room for UI scale 120% above Back.
        var (x, width) = (120f, 1040f);
        var y = 140f;
        void Section(string title, string text)
        {
            _ui.Text(Font.Title, 16, title, new Vector2(x, y), _ui["ember"], spacing: 2);
            y += 24;
            y += _ui.Wrapped(Font.Body, 19, text, new Vector2(x, y), width, _ui["cream"], 1.3f) + 10;
        }

        Section("PUZZLE BOY COMMUNITY EDITION", "Game, stages, Pixel Jack, music and sound by the Puzzle Boy Community authors. Code and original content under the Apache License 2.0.");
        Section("FONTS", "Atkinson Hyperlegible by the Braille Institute of America (Elliott Scott, Megan Eiswerth, Linus Boman, Theodore Petrosky).\nSilkscreen by Jason Kottke. Both under the SIL Open Font License 1.1.");
        Section("COLORS", "Block colors from the Okabe-Ito colorblind-safe palette.");
        Section("LIBRARIES", "MonoGame (Ms-PL), SDL2 and FontStashSharp (zlib), OpenAL Soft (LGPL 2), StbTrueTypeSharp and StbImageSharp (public domain), NVorbis and Cyotek.Drawing.BitmapFont (MIT). Their notices are in THIRD_PARTY_NOTICES.txt beside the game.");
        Section("INSPIRATION", Notice);
        DrawMenu(_backMenu, ["Back"], CreditsBack, 26);
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

        var session = _session!;
        _ui.Panel(430, 132, 420, 470, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 40, "PAUSED", new Vector2(640, 156), _ui["cream"], 0.5f, 2);
        var moves = session.Moves == 1 ? "1 move" : $"{session.Moves} moves";
        _ui.Text(Font.Body, 19, $"{session.Stage.Title}  ·  {moves}  ·  {FormatTime(session.ElapsedTicks / 30.0)[..^2]}", new Vector2(640, 214), _ui["bandOnDark"], 0.5f);
        DrawMenu(_pauseMenu, PauseItems, PauseMenu, 26);
        _ui.Hints([("Arrows", "Choose"), ("Enter", "Select"), ("Esc", "Resume")], 640, 680, _ui["cream"], 16);
    }

    private void DrawHelp()
    {
        _ui.Panel(200, 56, 880, 604, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 36, "HOW TO PLAY", new Vector2(640, 80), _ui["cream"], 0.5f, 2);
        var height = _ui.Wrapped(Font.Body, 20, "Lead Pixel Jack to the glowing goal. Walk into a block to push it; it moves only if all of it can. Push a block entirely over holes and it drops in, making floor. Push a swivel's arm and it turns around its center. Undo takes back your last push or turn, or a restart.", new Vector2(250, 134), 780, _ui["cream"]);
        (string Keys, string Pad, string Action)[] rows =
        [
            ("Arrows / WASD", "D-pad / stick", "Move (hold to keep walking)"),
            ("Q  E", "LB  RB", "Turn the view"),
            ("V", "Y", "Overhead view"),
            ("Z / Backspace", "B", "Undo"),
            ("R", "Back", "Restart"),
            ("C", "X", "Switch character"),
            ("Esc / P", "Start", "Pause"),
            ("Enter  Esc", "A  B", "Choose and go back in menus"),
        ];
        var y = 134 + height + 8;
        _ui.Text(Font.Title, 15, "KEYBOARD", new Vector2(250, y), _ui["ember"], spacing: 2);
        _ui.Text(Font.Title, 15, "CONTROLLER", new Vector2(480, y), _ui["ember"], spacing: 2);
        y += 26;
        foreach (var (keys, pad, action) in rows)
        {
            _ui.Text(Font.BodyBold, 19, keys, new Vector2(250, y), _ui["cream"]);
            _ui.Text(Font.BodyBold, 19, pad, new Vector2(480, y), _ui["cream"]);
            _ui.Text(Font.Body, 19, action, new Vector2(680, y), _ui["cream"] * 0.85f);
            y += 19 * _ui.TextScale * 1.45f;
        }

        if (_session?.Stage.Hint is { } hint)
        {
            _ui.Text(Font.Title, 15, "HINT", new Vector2(250, y + 8), _ui["ember"], spacing: 2);
            _ui.Wrapped(Font.Body, 19, hint, new Vector2(330, y + 4), 700, _ui["cream"]);
        }

        _ui.Hints([("Enter", "Close")], 640, 680, _ui["cream"], 16);
    }

    private void Heading(string title)
    {
        _ui.ShadowText(Font.TitleBold, 56, title, new Vector2(640, 52), _ui["cream"], _ui["pumpkinShade"], 0.5f, 2, 5);
    }

    /// <summary>The ember bar behind a focused item whose row starts at <paramref name="top"/>.</summary>
    private void FocusBar(MenuLayout layout, float top)
    {
        var glow = 0.85f + (0.15f * MathF.Sin(Motion * 0.15f));
        _ui.Panel(layout.Left - 6, top + 4, layout.Width + 12, layout.Spacing - 8, _ui["pumpkinShade"] * glow);
        _ui.Panel(layout.Left, top + 4, layout.Width, layout.Spacing - 8, _ui["ember"]);
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
                FocusBar(layout, top);
                _ui.Rect(layout.Left + 14, top + (layout.Spacing / 2) - 5, 10, 10, _ui["ink"]);
            }

            var color = focused ? _ui["ink"] : _ui["cream"] * 0.9f;
            _ui.Text(Font.BodyBold, size, labels[i], new Vector2(layout.Left + 36, top + ((layout.Spacing - (size * _ui.TextScale * 1.2f)) / 2)), color);
        }
    }

    /// <summary>A "SOON" tag at the right of item <paramref name="index"/> of a menu laid out as <paramref name="layout"/>.</summary>
    private void SoonTag(MenuLayout layout, int index, Color color) =>
        _ui.Text(Font.Title, 16, "SOON", new Vector2(layout.Left + layout.Width - 16, layout.Top + (index * layout.Spacing) + (layout.Spacing / 2) - (8 * _ui.TextScale)), color, 1, 1);

    /// <summary>
    /// Blocks drifting slowly through the dark, behind the menus; <paramref name="strength"/> dims them. They stand still
    /// when the player reduces motion, and high contrast leaves them out.
    /// </summary>
    private void DrawDrift(float strength)
    {
        if (_save.Data.Settings.HighContrast)
        {
            return;
        }

        var ticks = Motion;
        var mesh = new Mesh();
        for (var i = 0; i < 16; i++)
        {
            var hash = (uint)((i + 3) * 2246822519u);
            var x = ((hash % 1000) / 1000f * 15) - 7.5f;
            var z = -4 - ((hash / 1000 % 1000) / 1000f * 10);
            var speed = 0.006f + (hash % 7 * 0.002f);
            var y = ((((hash % 997) / 997f * 12) + (ticks * speed)) % 12) - 6;
            var spin = Matrix.CreateRotationX((ticks * speed * 0.7f) + i) * Matrix.CreateRotationY((ticks * speed) + (i * 2));
            var group = i + 1;
            mesh.AddBox(new(-0.4f, -0.4f, -0.4f), new(0.4f, 0.4f, 0.4f), _board.GroupColor(group) * strength, BoardRenderer.GroupPattern(group), BoardRenderer.GroupPattern(group), spin * Matrix.CreateTranslation(x, y, z));
        }

        var view = Matrix.CreateLookAt(new Vector3(0, 0.45f, 4.2f), new Vector3(0, 0.45f, 0), Vector3.Up);
        _board.DrawMesh(mesh, Matrix.Identity, view, Projection(40), new Color(0.75f, 0.75f, 0.8f), _ui["night"]);
    }

    private void DrawTitleJack()
    {
        var ticks = Motion;
        var view = Matrix.CreateLookAt(new Vector3(0, 0.45f, 4.2f), new Vector3(0, 0.45f, 0), Vector3.Up);
        var bob = MathF.Sin(ticks * 0.08f) * 0.06f;
        var place = Matrix.CreateScale(1.85f) * Matrix.CreateRotationY(MathF.Sin(ticks * 0.025f) * 0.5f) * Matrix.CreateTranslation(1.55f, -0.95f + bob, 0);
        var stand = new Mesh();
        stand.AddBox(new(-0.55f, -0.22f, -0.55f), new(0.55f, 0, 0.55f), Color.Lerp(_ui["hatOnDark"], _ui["cream"], 0.25f), Atlas.Plain, Atlas.Plain, Matrix.Identity, _ui["band"]);
        _board.DrawMesh(stand, place, view, Projection(40), Color.White);
        _board.DrawMesh(_board.Jack, Matrix.CreateScale(1, 1 + (bob * 0.3f), 1) * place, view, Projection(40), Color.White);
    }

    private Matrix Projection(float fieldOfView) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(fieldOfView), GraphicsDevice.Viewport.AspectRatio, 0.05f, 60f);
}
