using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Graphics;

namespace PuzzleBoy.Community;

/// <summary>
/// The Import Classic screen, opened from Settings: the only place the window looks for the player's own copy of
/// Nocturne, and only when they choose one of its actions.
/// </summary>
internal sealed partial class CommunityGame
{
    /// <summary>The Import Classic menu, in order.</summary>
    internal static readonly string[] ClassicItems = ["Find My Steam Install", "Enter Folder Path", "Remove Classic Stages", "Back"];

    // A typed path can be long, but not unbounded.
    private const int MaxClassicPath = 400;

    private static readonly MenuLayout ClassicLayout = new(72, 160, 470, 54);
    private readonly MenuList _classicMenu = new(ClassicItems.Length);
    private string? _classicStatus;
    private string? _classicPath;
    private bool _classicConfirmRemove;
    private (int Original, int Bonus) _classicCounts;

    private void OpenImportClassic()
    {
        _classicMenu.Set(0);
        (_classicStatus, _classicPath, _classicConfirmRemove) = (null, null, false);
        CountClassic();
        _nav.Open(Screen.ImportClassic);
    }

    /// <summary>Counts the imported stages in each classic campaign.</summary>
    private void CountClassic()
    {
        var packs = ClassicStages.Load(_save.Folder, []);
        int Count(string id) => packs.FirstOrDefault(p => p.PackId == id)?.Stages.Count ?? 0;
        _classicCounts = (Count(ClassicStages.OriginalPackId), Count(ClassicStages.BonusPackId));
    }

    private void UpdateImportClassic(Controls controls)
    {
        if (_classicPath is not null)
        {
            UpdateClassicPath(controls);
            return;
        }

        if (_classicConfirmRemove)
        {
            var pick = Cancelled(controls) ? 0 : RunMenu(_confirmMenu, controls, ConfirmLayout);
            if (pick == 1)
            {
                _classicStatus = ClassicStages.Remove(_save.Folder) ?? "The classic stages were removed from your data folder. Your progress on them is kept.";
                CountClassic();
            }

            _classicConfirmRemove = pick is null;
            return;
        }

        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        switch (RunMenu(_classicMenu, controls, ClassicLayout))
        {
            case 0:
                RunClassicImport(null);
                break;
            case 1:
                (_classicPath, _classicStatus) = ("", null);
                _typed.Clear();
                break;
            case 2 when _classicCounts == (0, 0):
                _classicStatus = "There are no classic stages to remove.";
                _sounds.Play(Sfx.Buzz);
                break;
            case 2:
                _classicConfirmRemove = true;
                _confirmMenu.Set(0);
                break;
            case 3:
                _nav.Back();
                break;
        }
    }

    /// <summary>A tick of typing the game's folder: Enter imports from it, Esc gives up.</summary>
    private void UpdateClassicPath(Controls controls)
    {
        bool Key(Keys key) => controls.Keys.IsKeyDown(key) && !controls.PreviousKeys.IsKeyDown(key);
        foreach (var c in _typed.ToString().Where(c => !char.IsControl(c)))
        {
            if (_classicPath!.Length < MaxClassicPath)
            {
                _classicPath += c;
            }
        }

        _typed.Clear();
        if (Key(Keys.Back) && _classicPath!.Length > 0)
        {
            _classicPath = _classicPath[..^1];
        }

        if (Key(Keys.Escape))
        {
            _classicPath = null;
            _sounds.Play(Sfx.Cancel);
        }
        else if (Key(Keys.Enter))
        {
            var folder = _classicPath!;
            _classicPath = null;
            RunClassicImport(folder);
        }
    }

    /// <summary>
    /// Imports from <paramref name="folder"/>, or from a search beside the game and in the Steam libraries when null.
    /// shortcut: it runs on the game's thread, a moment's pause for one small bundle; move it to a task if it ever reads more.
    /// </summary>
    private void RunClassicImport(string? folder)
    {
        var result = folder is null ? ClassicImport.FromSteam(_save.Folder) : ClassicImport.FromFolder(folder, _save.Folder);
        _classicStatus = result.Message;
        CountClassic();
        _sounds.Play(result.Imported ? Sfx.Goal : Sfx.Buzz);
    }

    private void DrawImportClassic()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("IMPORT CLASSIC");
        DrawMenu(_classicMenu, ClassicItems, ClassicLayout, 24);

        var (x, y, width) = (600f, 150f, 610f);
        _ui.Panel(x, y, width, 460, _ui["ink"] * 0.78f);
        _ui.Text(Font.Title, 16, "OPTIONAL", new Vector2(x + 24, y + 18), _ui["bandOnDark"], spacing: 1);
        var below = y + 48 + _ui.Wrapped(Font.Body, 19, "This needs your own copy of Shin Megami Tensei III Nocturne HD Remaster. It copies only the Puzzle Boy stage files from it into your data folder, so the original's 20 stages and 23 unused bonus stages show up in Stage Select. Nothing from the game ships with Community Edition, and the classic stages are never shared or exported.", new Vector2(x + 24, y + 44), width - 48, _ui["cream"]);
        var (original, bonus) = _classicCounts;
        var imported = (original, bonus) == (0, 0) ? "Nothing imported yet." : $"Imported: {original} original and {bonus} bonus stages.";
        _ui.Text(Font.BodyBold, 19, imported, new Vector2(x + 24, below), _ui["cream"]);
        if (_classicStatus is not null)
        {
            _ui.Wrapped(Font.Body, 18, _classicStatus, new Vector2(x + 24, below + 36), width - 48, _ui["ember"]);
        }

        _ui.Hints([("Up Down", "Choose"), ("Enter", "Select"), ("Esc", "Back")], 640, 660, _ui["cream"], 17);
        if (_classicPath is not null)
        {
            _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
            _ui.Panel(240, 220, 800, 300, _ui["hat"] * 0.97f);
            _ui.Text(Font.TitleBold, 28, "GAME FOLDER", new Vector2(640, 246), _ui["cream"], 0.5f, 2);
            _ui.Text(Font.Body, 19, "Type the folder that holds smt3hd.exe, or a copy of its smt3hd_Data folder:", new Vector2(270, 296), _ui["cream"]);
            _ui.Panel(270, 332, 740, 112, _ui["night"]);
            DrawPath(_classicPath + (_ticks / 15 % 2 == 0 ? "_" : " "), new Vector2(286, 344), 708, 19, _ui["ember"]);
            _ui.Hints([("Enter", "Import"), ("Esc", "Cancel")], 640, 480, _ui["cream"], 16);
        }

        if (_classicConfirmRemove)
        {
            _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
            _ui.Panel(400, 250, 480, 260, _ui["hat"] * 0.97f);
            _ui.Text(Font.TitleBold, 28, "REMOVE CLASSIC?", new Vector2(640, 276), _ui["cream"], 0.5f, 2);
            _ui.Text(Font.Body, 19, "The imported stage files are deleted.", new Vector2(640, 326), _ui["cream"], 0.5f);
            _ui.Text(Font.Body, 19, "Your progress on them is kept.", new Vector2(640, 350), _ui["cream"], 0.5f);
            DrawMenu(_confirmMenu, ["Cancel", "Remove"], ConfirmLayout, 24);
        }

        _ui.End();
    }
}
