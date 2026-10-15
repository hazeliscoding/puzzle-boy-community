using Microsoft.Xna.Framework;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Play;

namespace PuzzleBoy.Community;

/// <summary>The play screen: input, the board, the HUD and the clear celebration.</summary>
internal sealed partial class CommunityGame
{
    /// <summary>Ticks after a stage starts or restarts before input is read, as in the original.</summary>
    internal const int StartLock = PlayInput.StartLock;

    /// <summary>Ticks the stage's name shows big as it starts, before it has faded.</summary>
    internal const int IntroTicks = 50;

    private void UpdatePlaying(Controls controls)
    {
        if (_themePending)
        {
            // The loading frame has shown; the classic pack's check and decoding take a moment the first time.
            if (_themeShown)
            {
                _themePending = false;
                ChooseTheme();
            }

            return;
        }

        var session = _session!;
        List<Sfx> sounds = [];
        List<int> effects = [];
        var next = PlayTick.Run(session, _camera, _classic is null ? null : _classicStage, controls, _playtest, _playtest ? null : _save.Data, sounds, effects, out var newBest);
        foreach (var number in effects)
        {
            _classic!.Play(number);
        }

        if (newBest is { } best)
        {
            _newBest = best;
            _save.Save();
        }

        if (next == PlayNext.Pause)
        {
            _pauseMenu.Set(0);
            _helpOpen = false;
            _nav.Open(Screen.Paused);
        }

        foreach (var sfx in sounds)
        {
            Sound(sfx);
        }

        if (next == PlayNext.LeavePlaytest || (next == PlayNext.Continue && _playtest))
        {
            EndPlaytest(next == PlayNext.Continue ? $"Playtest cleared in {session.Moves} {(session.Moves == 1 ? "move" : "moves")}." : "Playtest stopped.");
        }
        else if (next == PlayNext.Continue && !session.Next())
        {
            // The pack is done: back to the stage list, where My Stages or an imported pack stays open on its last stage.
            _session = null;
            LoadSelectPacks();
            var tab = _selectPacks.FindIndex(p => p.PackId == _playPack?.PackId);
            if (tab >= 0 && !_manifest.Packs.Contains(_playPack!))
            {
                ShowTab(tab, session.Index);
            }
            else
            {
                FocusNextStage();
            }

            _nav.ReturnTo(Screen.StageSelect);
        }
    }

    private void DrawPlaying()
    {
        var session = _session!;
        if (_themePending)
        {
            _ui.Begin();
            _ui.Panel(390, 320, 500, 80, _ui["ink"] * 0.85f);
            _ui.Text(Font.Title, 18, "LOADING THE CLASSIC THEME", new Vector2(640, 348), _ui["ember"], 0.5f, 2);
            _ui.End();
            _themeShown = true;
            return;
        }

        if (_classic is { } classic)
        {
            classic.Draw(session, _classicStage!, GraphicsDevice.Viewport.AspectRatio, _ticks);
        }
        else
        {
            DrawDefaultBoard(session);
        }

        _ui.Begin();
        DrawHud(session);
        if (session.Board.IsCleared && session.ClearTicks > 0)
        {
            DrawCelebration(session);
        }
        else if (session.Ticks < IntroTicks || ShowsHint(session))
        {
            DrawIntro(session);
        }

        if (_nav.Current == Screen.Paused)
        {
            DrawPaused();
        }

        _ui.End();
    }

    /// <summary>
    /// The Default theme's board: drawn in code, the whole play area framed afresh each frame, or centered on the cursor in
    /// scroll mode.
    /// </summary>
    private void DrawDefaultBoard(PlaySession session)
    {
        var (minX, minY, maxX, maxY) = BoardRenderer.PlayBounds(session.Board);
        var viewport = GraphicsDevice.Viewport;
        var (pivot, distance) = _camera.Frame(minX, minY, maxX, maxY, viewport.Width, viewport.Height);
        if (session.Scroll is { } cursor)
        {
            // Scroll mode looks at the cursor from the same distance.
            pivot = new Vector3(cursor.X, 0, -cursor.Y);
        }

        _board.Draw(session, _camera.View(pivot, distance), CameraRig.Projection(viewport.AspectRatio), _ticks);
    }

    private void DrawHud(PlaySession session)
    {
        var heading = _playtest ? "PLAYTEST  ·  NOTHING IS RECORDED" : $"{_playPack!.Title.ToUpperInvariant()}  ·  STAGE {session.Index + 1} OF {session.Count}";
        _ui.Panel(20, 18, 460, 96, _ui["ink"] * 0.72f);
        _ui.Text(Font.Title, 16, heading, new Vector2(40, 32), _ui["bandOnDark"], spacing: 1);
        _ui.Text(Font.BodyBold, 34, session.Stage.Title, new Vector2(40, 54), _ui["cream"]);

        Stat(1260, "MOVES", session.Moves.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Stat(1140, "UNDO", session.UndoAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Stat(1020, "TIME", FormatTime(session.ElapsedTicks / 30.0)[..^2]);
        var several = session.Board.Characters.Count > 1;
        if (several)
        {
            Stat(900, "JACK", $"{session.Board.ActiveIndex + 1}/{session.Board.Characters.Count}");
        }

        _ui.Rect(0, 672, 1280, 48, _ui["ink"] * 0.78f);
        if (_nav.Current == Screen.Paused)
        {
            // The pause menu puts its own hints in the bar.
            return;
        }

        if (session.Scroll is not null)
        {
            _ui.Hints([("W A S D", "Look around"), ("Q E", "Turn"), ("V", "View"), ("Tab", "Back to Jack")], 640, 680, _ui["cream"], 16);
            return;
        }

        List<(string, string)> hints = [("W A S D", "Move"), ("Q E", "Turn"), ("V", "View"), ("Tab", "Scroll"), ("Z", "Undo"), ("R", "Restart")];
        if (several)
        {
            hints.Add(("C", "Switch"));
        }

        hints.Add(_playtest ? ("Esc", "Editor") : ("Esc", "Pause"));
        _ui.Hints(hints, 640, 680, _ui["cream"], 16);

        void Stat(float right, string label, string value)
        {
            _ui.Panel(right - 108, 18, 108, 96, _ui["ink"] * 0.72f);
            _ui.Text(Font.Title, 15, label, new Vector2(right - 54, 32), _ui["bandOnDark"], 0.5f, 1);
            _ui.Text(Font.BodyBold, value.Length > 3 ? 32 : 40, value, new Vector2(right - 54, value.Length > 3 ? 58 : 52), _ui["cream"], 0.5f);
        }
    }

    /// <summary>A time in seconds as <c>m:ss.s</c>.</summary>
    internal static string FormatTime(double seconds)
    {
        var tenths = (int)Math.Floor(seconds * 10);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tenths / 600}:{tenths / 10 % 60:00}.{tenths % 10}");
    }

    /// <summary>
    /// True while a stage the player hasn't cleared waits for its first step, when the intro shows its hint, so a new idea
    /// is explained where it's first needed rather than only in Help.
    /// </summary>
    private bool ShowsHint(PlaySession session) =>
        session.Stage.Hint is not null && session.Moves == 0 && !_save.Data.IsCompleted(session.Stage.Id);

    private void DrawIntro(PlaySession session)
    {
        var hint = ShowsHint(session) ? session.Stage.Hint : null;
        var fade = hint is null ? Math.Clamp((IntroTicks - session.Ticks) / 15f, 0, 1) : 1;
        var hintHeight = hint is null ? 0 : _ui.Wrapped(Font.Body, 20, hint, Vector2.Zero, 680, Color.White, 1.3f, draw: false) + 10;
        var top = 520 - hintHeight;
        _ui.Panel(280, top, 720, 110 + hintHeight, _ui["ink"] * (0.85f * fade));
        _ui.Text(Font.Title, 18, _playtest ? "PLAYTEST" : $"STAGE {session.Index + 1}", new Vector2(640, top + 16), _ui["ember"] * fade, 0.5f, 3);
        _ui.Text(Font.BodyBold, 40, session.Stage.Title, new Vector2(640, top + 42), _ui["cream"] * fade, 0.5f);
        if (hint is not null)
        {
            _ui.Wrapped(Font.Body, 20, hint, new Vector2(300, top + 104), 680, _ui["cream"], 1.3f);
        }
    }

    private void DrawCelebration(PlaySession session)
    {
        var t = session.ClearTicks;
        var rise = Math.Clamp(t / 8f, 0, 1);

        // Confetti in the block colors, falling from the top; none when the player reduces motion.
        for (var i = 0; i < (_save.Data.Settings.ReduceMotion ? 0 : 70); i++)
        {
            var hash = (uint)((i + 1) * 2654435761u);
            var x = (hash % 1280) + (MathF.Sin((t * 0.1f) + i) * 20);
            var y = -20 + ((t * (5 + (hash % 5))) + (hash % 300)) - 300;
            if (y > -20 && y < 720)
            {
                _ui.Rect(x, y, 10, 6 + (hash % 6), _theme.GroupColors[i % _theme.GroupColors.Count]);
            }
        }

        _ui.Panel(290, 230 + ((1 - rise) * 30), 700, 210, _ui["ink"] * (0.85f * rise));
        _ui.ShadowText(Font.TitleBold, 72, "STAGE CLEAR!", new Vector2(640, 252 + ((1 - rise) * 30)), _ui["ember"] * rise, _ui["pumpkinShade"] * rise, 0.5f, 2, 6);
        var moves = session.Moves == 1 ? "1 move" : $"{session.Moves} moves";
        var line = $"{moves}  ·  {FormatTime(session.ElapsedTicks / 30.0)}{(_newBest ? "  ·  New best!" : "")}";
        _ui.Text(Font.BodyBold, 30, line, new Vector2(640, 346), _ui["cream"] * rise, 0.5f);
        var next = _playtest ? "Back to the editor" : session.HasNext ? $"Next: {session.StageAt(session.Index + 1).Title}" : "Pack complete!";
        _ui.Text(Font.Body, 24, next, new Vector2(640, 390), _ui["bandOnDark"] * rise, 0.5f);
    }
}
