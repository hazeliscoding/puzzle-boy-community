using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Graphics;

namespace PuzzleBoy.Community;

/// <summary>
/// The Classic Nocturne screen, opened from Settings: the only place the window looks for the player's own copy of
/// Nocturne or a classic pack, and only when they choose one of its actions.
/// </summary>
internal sealed partial class CommunityGame
{
    /// <summary>The Classic Nocturne menu, in order.</summary>
    internal static readonly string[] ClassicItems = ["Import from My Steam Install", "Import from a Folder or Zip", "Export My Classic Pack", "Remove Classic Pack", "Back"];

    /// <summary>What a schema 1 pack's owner is told, once a session in play and on this screen.</summary>
    internal const string ReimportNotice = "Re-import your classic pack to get the Nocturne-styled menus.";

    // A typed path can be long, but not unbounded.
    private const int MaxClassicPath = 400;

    private static readonly MenuLayout ClassicLayout = new(72, 160, 470, 54);
    private readonly MenuList _classicMenu = new(ClassicItems.Length);
    private string? _classicStatus;
    private string? _classicPath;
    private bool _classicConfirmRemove;
    private string _classicSummary = "";
    private bool _classicHasPack;

    // An import or export waiting for the frame that says it's working to show: it takes a few seconds.
    private Action? _classicWork;
    private bool _classicWorkShown;

    private void OpenImportClassic()
    {
        _classicMenu.Set(0);
        (_classicStatus, _classicPath, _classicConfirmRemove) = (null, null, false);
        DescribeClassic();
        _nav.Open(Screen.ImportClassic);
    }

    /// <summary>
    /// Says what classic pack the game uses, from its manifest: the data folder's, or one beside the game. The files are
    /// checked when they're used.
    /// </summary>
    private void DescribeClassic()
    {
        var folder = ClassicStages.PackFolder(_save.Folder);
        var beside = folder != ClassicStages.Folder(_save.Folder);
        _classicHasPack = Directory.Exists(ClassicStages.Folder(_save.Folder));
        if (!ClassicPack.IsIn(folder))
        {
            var legacy = ClassicStages.Load(_save.Folder, []).Sum(p => p.Stages.Count);
            _classicSummary = legacy > 0
                ? $"You have {legacy} classic stages from an earlier version, but no classic pack. Import again to get the Classic theme; your progress is kept."
                : "No classic pack yet.";
            return;
        }

        var place = beside ? "The classic pack in the classic-pack folder beside the game is used read-only; importing one puts it in your data folder instead. " : "";
        if (ClassicPack.Open(folder, checkHashes: false, out var problem) is not { } pack)
        {
            _classicSummary = $"{place}Your classic pack can't be used. {problem}";
            return;
        }

        var made = pack.Created is { Length: >= 10 } created ? $" on {created[..10]}" : "";
        var where = pack.Origin == ClassicPack.Licensed
            ? $"Licensed pack from {pack.Licensor}, under {pack.License}{made}."
            : $"Made from your own copy{made}{(pack.CreatedFrom is { } from ? $", from {from}" : "")}.";
        _classicSummary = $"{place}{where} {pack.Size / 1e6:0.0} MB.{(pack.HasUi ? "" : $" {ReimportNotice}")}";
    }

    private void UpdateImportClassic(Controls controls)
    {
        if (_classicWork is { } work)
        {
            if (_classicWorkShown)
            {
                _classicWork = null;
                work();
            }

            return;
        }

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
                _classicStatus = ClassicStages.Remove(_save.Folder) ?? "Your classic pack was removed from your data folder. Your progress on the classic stages is kept.";
                DescribeClassic();
                RequestSkin();
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
            case 2:
                Working("Exporting your classic pack…", () =>
                {
                    var path = ClassicImport.Export(_save.Folder, out var message);
                    _classicStatus = message;
                    _sounds.Play(path is null ? Sfx.Buzz : Sfx.Goal);
                });
                break;
            case 3 when !_classicHasPack:
                _classicStatus = ClassicPack.IsIn(ClassicStages.PackFolder(_save.Folder))
                    ? "The classic pack beside the game is never changed from here. Delete its classic-pack folder yourself to stop using it."
                    : "There's no classic pack to remove.";
                _sounds.Play(Sfx.Buzz);
                break;
            case 3:
                _classicConfirmRemove = true;
                _confirmMenu.Set(0);
                break;
            case 4:
                _nav.Back();
                break;
        }
    }

    /// <summary>A tick of typing a folder or zip: Enter imports from it, Esc gives up.</summary>
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
    /// Imports from <paramref name="folder"/> (a game folder, a pack folder or a pack zip), or from a search beside the game
    /// and in the Steam libraries when null. It runs after a frame that says so has shown.
    /// shortcut: it runs on the game's thread, a few seconds' pause behind that frame; move it to a task if it ever grows.
    /// </summary>
    private void RunClassicImport(string? folder) =>
        Working("Importing… copying and checking about 120 MB takes a few seconds.", () =>
        {
            var result = folder is null ? ClassicImport.FromSteam(_save.Folder) : ClassicImport.FromFolder(folder, _save.Folder);
            _classicStatus = result.Message;
            DescribeClassic();
            RequestSkin();
            _sounds.Play(result.Imported ? Sfx.Goal : Sfx.Buzz);
        });

    /// <summary>Shows <paramref name="status"/>, then runs <paramref name="work"/> on the next tick after it's been drawn.</summary>
    private void Working(string status, Action work) => (_classicStatus, _classicWork, _classicWorkShown) = (status, work, false);

    private void DrawImportClassic()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("CLASSIC NOCTURNE");
        DrawMenu(_classicMenu, ClassicItems, ClassicLayout, 24);

        var (x, y, width) = (600f, 150f, 610f);
        _ui.Panel(x, y, width, 460, _ui["ink"] * 0.78f);
        _ui.Text(Font.Title, 16, "OPTIONAL  ·  YOUR OWN COPY", new Vector2(x + 24, y + 18), _ui["bandOnDark"], spacing: 1);
        var below = y + 48 + _ui.Wrapped(Font.Body, 18, "The Classic theme plays every stage in the original minigame's look and sound. It needs your own copy of Shin Megami Tensei III Nocturne HD Remaster: import once, and the game copies the bundles it reads into your data folder, with the original's 20 stages and 23 unused bonus stages. Export makes a zip to carry to your other computers. Nothing from Nocturne ships with Community Edition; don't share your pack.", new Vector2(x + 24, y + 44), width - 48, _ui["cream"]);
        below += 8 + _ui.Wrapped(Font.BodyBold, 18, _classicSummary, new Vector2(x + 24, below), width - 48, _ui["cream"]);
        if (_classicStatus is not null)
        {
            _ui.Wrapped(Font.Body, 17, _classicStatus, new Vector2(x + 24, below + 12), width - 48, _ui["ember"]);
        }

        _ui.Hints(MenuHints(("Up Down", "Choose"), ("Enter", "Select"), ("Esc", "Back")), 640, 660, _ui["cream"], 17);
        if (_classicPath is not null)
        {
            _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
            _ui.Panel(240, 220, 800, 300, _ui["hat"] * 0.97f);
            _ui.Text(Font.TitleBold, 28, "FOLDER OR ZIP", new Vector2(640, 246), _ui["cream"], 0.5f, 2);
            _ui.Text(Font.Body, 19, "Type the folder that holds smt3hd.exe, or a classic pack's zip or folder:", new Vector2(270, 296), _ui["cream"]);
            _ui.Panel(270, 332, 740, 112, _ui["night"]);
            DrawPath(_classicPath + (_ticks / 15 % 2 == 0 ? "_" : " "), new Vector2(286, 344), 708, 19, _ui["ember"]);
            _ui.Hints([("Enter", "Import"), ("Esc", "Cancel")], 640, 480, _ui["cream"], 16);
        }

        if (_classicConfirmRemove)
        {
            _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
            _ui.Panel(400, 250, 480, 260, _ui["hat"] * 0.97f);
            _ui.Text(Font.TitleBold, 28, "REMOVE PACK?", new Vector2(640, 276), _ui["cream"], 0.5f, 2);
            _ui.Text(Font.Body, 19, "Your classic pack is deleted.", new Vector2(640, 326), _ui["cream"], 0.5f);
            _ui.Text(Font.Body, 19, "Your progress on it is kept.", new Vector2(640, 350), _ui["cream"], 0.5f);
            DrawMenu(_confirmMenu, ["Cancel", "Remove"], ConfirmLayout, 24);
        }

        _ui.End();
        _classicWorkShown = _classicWork is not null;
    }
}
