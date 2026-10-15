using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Editor;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>
/// The level editor screen: the list of the player's stages, and the canvas that edits one. It only draws and routes
/// input; <see cref="StageEditor"/>, <see cref="EditorView"/> and <see cref="CustomStages"/> hold the logic.
/// </summary>
internal sealed partial class CommunityGame
{
    /// <summary>The canvas menu Esc opens, in order.</summary>
    internal static readonly string[] EditorMenuItems = ["Resume", "Undo", "Redo", "Playtest", "Solve (capped)", "Save", "Rename", "Close Stage"];

    /// <summary>The most positions the editor's Solve visits before it gives up, which takes a few seconds.</summary>
    internal const int EditorSolveCap = 250_000;

    /// <summary>What can be done with a stage in the editor's list, in order.</summary>
    internal static readonly string[] LibraryActions = ["Edit", "Duplicate", "Export as Pack", "Delete", "Cancel"];

    /// <summary>The export panel's fixed rows, in order; the player's stages follow them, each to tick.</summary>
    internal static readonly string[] ExportRows = ["Title", "Creator", "License", "Export", "Back"];

    /// <summary>The longest pack title or creator the export panel takes.</summary>
    internal const int MaxPackText = 60;

    // The export panel shows this many rows and scrolls the rest.
    private const int ExportVisible = 9;

    private static readonly RectangleF CanvasBox = new(272, 72, 600, 588);
    private static readonly MenuLayout EditorMenuLayout = new(490, 196, 300, 50);
    private static readonly MenuLayout ExportLayout = new(300, 132, 680, 42);
    private static readonly MenuLayout ToolLayout = new(24, 104, 224, 32);

    // The group selector's buttons, the hole toggle and the problem list, in virtual pixels.
    private static readonly RectangleF GroupDown = new(24, 432, 40, 56);
    private static readonly RectangleF GroupUp = new(208, 432, 40, 56);
    private static readonly RectangleF NewGroupButton = new(24, 522, 224, 30);
    private static readonly RectangleF HoleToggle = new(24, 560, 224, 30);

    private readonly List<(RectangleF Row, int Problem)> _problemRows = [];
    private readonly MenuList _toolMenu = new(StageEditor.Tools.Length);
    private readonly System.Text.StringBuilder _typed = new();
    private readonly HashSet<string> _exportMarked = [];
    private EditorView _view = new(CanvasBox);
    private StageEditor? _editor;
    private List<CustomStage> _custom = [];
    private MenuList _libraryMenu = new(1);
    private MenuList _overlayMenu = new(1);
    private EditorOverlay _overlay;
    private bool _confirmQuit;
    private bool _quitConfirmed;
    private int _libraryScroll;
    private string? _libraryStatus;
    private (int X, int Y)? _rectAnchor;
    private bool _keyStroke;
    private bool _mouseStroke;
    private bool _mouseRect;
    private (int X, int Y) _lastMouseCell;
    private Vector2? _panFrom;
    private string _titleDraft = "";
    private MouseState _mouse;
    private Task<SolveResult>? _solve;
    private int _solveVersion;
    private string _exportTitle = "Shared Stages";
    private string _exportCreator = "Anonymous";
    private int _exportLicense;
    private bool _exportCreatorField;
    private int _exportScroll;
    private string? _exportStatus;
    private string? _exportPath;

    /// <summary>What sits over the editor and takes its input.</summary>
    private enum EditorOverlay
    {
        None,
        Actions,
        ConfirmDelete,
        Menu,
        ConfirmClose,
        Title,
        Export,
        ExportText,
    }

    /// <summary>The stage open in the editor, or null on its list.</summary>
    internal StageEditor? Editor => _editor;

    /// <summary>True while the unsaved-changes question asks whether to quit, after the window was closed.</summary>
    internal bool AskingToQuit => _overlay == EditorOverlay.ConfirmClose && _confirmQuit;

    /// <summary>The editor's list of the player's stages, as last read.</summary>
    internal IReadOnlyList<CustomStage> CustomList => _custom;

    /// <summary>Opens the editor on its list of the player's stages.</summary>
    private void OpenEditor()
    {
        _editor = null;
        _overlay = EditorOverlay.None;
        _libraryStatus = null;
        RefreshLibrary(null);
        _nav.Open(Screen.Editor);
    }

    /// <summary>Reads the custom folder again and focuses <paramref name="focus"/>, or keeps the focus.</summary>
    private void RefreshLibrary(string? focus)
    {
        var keep = _libraryMenu.Focus;
        _custom = CustomStages.List(_save.Folder);
        _libraryMenu = new MenuList(_custom.Count + 1);
        _libraryMenu.Set(focus is null ? Math.Min(keep, _custom.Count) : _custom.FindIndex(s => s.Name == focus) + 1);
        KeepLibraryVisible();
    }

    private void KeepLibraryVisible()
    {
        var focus = _libraryMenu.Focus;
        _libraryScroll = Math.Clamp(_libraryScroll, Math.Max(0, focus - SelectRows + 1), focus);
        _libraryScroll = Math.Clamp(_libraryScroll, 0, Math.Max(0, _libraryMenu.Count - SelectRows));
    }

    /// <summary>Opens <paramref name="editor"/> on the canvas, zoomed in on what the stage holds, or on the whole grid for a new one.</summary>
    private void OpenCanvas(StageEditor editor)
    {
        _editor = editor;
        _view = new EditorView(CanvasBox);
        if (editor.Bounds() is { } bounds)
        {
            _view.Frame(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
            editor.Cursor = ((bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2);
        }

        _overlay = EditorOverlay.None;
        (_rectAnchor, _keyStroke, _mouseStroke, _mouseRect, _panFrom) = (null, false, false, false, null);
    }

    private void UpdateEditor(Controls controls)
    {
        if (_editor is null)
        {
            UpdateLibrary(controls);
        }
        else
        {
            UpdateCanvas(controls, _editor);
        }
    }

    private void UpdateLibrary(Controls controls)
    {
        var chosen = _libraryMenu.Focus - 1;
        switch (_overlay)
        {
            case EditorOverlay.Actions:
                if (Cancelled(controls))
                {
                    _overlay = EditorOverlay.None;
                    _sounds.Play(Sfx.Cancel);
                    return;
                }

                switch (RunMenu(_overlayMenu, controls, EditorMenuLayout))
                {
                    case 0:
                        if (_custom[chosen].Stage is { } stage)
                        {
                            OpenCanvas(new StageEditor(stage, _custom[chosen].Title, _custom[chosen].Name));
                        }
                        else
                        {
                            _libraryStatus = $"It can't be opened: {_custom[chosen].Problem}";
                            _overlay = EditorOverlay.None;
                            _sounds.Play(Sfx.Buzz);
                        }

                        break;
                    case 1:
                        var copy = CustomStages.Duplicate(_save.Folder, _custom[chosen].Name, out var error);
                        _libraryStatus = error ?? $"Copied as “{_custom[chosen].Title} copy”.";
                        _overlay = EditorOverlay.None;
                        RefreshLibrary(copy);
                        break;
                    case 2:
                        OpenExport(_custom[chosen]);
                        break;
                    case 3:
                        _overlay = EditorOverlay.ConfirmDelete;
                        _overlayMenu = new MenuList(2);
                        break;
                    case 4:
                        _overlay = EditorOverlay.None;
                        break;
                }

                return;
            case EditorOverlay.Export:
                UpdateExport(controls);
                return;
            case EditorOverlay.ExportText:
                UpdateTextEntry(controls, MaxPackText, KeepExportText, EditorOverlay.Export);
                return;
            case EditorOverlay.ConfirmDelete:
                var delete = Cancelled(controls) ? 0 : RunMenu(_overlayMenu, controls, ConfirmLayout);
                if (delete == 0)
                {
                    _overlay = EditorOverlay.None;
                }
                else if (delete == 1)
                {
                    DeleteCustom(_custom[chosen]);
                }

                return;
        }

        if (Cancelled(controls))
        {
            GoBack();
            return;
        }

        var choice = RunMenu(_libraryMenu, controls, SelectMenu, _libraryScroll, SelectRows);
        KeepLibraryVisible();
        if (choice == 0)
        {
            OpenCanvas(new StageEditor());
        }
        else if (choice is not null)
        {
            _overlay = EditorOverlay.Actions;
            _overlayMenu = new MenuList(LibraryActions.Length);
        }
    }

    private void DeleteCustom(CustomStage stage)
    {
        _overlay = EditorOverlay.None;
        if (CustomStages.Delete(_save.Folder, stage.Name) is { } error)
        {
            _libraryStatus = error;
            _sounds.Play(Sfx.Buzz);
            return;
        }

        // A new stage that later takes the same name starts uncleared.
        if (_save.Data.Stages.Remove(stage.Id))
        {
            _save.Save();
        }

        _libraryStatus = $"Deleted “{stage.Title}”.";
        RefreshLibrary(null);
    }

    /// <summary>Opens the export panel with <paramref name="stage"/> ticked and the focus on Export.</summary>
    private void OpenExport(CustomStage stage)
    {
        _exportMarked.Clear();
        _exportMarked.Add(stage.Name);
        (_exportStatus, _exportPath) = (null, null);
        _exportScroll = 0;
        _overlay = EditorOverlay.Export;
        _overlayMenu = new MenuList(ExportRows.Length + _custom.Count);
        _overlayMenu.Set(3);
    }

    private void UpdateExport(Controls controls)
    {
        if (Cancelled(controls))
        {
            _overlay = EditorOverlay.None;
            _sounds.Play(Sfx.Cancel);
            return;
        }

        if ((_menuLeft || _menuRight) && _overlayMenu.Focus == 2)
        {
            _exportLicense = (_exportLicense + (_menuRight ? 1 : -1) + PackArchive.Licenses.Length) % PackArchive.Licenses.Length;
            _sounds.Play(Sfx.MenuMove);
            return;
        }

        var choice = RunMenu(_overlayMenu, controls, ExportLayout, _exportScroll, ExportVisible);
        _exportScroll = Math.Clamp(_exportScroll, Math.Max(0, _overlayMenu.Focus - ExportVisible + 1), _overlayMenu.Focus);
        _exportScroll = Math.Clamp(_exportScroll, 0, Math.Max(0, _overlayMenu.Count - ExportVisible));
        switch (choice)
        {
            case 0 or 1:
                _exportCreatorField = choice == 1;
                _titleDraft = _exportCreatorField ? _exportCreator : _exportTitle;
                _typed.Clear();
                _overlay = EditorOverlay.ExportText;
                break;
            case 2:
                _exportLicense = (_exportLicense + 1) % PackArchive.Licenses.Length;
                break;
            case 3:
                var stages = _custom.Where(s => _exportMarked.Contains(s.Name)).ToList();
                _exportPath = PackArchive.Export(_save.Folder, stages, _exportTitle, _exportCreator, PackArchive.Licenses[_exportLicense], PackArchive.ReadKnown(), PackArchive.Reserved(_manifest.Packs), out var message);
                _exportStatus = message;
                _sounds.Play(_exportPath is null ? Sfx.Buzz : Sfx.Goal);
                break;
            case 4:
                _overlay = EditorOverlay.None;
                break;
            case { } row:
                var stage = _custom[row - ExportRows.Length];
                if (stage.Stage is null)
                {
                    _exportStatus = $"“{stage.Title}” can't be read as a stage, so it can't go in a pack.";
                    _sounds.Play(Sfx.Buzz);
                }
                else if (!_exportMarked.Remove(stage.Name))
                {
                    _exportMarked.Add(stage.Name);
                }

                break;
        }
    }

    private void KeepExportText(string text)
    {
        if (_exportCreatorField)
        {
            _exportCreator = text;
        }
        else
        {
            _exportTitle = text;
        }
    }

    private void UpdateCanvas(Controls controls, StageEditor editor)
    {
        var keys = controls.Keys;
        bool Key(Keys key) => keys.IsKeyDown(key) && !controls.PreviousKeys.IsKeyDown(key);
        bool Pad(Buttons button) => controls.Pad.IsButtonDown(button) && !controls.PreviousPad.IsButtonDown(button);
        if (_solve is { IsCompleted: true } solve)
        {
            // A result for a stage that has changed since would mislead.
            editor.Message = editor.Version == _solveVersion ? StageEditor.SolveMessage(solve.Result, EditorSolveCap) : "The stage changed while it was being solved. Solve it again.";
            _solve = null;
        }

        switch (_overlay)
        {
            case EditorOverlay.Menu:
                UpdateEditorMenu(controls, editor);
                return;
            case EditorOverlay.ConfirmClose:
                var close = Cancelled(controls) ? 0 : RunMenu(_overlayMenu, controls, ConfirmLayout);
                if (close == 0)
                {
                    _overlay = EditorOverlay.None;
                    _confirmQuit = false;
                }
                else if (close == 1 && _confirmQuit)
                {
                    _quitConfirmed = true;
                    Exit();
                }
                else if (close == 1)
                {
                    CloseCanvas();
                }

                return;
            case EditorOverlay.Title:
                UpdateTextEntry(controls, StageEditor.MaxTitle, title => editor.SetTitle(title), EditorOverlay.None);
                return;
        }

        if (Key(Keys.Escape) || Pad(Buttons.Start) || Pad(Buttons.B))
        {
            editor.EndStroke();
            _keyStroke = _mouseStroke = false;
            if (_rectAnchor is not null)
            {
                _rectAnchor = null;
                _mouseRect = false;
                _sounds.Play(Sfx.Cancel);
                return;
            }

            _overlay = EditorOverlay.Menu;
            _overlayMenu = new MenuList(EditorMenuItems.Length);
            _sounds.Play(Sfx.Confirm);
            return;
        }

        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        if (ctrl)
        {
            if (Key(Keys.S))
            {
                SaveStage(editor);
            }
            else if (Key(Keys.Z) && !shift)
            {
                Undo(editor, redo: false);
            }
            else if (Key(Keys.Y) || (Key(Keys.Z) && shift))
            {
                Undo(editor, redo: true);
            }

            return;
        }

        UpdateCanvasKeys(controls, editor, Key, Pad, shift);
        UpdateCanvasMouse(editor, shift);
    }

    private void UpdateCanvasKeys(Controls controls, StageEditor editor, Func<Keys, bool> key, Func<Buttons, bool> pad, bool shift)
    {
        for (var i = 0; i < StageEditor.Tools.Length; i++)
        {
            if (key(Keys.D1 + i) || key(Keys.NumPad1 + i))
            {
                ChooseTool(editor, StageEditor.Tools[i]);
            }
        }

        if (pad(Buttons.LeftShoulder) || pad(Buttons.RightShoulder))
        {
            var next = (Array.IndexOf(StageEditor.Tools, editor.Tool) + (pad(Buttons.LeftShoulder) ? -1 : 1) + StageEditor.Tools.Length) % StageEditor.Tools.Length;
            ChooseTool(editor, StageEditor.Tools[next]);
        }

        if (key(Keys.N))
        {
            _sounds.Play(editor.NewGroup() ? Sfx.MenuMove : Sfx.Buzz);
        }

        if (key(Keys.OemOpenBrackets) || key(Keys.OemCloseBrackets))
        {
            editor.ChangeNumber(key(Keys.OemOpenBrackets) ? -1 : 1);
            _sounds.Play(Sfx.MenuMove);
        }

        if (key(Keys.G))
        {
            editor.Pick(editor.Cursor.X, editor.Cursor.Y);
            _sounds.Play(Sfx.MenuMove);
        }

        if (key(Keys.H))
        {
            editor.OverHole = !editor.OverHole;
            _sounds.Play(Sfx.MenuMove);
        }

        if (key(Keys.T))
        {
            OpenTitleEntry(editor);
            return;
        }

        if (key(Keys.P) || key(Keys.F5) || pad(Buttons.Y))
        {
            Playtest(editor);
            return;
        }

        if (key(Keys.Tab) || pad(Buttons.X))
        {
            _sounds.Play(editor.NextProblem(shift ? -1 : 1) ? Sfx.MenuMove : Sfx.Buzz);
            _view.Follow(editor.Cursor.X, editor.Cursor.Y);
        }

        if (key(Keys.OemPlus) || key(Keys.Add) || key(Keys.OemMinus) || key(Keys.Subtract))
        {
            var corner = _view.CellCorner(editor.Cursor.X, editor.Cursor.Y) + new Vector2(_view.Cell / 2);
            _view.ZoomBy(key(Keys.OemMinus) || key(Keys.Subtract) ? -1 : 1, corner);
            _view.Follow(editor.Cursor.X, editor.Cursor.Y);
        }

        if (key(Keys.R))
        {
            ToggleRect(editor);
        }

        // Up is +y, as the canvas draws it.
        var (dx, dy) = ((_menuRight ? 1 : 0) - (_menuLeft ? 1 : 0), (_menuUp ? 1 : 0) - (_menuDown ? 1 : 0));
        if ((dx, dy) != (0, 0))
        {
            if (shift)
            {
                _view.PanBy(new Vector2(-dx, dy) * _view.Cell);
            }
            else
            {
                editor.Cursor = (editor.Cursor.X + dx, editor.Cursor.Y + dy);
                _view.Follow(editor.Cursor.X, editor.Cursor.Y);
                if (_keyStroke)
                {
                    editor.PaintAt(editor.Cursor.X, editor.Cursor.Y);
                }
            }
        }

        if (controls.Pressed(Command.Confirm))
        {
            if (_rectAnchor is not null)
            {
                ToggleRect(editor);
            }
            else
            {
                editor.BeginStroke();
                _keyStroke = true;
                Painted(editor.PaintAt(editor.Cursor.X, editor.Cursor.Y));
            }
        }
        else if (_keyStroke && !controls.Held(Command.Confirm))
        {
            editor.EndStroke();
            _keyStroke = false;
        }
    }

    private void UpdateCanvasMouse(StageEditor editor, bool shift)
    {
        if (!IsActive)
        {
            return;
        }

        var cell = _view.CellAt(_pointer);
        var left = _mouse.LeftButton == ButtonState.Pressed;
        var panning = _mouse.RightButton == ButtonState.Pressed || _mouse.MiddleButton == ButtonState.Pressed;
        if (_pointerMoved && cell is { } hover && _panFrom is null)
        {
            editor.Cursor = hover;
        }

        if (_clicked && cell is { } at)
        {
            if (shift && StageEditor.Fills(editor.Tool))
            {
                (_rectAnchor, _mouseRect) = (at, true);
            }
            else
            {
                editor.BeginStroke();
                (_mouseStroke, _lastMouseCell) = (true, at);
                Painted(editor.PaintAt(at.X, at.Y));
            }
        }
        else if (_clicked)
        {
            ClickPanels(editor);
        }

        // A fast drag skips cells between ticks, so the stroke paints the line between them.
        if (_mouseStroke && left && cell is { } to && to != _lastMouseCell)
        {
            foreach (var (x, y) in Line(_lastMouseCell, to))
            {
                editor.PaintAt(x, y);
            }

            _lastMouseCell = to;
        }

        if (!left && _mouseStroke)
        {
            editor.EndStroke();
            _mouseStroke = false;
        }

        if (!left && _mouseRect)
        {
            _mouseRect = false;
            ToggleRect(editor);
        }

        if (panning && _panFrom is null && CanvasBox.Contains(_pointer))
        {
            _panFrom = _pointer;
        }
        else if (panning && _panFrom is { } from)
        {
            _view.PanBy(_pointer - from);
            _panFrom = _pointer;
        }
        else if (!panning)
        {
            _panFrom = null;
        }

        if (_wheel != 0 && CanvasBox.Contains(_pointer))
        {
            _view.ZoomBy(_wheel, _pointer);
        }
    }

    /// <summary>A click outside the canvas: a tool, the group selector, the hole toggle or a problem.</summary>
    private void ClickPanels(StageEditor editor)
    {
        var tool = _toolMenu.HitTest(_pointer, ToolLayout.Left, ToolLayout.Top, ToolLayout.Width, ToolLayout.Spacing);
        if (tool >= 0)
        {
            ChooseTool(editor, StageEditor.Tools[tool]);
        }
        else if (GroupDown.Contains(_pointer) || GroupUp.Contains(_pointer))
        {
            editor.ChangeNumber(GroupDown.Contains(_pointer) ? -1 : 1);
            _sounds.Play(Sfx.MenuMove);
        }
        else if (NewGroupButton.Contains(_pointer))
        {
            _sounds.Play(editor.NewGroup() ? Sfx.MenuMove : Sfx.Buzz);
        }
        else if (HoleToggle.Contains(_pointer))
        {
            editor.OverHole = !editor.OverHole;
            _sounds.Play(Sfx.MenuMove);
        }
        else if (_problemRows.FirstOrDefault(r => r.Row.Contains(_pointer)) is { Row.Width: > 0 } row)
        {
            editor.FocusProblem(row.Problem);
            _view.Follow(editor.Cursor.X, editor.Cursor.Y);
            _sounds.Play(Sfx.MenuMove);
        }
    }

    private void ChooseTool(StageEditor editor, EditorTool tool)
    {
        editor.Tool = tool;
        if (!StageEditor.Fills(tool))
        {
            _rectAnchor = null;
        }

        _sounds.Play(Sfx.MenuMove);
    }

    /// <summary>Starts a rectangle at the cursor, or fills the one started.</summary>
    private void ToggleRect(StageEditor editor)
    {
        if (_rectAnchor is { } anchor)
        {
            _rectAnchor = null;
            _sounds.Play(editor.FillRect(anchor, editor.Cursor) ? Sfx.Push : Sfx.Buzz);
        }
        else if (StageEditor.Fills(editor.Tool))
        {
            _rectAnchor = editor.Cursor;
            _sounds.Play(Sfx.MenuMove);
        }
        else
        {
            editor.Message = "Rectangles fill with wall, floor or hole.";
            _sounds.Play(Sfx.Buzz);
        }
    }

    private void Painted(bool changed)
    {
        if (changed)
        {
            _sounds.Play(Sfx.Step);
        }
    }

    private void UpdateEditorMenu(Controls controls, StageEditor editor)
    {
        if (Cancelled(controls))
        {
            _overlay = EditorOverlay.None;
            _sounds.Play(Sfx.Cancel);
            return;
        }

        switch (RunMenu(_overlayMenu, controls, EditorMenuLayout))
        {
            case 0:
                _overlay = EditorOverlay.None;
                break;
            case 1:
            case 2:
                // The menu stays open, so several undos can follow.
                Undo(editor, redo: _overlayMenu.Focus == 2);
                break;
            case 3:
                _overlay = EditorOverlay.None;
                Playtest(editor);
                break;
            case 4:
                _overlay = EditorOverlay.None;
                Solve(editor);
                break;
            case 5:
                _overlay = EditorOverlay.None;
                SaveStage(editor);
                break;
            case 6:
                OpenTitleEntry(editor);
                break;
            case 7:
                if (editor.Dirty)
                {
                    _overlay = EditorOverlay.ConfirmClose;
                    _overlayMenu = new MenuList(2);
                    _confirmQuit = false;
                }
                else
                {
                    CloseCanvas();
                }

                break;
        }
    }

    private void Undo(StageEditor editor, bool redo)
    {
        _keyStroke = _mouseStroke = false;
        if (redo ? editor.Redo() : editor.Undo())
        {
            editor.Message = null;
            _sounds.Play(Sfx.Cancel);
        }
        else
        {
            editor.Message = redo ? "Nothing to redo." : "Nothing to undo.";
            _sounds.Play(Sfx.Buzz);
        }
    }

    /// <summary>
    /// Plays the stage as it is now on the play screen, through the same board and session as any stage. The editor
    /// keeps its document, cursor and undo history; Esc or a clear comes back to it. A stage with problems isn't played.
    /// </summary>
    private void Playtest(StageEditor editor)
    {
        editor.EndStroke();
        _keyStroke = _mouseStroke = false;
        if (!Playable(editor, "Not played"))
        {
            return;
        }

        StartStage(new StagePack("playtest", "Playtest", "You", "NOASSERTION", [new PackStage("playtest/stage", editor.Title, editor.TryBuild()!)]), 0);
        _playtest = true;
        _sounds.Play(Sfx.Confirm);
    }

    /// <summary>Leaves a playtest for the editor, showing <paramref name="message"/> there.</summary>
    private void EndPlaytest(string message)
    {
        _session = null;
        _playtest = false;
        _nav.ReturnTo(Screen.Editor);
        _editor?.Message = message;
        _sounds.Play(Sfx.Cancel);
    }

    /// <summary>
    /// Starts a search for a solution, at most <see cref="EditorSolveCap"/> positions, in the background; the result shows
    /// in the status line when it's done.
    /// </summary>
    private void Solve(StageEditor editor)
    {
        if (_solve is not null)
        {
            editor.Message = "Still solving…";
            return;
        }

        if (!Playable(editor, "Not solved"))
        {
            return;
        }

        var stage = editor.TryBuild()!;
        _solveVersion = editor.Version;
        _solve = Task.Run(() => Solver.Solve(stage, EditorSolveCap));
        editor.Message = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Solving, trying up to {EditorSolveCap:N0} positions…");
    }

    /// <summary>True when the stage has no problems; otherwise the cursor goes to the first and the status says <paramref name="what"/> and why.</summary>
    private bool Playable(StageEditor editor, string what)
    {
        if (editor.Problems.Count == 0)
        {
            return true;
        }

        var count = editor.Problems.Count;
        editor.FocusProblem(0);
        _view.Follow(editor.Cursor.X, editor.Cursor.Y);
        editor.Message = $"{what}: fix the {(count == 1 ? "problem" : $"{count} problems")} first.";
        _sounds.Play(Sfx.Buzz);
        return false;
    }

    /// <summary>
    /// Holds any exit (the window's close button, Alt+F4, Quit) while the open stage has unsaved changes, and asks first
    /// with the same question as Close Stage. A playtest ends so the question shows over the editor.
    /// </summary>
    private void HoldExitForUnsavedChanges(object? sender, ExitingEventArgs e)
    {
        // A finished smoke run always exits; it never leaves a player's work behind.
        if (_quitConfirmed || _editor is not { Dirty: true } editor || _smoke is { Done: true })
        {
            return;
        }

        e.Cancel = true;
        if (_playtest)
        {
            EndPlaytest("Your changes aren't saved.");
        }

        editor.EndStroke();
        (_rectAnchor, _keyStroke, _mouseStroke) = (null, false, false);
        _overlay = EditorOverlay.ConfirmClose;
        _overlayMenu = new MenuList(2);
        _confirmQuit = true;
    }

    /// <summary>Back to the editor's list, dropping the open stage.</summary>
    private void CloseCanvas()
    {
        var name = _editor?.Name;
        _editor = null;
        _solve = null;
        _overlay = EditorOverlay.None;
        _sounds.Play(Sfx.Cancel);
        RefreshLibrary(name);
    }

    /// <summary>
    /// Saves the stage to the custom folder: under its name, or a new one from its title on its first save. A stage
    /// with problems isn't saved; the cursor goes to the first.
    /// </summary>
    private void SaveStage(StageEditor editor)
    {
        editor.EndStroke();
        if (!Playable(editor, "Not saved"))
        {
            return;
        }

        var name = editor.Name ?? CustomStages.UniqueName(_save.Folder, editor.Title);
        if (CustomStages.Write(_save.Folder, name, editor.ToDocument()) is { } error)
        {
            editor.Message = error;
            _sounds.Play(Sfx.Buzz);
            return;
        }

        editor.MarkSaved(name);
        editor.Message = $"Saved as custom/{name}.json.";
        _sounds.Play(Sfx.Confirm);
    }

    private void OpenTitleEntry(StageEditor editor)
    {
        _titleDraft = editor.Title;
        _typed.Clear();
        _overlay = EditorOverlay.Title;
    }

    /// <summary>
    /// A tick of typing into <see cref="_titleDraft"/>, up to <paramref name="max"/> characters: Enter hands the text to
    /// <paramref name="keep"/>, and Enter or Esc returns to <paramref name="back"/>.
    /// </summary>
    private void UpdateTextEntry(Controls controls, int max, Action<string> keep, EditorOverlay back)
    {
        bool Key(Keys key) => controls.Keys.IsKeyDown(key) && !controls.PreviousKeys.IsKeyDown(key);
        foreach (var c in _typed.ToString())
        {
            if (!char.IsControl(c) && _titleDraft.Length < max)
            {
                _titleDraft += c;
            }
        }

        _typed.Clear();
        if (Key(Keys.Back) && _titleDraft.Length > 0)
        {
            _titleDraft = _titleDraft[..^1];
        }

        if (Key(Keys.Escape))
        {
            _overlay = back;
            _sounds.Play(Sfx.Cancel);
        }
        else if (Key(Keys.Enter))
        {
            keep(_titleDraft);
            _overlay = back;
            _sounds.Play(Sfx.Confirm);
        }
    }

    /// <summary>The cells from <paramref name="from"/> to <paramref name="to"/> on a straight line, both ends included.</summary>
    internal static IEnumerable<(int X, int Y)> Line((int X, int Y) from, (int X, int Y) to)
    {
        var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : i / (float)steps;
            yield return ((int)MathF.Round(from.X + ((to.X - from.X) * t), MidpointRounding.AwayFromZero), (int)MathF.Round(from.Y + ((to.Y - from.Y) * t), MidpointRounding.AwayFromZero));
        }
    }

    private void DrawEditor()
    {
        if (_editor is null)
        {
            DrawLibrary();
        }
        else
        {
            DrawCanvas(_editor);
        }
    }

    private void DrawLibrary()
    {
        DrawDrift(0.6f);
        _ui.Begin();
        Heading("LEVEL EDITOR");
        var layout = SelectMenu;
        for (var row = 0; row < SelectRows && _libraryScroll + row < _libraryMenu.Count; row++)
        {
            var i = _libraryScroll + row;
            var top = layout.Top + (row * layout.Spacing);
            var focused = i == _libraryMenu.Focus;
            if (focused)
            {
                FocusBar(layout, top);
            }

            var ink = focused ? FocusInk : _ui["cream"] * 0.92f;
            if (i == 0)
            {
                _ui.Text(Font.BodyBold, 22, "+  New stage", new Vector2(layout.Left + 20, top + 12), ink);
                continue;
            }

            var stage = _custom[i - 1];
            _ui.Text(Font.BodyBold, 22, stage.Title, new Vector2(layout.Left + 20, top + 12), ink);
            _ui.Text(Font.Title, 16, stage.Stage is null ? "DAMAGED" : _save.Data.IsCompleted(stage.Id) ? "CLEARED" : "", new Vector2(layout.Left + layout.Width - 16, top + 16), ink, 1, 1);
        }

        var middle = layout.Left + (layout.Width / 2);
        if (_libraryScroll > 0)
        {
            Chevron(middle, layout.Top - 10, up: true);
        }

        if (_libraryScroll + SelectRows < _libraryMenu.Count)
        {
            Chevron(middle, layout.Top + (SelectRows * layout.Spacing) + 6, up: false);
        }

        DrawLibraryCard();
        if (_libraryStatus is not null)
        {
            _ui.Text(Font.Body, 18, _libraryStatus, new Vector2(640, 622), _ui["ember"], 0.5f);
        }

        _ui.Hints(MenuHints(("Arrows", "Choose"), ("Enter", "Select"), ("Esc", "Back")), 640, 660, _ui["cream"], 17);
        if (_overlay == EditorOverlay.Actions)
        {
            var stage = _custom[_libraryMenu.Focus - 1];
            DrawMenuOverlay(stage.Title.ToUpperInvariant(), LibraryActions);
        }
        else if (_overlay == EditorOverlay.ConfirmDelete)
        {
            var stage = _custom[_libraryMenu.Focus - 1];
            DrawConfirm("DELETE STAGE?", $"“{stage.Title}” and its file custom/{stage.Name}.json are deleted for good.", ["Cancel", "Delete"]);
        }
        else if (_overlay == EditorOverlay.Export)
        {
            DrawExport();
        }
        else if (_overlay == EditorOverlay.ExportText)
        {
            DrawExport();
            DrawTitleEntry(_exportCreatorField ? "PACK CREATOR" : "PACK TITLE", MaxPackText);
        }

        _ui.End();
    }

    /// <summary>The export panel: the pack's title, creator and license, Export, and the stages to tick.</summary>
    private void DrawExport()
    {
        _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
        _ui.Panel(260, 60, 760, 620, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 28, "EXPORT A PACK", new Vector2(640, 80), _ui["cream"], 0.5f, 2);
        var layout = ExportLayout;
        for (var row = 0; row < ExportVisible && _exportScroll + row < _overlayMenu.Count; row++)
        {
            var i = _exportScroll + row;
            var top = layout.Top + (row * layout.Spacing);
            var focused = i == _overlayMenu.Focus;
            if (focused)
            {
                FocusBar(layout, top);
            }

            var ink = focused ? FocusInk : _ui["cream"];
            var marked = _custom.Count(s => _exportMarked.Contains(s.Name));
            var (label, value) = i switch
            {
                0 => ("Title", _exportTitle),
                1 => ("Creator", _exportCreator),
                2 => ("License", $"<  {PackArchive.Licenses[_exportLicense]}  >"),
                3 => ($"Export {marked} {(marked == 1 ? "stage" : "stages")}", ""),
                4 => ("Back", ""),
                _ => ($"{(_exportMarked.Contains(_custom[i - ExportRows.Length].Name) ? "[x]" : "[  ]")}  {_custom[i - ExportRows.Length].Title}", ""),
            };
            _ui.Text(Font.BodyBold, 20, label, new Vector2(layout.Left + 16, top + 10), ink);
            _ui.Text(Font.Body, 20, value, new Vector2(layout.Left + layout.Width - 16, top + 10), ink, 1);
        }

        if (_exportScroll + ExportVisible < _overlayMenu.Count)
        {
            Chevron(640, layout.Top + (ExportVisible * layout.Spacing) + 4, up: false);
        }

        // The status names the archive's full path last; it's drawn on its own lines so it can break at its folders.
        var (text, path) = _exportPath is { } written && _exportStatus is { } done && done.EndsWith(written, StringComparison.Ordinal)
            ? (done[..^written.Length].TrimEnd(), written)
            : (_exportStatus ?? "The pack is written as a .zip to this folder. Share that file; a player puts it in their imports folder and picks it under Import in stage select.", _exportStatus is null ? Path.GetFullPath(PackArchive.ExportsFolder(_save.Folder)) : null);
        var color = _exportStatus is null ? _ui["cream"] * 0.85f : _ui["ember"];
        var height = _ui.Wrapped(Font.Body, 17, text, new Vector2(300, 524), 680, color);
        if (path is not null)
        {
            DrawPath(path, new Vector2(300, 528 + height), 680, 16, color);
        }
        _ui.Hints(MenuHints(("Enter", "Change"), ("Left Right", "License"), ("Esc", "Back")), 640, 648, _ui["cream"], 16);
    }

    /// <summary>The focused entry's card: what New does, or a preview of the stage.</summary>
    private void DrawLibraryCard()
    {
        var (x, y, width, height) = (690f, 150f, 520f, 460f);
        _ui.Panel(x, y, width, height, _ui["ink"] * 0.78f);
        if (_libraryMenu.Focus == 0)
        {
            _ui.Text(Font.Title, 16, "MY STAGES", new Vector2(x + 24, y + 18), _ui["bandOnDark"], spacing: 1);
            _ui.Text(Font.BodyBold, 30, "Make a stage", new Vector2(x + 24, y + 40), _ui["cream"]);
            var text = "Paint walls, floor, holes, blocks and swivels, set the goal and up to four starts, and playtest as you go. "
                + "A stage saves once it has no problems, and then shows up under My Stages in stage select.";
            text += _custom.Count == 0 ? "\n\nYou have no stages yet." : $"\n\nYou have {_custom.Count} {(_custom.Count == 1 ? "stage" : "stages")}.";
            _ui.Wrapped(Font.Body, 20, text, new Vector2(x + 24, y + 96), width - 48, _ui["cream"]);
            return;
        }

        var stage = _custom[_libraryMenu.Focus - 1];
        _ui.Text(Font.Title, 16, $"MY STAGES  ·  CUSTOM/{stage.Name.ToUpperInvariant()}.JSON", new Vector2(x + 24, y + 18), _ui["bandOnDark"], spacing: 1);
        _ui.Text(Font.BodyBold, 30, stage.Title, new Vector2(x + 24, y + 40), _ui["cream"]);
        var (px, py, pw, ph) = (x + 24, y + 96, width - 48, 300f);
        _ui.Panel(px, py, pw, ph, _ui["night"]);
        if (stage.Stage is { } playable)
        {
            DrawPreview(playable, px + 8, py + 8, pw - 16, ph - 16);
        }
        else
        {
            _ui.Wrapped(Font.Body, 19, $"This file can't be read as a stage: {stage.Problem}", new Vector2(px + 16, py + 16), pw - 32, _ui["cream"]);
        }

        _ui.Text(Font.Body, 19, "Enter to edit, copy, export or delete it.", new Vector2(x + 24, y + 412), _ui["cream"] * 0.85f);
    }

    private void DrawCanvas(StageEditor editor)
    {
        _ui.Begin();
        var hc = _save.Data.Settings.HighContrast;
        _ui.Rect(CanvasBox.X, CanvasBox.Y, CanvasBox.Width, CanvasBox.Height, _ui["night"]);
        var (floor, wall) = PreviewTones(hc);
        var cell = _view.Cell;
        for (var y = 0; y < Stage.Height; y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                var p = _view.CellCorner(x, y);
                if (p.X + cell < CanvasBox.X || p.X > CanvasBox.Right || p.Y + cell < CanvasBox.Y || p.Y > CanvasBox.Bottom)
                {
                    continue;
                }

                var tile = editor[x, y];
                DrawTile(tile, p.X, p.Y, cell, floor, wall, (x + y) % 2 == 0 || hc);

                // The group's number, beside its color and pattern, so two groups never look alike.
                if (tile.Group > 0)
                {
                    var size = Math.Clamp(cell * 0.5f, 11, 30);
                    _ui.ShadowText(Font.BodyBold, size, tile.Group.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(p.X + (cell / 2), p.Y + (cell * 0.18f)), _ui["cream"], _ui["ink"], 0.5f, 0, Math.Max(1, cell / 20));
                }
            }
        }

        // Faint lines let an author count cells, on the dark walls as on the floor.
        for (var i = 0; i <= Stage.Width; i++)
        {
            var a = _view.CellCorner(i, Stage.Height - 1);
            _ui.Rect(a.X, Math.Max(a.Y, CanvasBox.Y), 1, Math.Min(cell * Stage.Height, CanvasBox.Height), _ui["cream"] * 0.08f);
            var b = _view.CellCorner(0, i - 1);
            _ui.Rect(Math.Max(b.X, CanvasBox.X), b.Y, Math.Min(cell * Stage.Width, CanvasBox.Width), 1, _ui["cream"] * 0.08f);
        }

        if (editor.Goal is { } goal)
        {
            var p = _view.CellCorner(goal.X, goal.Y);
            DrawGoal(p.X, p.Y, cell);
        }

        for (var i = 0; i < editor.Starts.Count; i++)
        {
            var p = _view.CellCorner(editor.Starts[i].X, editor.Starts[i].Y);
            DrawMiniJack(p.X, p.Y, cell);
            _ui.ShadowText(Font.BodyBold, Math.Clamp(cell * 0.4f, 11, 24), (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(p.X + cell - 3, p.Y + (cell * 0.5f)), _ui["cream"], _ui["ink"], 1, 0, 1);
        }

        DrawCanvasMarks(editor);
        DrawEditorPanels(editor);
        switch (_overlay)
        {
            case EditorOverlay.Menu:
                DrawMenuOverlay("EDITOR", EditorMenuItems);
                break;
            case EditorOverlay.ConfirmClose:
                DrawConfirm("UNSAVED CHANGES", _confirmQuit ? $"Quit and lose the changes to “{editor.Title}” since it was last saved?" : $"Close “{editor.Title}” and lose the changes since it was last saved?", ["Keep Editing", _confirmQuit ? "Quit" : "Discard"]);
                break;
            case EditorOverlay.Title:
                DrawTitleEntry();
                break;
        }

        _ui.End();
    }

    /// <summary>The problems' cells, the rectangle being drawn and the cursor, over the canvas.</summary>
    private void DrawCanvasMarks(StageEditor editor)
    {
        var cell = _view.Cell;
        var pulse = 0.75f + (0.25f * MathF.Sin(Motion * 0.2f));
        var problemColor = new Color(255, 70, 90);
        for (var i = 0; i < editor.Problems.Count; i++)
        {
            if (editor.Problems[i] is { X: { } x, Y: { } y })
            {
                var p = _view.CellCorner(x, y);
                var focused = i == editor.ProblemFocus;
                Outline(p.X, p.Y, cell, cell, focused ? 5 : 3, problemColor * (focused ? pulse : 0.9f));

                // A mark as well as a color: an exclamation badge in the corner.
                var badge = Math.Clamp(cell * 0.42f, 12, 22);
                _ui.Panel(p.X + cell - badge, p.Y, badge, badge, problemColor);
                _ui.Text(Font.BodyBold, badge * 0.8f, "!", new Vector2(p.X + cell - (badge / 2), p.Y), _ui["ink"], 0.5f);
            }
        }

        if (_rectAnchor is { } anchor)
        {
            var a = _view.CellCorner(Math.Min(anchor.X, editor.Cursor.X), Math.Max(anchor.Y, editor.Cursor.Y));
            var b = _view.CellCorner(Math.Max(anchor.X, editor.Cursor.X), Math.Min(anchor.Y, editor.Cursor.Y)) + new Vector2(cell);
            _ui.Rect(a.X, a.Y, b.X - a.X, b.Y - a.Y, _ui["ember"] * 0.3f);
            Outline(a.X, a.Y, b.X - a.X, b.Y - a.Y, 2, _ui["ember"]);
        }

        var c = _view.CellCorner(editor.Cursor.X, editor.Cursor.Y);
        Outline(c.X - 2, c.Y - 2, cell + 4, cell + 4, 2, _ui["ink"]);
        Outline(c.X, c.Y, cell, cell, 3, _ui["ember"] * pulse);
    }

    private void Outline(float x, float y, float width, float height, float thickness, Color color)
    {
        _ui.Rect(x, y, width, thickness, color);
        _ui.Rect(x, y + height - thickness, width, thickness, color);
        _ui.Rect(x, y, thickness, height, color);
        _ui.Rect(x + width - thickness, y, thickness, height, color);
    }

    /// <summary>The bars and side panels around the canvas: they're opaque, so they also hide cells panned past its edge.</summary>
    private void DrawEditorPanels(StageEditor editor)
    {
        var panel = _ui["ink"];
        _ui.Rect(0, 0, VirtualScreen.Width, CanvasBox.Y, panel);
        _ui.Rect(0, CanvasBox.Bottom, VirtualScreen.Width, VirtualScreen.Height - CanvasBox.Bottom, panel);
        _ui.Rect(0, 0, CanvasBox.X, VirtualScreen.Height, panel);
        _ui.Rect(CanvasBox.Right, 0, VirtualScreen.Width - CanvasBox.Right, VirtualScreen.Height, panel);
        Outline(CanvasBox.X - 2, CanvasBox.Y - 2, CanvasBox.Width + 4, CanvasBox.Height + 4, 2, _ui["hatOnDark"]);

        _ui.Text(Font.TitleBold, 26, "LEVEL EDITOR", new Vector2(24, 20), _ui["cream"], spacing: 2);
        var tile = editor[editor.Cursor.X, editor.Cursor.Y];
        var at = $"({editor.Cursor.X}, {editor.Cursor.Y})  {DescribeCell(editor, tile)}";
        _ui.Text(Font.BodyBold, 19, at, new Vector2(CanvasBox.X, 24), _ui["cream"]);
        _ui.Text(Font.Title, 15, $"ZOOM {EditorView.Zooms[_view.Zoom]:0.#}×", new Vector2(CanvasBox.Right, 28), _ui["bandOnDark"], 1, 1);

        DrawToolPanel(editor);
        DrawStagePanel(editor);
        _ui.Hints([("Arrows", "Cursor"), ("Space", "Paint"), ("1-9", "Tool"), ("Tab", "Problem"), ("P", "Playtest"), ("Ctrl Z", "Undo"), ("Ctrl S", "Save"), ("Esc", "Menu")], 640, 680, _ui["cream"], 16);
    }

    private static string DescribeCell(StageEditor editor, Tile tile)
    {
        var what = tile.Kind switch
        {
            CellKind.Block => $"block, group {tile.Group}",
            CellKind.SwivelCenter => $"swivel center, group {tile.Group}",
            CellKind.SwivelArm => $"swivel arm, group {tile.Group}",
            CellKind.Hole => "hole",
            CellKind.Floor => "floor",
            _ => "wall",
        };
        if (tile.OverHole)
        {
            what += " over a hole";
        }

        var start = editor.Starts.ToList().IndexOf(editor.Cursor);
        if (start >= 0)
        {
            what += $", start {start + 1}";
        }

        return editor.Goal == editor.Cursor ? what + ", goal" : what;
    }

    private void DrawToolPanel(StageEditor editor)
    {
        _ui.Text(Font.Title, 15, "TOOLS", new Vector2(24, 82), _ui["bandOnDark"], spacing: 2);
        for (var i = 0; i < StageEditor.Tools.Length; i++)
        {
            var top = ToolLayout.Top + (i * ToolLayout.Spacing);
            var chosen = editor.Tool == StageEditor.Tools[i];
            if (chosen)
            {
                _ui.Panel(ToolLayout.Left, top + 1, ToolLayout.Width, ToolLayout.Spacing - 2, _ui["ember"]);
            }

            var ink = chosen ? _ui["ink"] : _ui["cream"];
            _ui.Text(Font.BodyBold, 17, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(ToolLayout.Left + 10, top + 6), ink * 0.7f);
            _ui.Text(Font.BodyBold, 18, StageEditor.NameOf(StageEditor.Tools[i]), new Vector2(ToolLayout.Left + 34, top + 5), ink);
        }

        var start = editor.Tool == EditorTool.Start;
        _ui.Text(Font.Title, 15, start ? "START" : "GROUP", new Vector2(24, 408), _ui["bandOnDark"], spacing: 2);
        _ui.Panel(GroupDown.X, GroupDown.Y, GroupDown.Width, GroupDown.Height, _ui["hat"]);
        _ui.Panel(GroupUp.X, GroupUp.Y, GroupUp.Width, GroupUp.Height, _ui["hat"]);
        _ui.Text(Font.BodyBold, 26, "[", new Vector2(GroupDown.X + 20, GroupDown.Y + 12), _ui["cream"], 0.5f);
        _ui.Text(Font.BodyBold, 26, "]", new Vector2(GroupUp.X + 20, GroupUp.Y + 12), _ui["cream"], 0.5f);
        var middle = new Vector2(136, GroupDown.Y);
        if (start)
        {
            DrawMiniJack(middle.X - 76, middle.Y + 4, 48);
            _ui.Text(Font.BodyBold, 40, editor.Start.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(middle.X + 10, middle.Y + 2), _ui["cream"]);
            var placed = editor.Start <= editor.Starts.Count ? $"at ({editor.Starts[editor.Start - 1].X}, {editor.Starts[editor.Start - 1].Y})" : "not placed";
            _ui.Text(Font.Body, 16, $"Start {editor.Start}: {placed}", new Vector2(24, 494), _ui["cream"] * 0.85f);
        }
        else
        {
            // Color and pattern are the look; the number is the group. Both show, since colors repeat.
            var swatch = new Rectangle(BoardRenderer.GroupPattern(editor.Group) * Atlas.TileSize, 0, Atlas.TileSize, Atlas.TileSize);
            _ui.Rect(middle.X - 72, middle.Y + 4, 48, 48, _ui["ink"]);
            _ui.Sprite(_board.AtlasTexture, swatch, middle.X - 70, middle.Y + 6, 44, 44, _board.GroupColor(editor.Group));
            _ui.Text(Font.BodyBold, 40, editor.Group.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(middle.X + 4, middle.Y + 2), _ui["cream"]);
            _ui.Text(Font.Body, 16, $"Group {editor.Group}: {editor.Describe(editor.Group)}", new Vector2(24, 494), _ui["cream"] * 0.85f);
        }

        Button(NewGroupButton, "N  New group", false);
        Button(HoleToggle, $"H  Over a hole: {(editor.OverHole ? "on" : "off")}", editor.OverHole);
        _ui.Wrapped(Font.Body, 14, "G pick from cell · T title · + − zoom · Shift+arrows or right-drag pan · R or Shift+drag rectangle", new Vector2(24, 598), 224, _ui["cream"] * 0.7f, 1.25f);

        void Button(RectangleF box, string label, bool on)
        {
            _ui.Panel(box.X, box.Y, box.Width, box.Height, on ? _ui["band"] : _ui["hat"]);
            _ui.Text(Font.BodyBold, 16, label, new Vector2(box.X + 10, box.Y + 5), _ui["cream"]);
        }
    }

    private void DrawStagePanel(StageEditor editor)
    {
        var (x, width) = (CanvasBox.Right + 20, VirtualScreen.Width - CanvasBox.Right - 40);
        _ui.Text(Font.Title, 15, "STAGE", new Vector2(x, 82), _ui["bandOnDark"], spacing: 2);
        _ui.Text(Font.BodyBold, 24, editor.Title, new Vector2(x, 102), _ui["cream"]);
        var file = editor.Name is null ? "Not saved yet" : $"custom/{editor.Name}.json";
        _ui.Text(Font.Body, 16, editor.Dirty ? $"{file}  ·  unsaved changes" : file, new Vector2(x, 136), editor.Dirty ? _ui["ember"] : _ui["cream"] * 0.75f);

        var problems = editor.Problems;
        _ui.Text(Font.Title, 15, problems.Count == 0 ? "PROBLEMS" : $"PROBLEMS ({problems.Count})", new Vector2(x, 172), _ui["bandOnDark"], spacing: 2);
        _problemRows.Clear();
        var (top, bottom) = (196f, 556f);
        if (problems.Count == 0)
        {
            _ui.Wrapped(Font.Body, 18, "None. The stage can be played and saved.", new Vector2(x, top), width, _ui["cream"]);
        }
        else
        {
            // Rows are as tall as their wrapped text; the list starts late enough that the focused one shows.
            float Height(int i) => _ui.Wrapped(Font.Body, 16, Label(problems[i]), Vector2.Zero, width - 16, Color.White, 1.25f, draw: false) + 10;
            var first = 0;
            while (editor.ProblemFocus > first && Enumerable.Range(first, editor.ProblemFocus - first + 1).Sum(Height) > bottom - top)
            {
                first++;
            }

            var y = top;
            for (var i = first; i < problems.Count && y + Height(i) <= bottom; i++)
            {
                var height = Height(i);
                if (i == editor.ProblemFocus)
                {
                    _ui.Panel(x - 8, y - 2, width + 8, height, _ui["hatOnDark"]);
                }

                _ui.Wrapped(Font.Body, 16, Label(problems[i]), new Vector2(x, y + 3), width - 16, _ui["cream"], 1.25f);
                _problemRows.Add((new RectangleF(x - 8, y - 2, width + 8, height), i));
                y += height;
            }
        }

        if (editor.Message is { } message)
        {
            _ui.Panel(x - 8, 566, width + 8, 86, _ui["hat"]);
            _ui.Wrapped(Font.Body, 16, message, new Vector2(x, 574), width - 8, _ui["cream"], 1.25f);
        }

        static string Label(StageProblem problem) => problem.X is { } px && problem.Y is { } py ? $"({px}, {py})  {problem.Message}" : problem.Message;
    }

    private void DrawMenuOverlay(string title, string[] items)
    {
        _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
        var height = 120 + (items.Length * EditorMenuLayout.Spacing);
        _ui.Panel(430, 130, 420, height, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 28, title.Length > 24 ? title[..23] + "…" : title, new Vector2(640, 152), _ui["cream"], 0.5f, 2);
        DrawMenu(_overlayMenu, items, EditorMenuLayout, 24);
    }

    private void DrawConfirm(string title, string text, string[] items)
    {
        _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
        _ui.Panel(400, 250, 480, 260, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 28, title, new Vector2(640, 276), _ui["cream"], 0.5f, 2);
        _ui.Wrapped(Font.Body, 19, text, new Vector2(430, 322), 420, _ui["cream"]);
        DrawMenu(_overlayMenu, items, ConfirmLayout, 24);
    }

    private void DrawTitleEntry(string heading = "STAGE TITLE", int max = StageEditor.MaxTitle)
    {
        _ui.Rect(0, 0, VirtualScreen.Width, VirtualScreen.Height, _ui["ink"] * 0.6f);
        _ui.Panel(340, 250, 600, 200, _ui["hat"] * 0.97f);
        _ui.Text(Font.TitleBold, 28, heading, new Vector2(640, 272), _ui["cream"], 0.5f, 2);
        _ui.Panel(370, 322, 540, 48, _ui["ink"]);
        var caret = (Motion / 15) % 2 == 0 || _save.Data.Settings.ReduceMotion ? "|" : "";

        // A pack's longer text is drawn smaller so it fits the box.
        _ui.Text(Font.BodyBold, max > StageEditor.MaxTitle ? 18 : 24, _titleDraft + caret, new Vector2(386, 334), _ui["cream"]);
        _ui.Text(Font.Body, 17, $"Type up to {max} characters. Enter keeps it, Esc cancels.", new Vector2(640, 392), _ui["cream"] * 0.85f, 0.5f);
    }
}
