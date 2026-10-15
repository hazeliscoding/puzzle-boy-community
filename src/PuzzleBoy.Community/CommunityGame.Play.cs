using Microsoft.Xna.Framework;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community;

/// <summary>The play screen: input, the board, the HUD and the clear celebration.</summary>
internal sealed partial class CommunityGame
{
    /// <summary>Ticks after a stage starts before a step is read, so a key still held from the menu doesn't walk.</summary>
    internal const int StartLock = 6;

    /// <summary>Ticks the stage's name shows big as it starts, before it has faded.</summary>
    internal const int IntroTicks = 50;

    private void UpdatePlaying(Controls controls)
    {
        var session = _session!;
        var events = session.Tick();
        if (events.HasFlag(PlayEvents.FallStarted))
        {
            _sounds.Play(Sfx.Fall);
        }

        if (events.HasFlag(PlayEvents.Cleared))
        {
            _sounds.Play(Sfx.Clear);
            _newBest = _save.Data.Record(session.Stage.Id, session.Moves, session.ElapsedTicks / 30.0);
            _save.Save();
        }
        else if (events.HasFlag(PlayEvents.ReachedGoal))
        {
            _sounds.Play(Sfx.Goal);
        }

        if (session.ReadyToContinue)
        {
            if (!session.Next())
            {
                // The pack is done: back to the stage list.
                _session = null;
                FocusNextStage();
                _nav.ReturnTo(Screen.StageSelect);
            }

            return;
        }

        if (controls.Pressed(Command.Pause))
        {
            _pauseMenu.Set(0);
            _helpOpen = false;
            _nav.Open(Screen.Paused);
            _sounds.Play(Sfx.Confirm);
            return;
        }

        if (session.Board.IsCleared || session.Ticks <= StartLock)
        {
            return;
        }

        if ((controls.Pressed(Command.TurnLeft) && _camera.TurnLeft())
            || (controls.Pressed(Command.TurnRight) && _camera.TurnRight())
            || (controls.Pressed(Command.Overhead) && _camera.ToggleOverhead()))
        {
            _sounds.Play(Sfx.View);
        }

        if (controls.Pressed(Command.Undo))
        {
            _sounds.Play(session.Undo() ? Sfx.Cancel : Sfx.Buzz);
        }

        if (controls.Pressed(Command.Restart))
        {
            session.Restart();
            _sounds.Play(Sfx.Confirm);
        }

        if (controls.Pressed(Command.Switch))
        {
            _sounds.Play(session.Switch() ? Sfx.View : Sfx.Buzz);
        }

        // Input follows the view; a held direction keeps walking. Steps wait for a turn of the view to finish.
        if (!session.Busy && !_camera.IsBusy && controls.HeldDirection() is { } screen)
        {
            var result = session.Step(_camera.DirectionFor(screen));
            if (result != StepResult.Refused)
            {
                _sounds.Play(result switch { StepResult.Walked => Sfx.Step, StepResult.Pushed => Sfx.Push, _ => Sfx.Turn });
            }
        }
    }

    private void DrawPlaying()
    {
        var session = _session!;
        // The whole play area stays in view, framed afresh each frame as the view turns or the window changes size.
        var (minX, minY, maxX, maxY) = BoardRenderer.PlayBounds(session.Board);
        var viewport = GraphicsDevice.Viewport;
        var (pivot, distance) = _camera.Frame(minX, minY, maxX, maxY, viewport.Width, viewport.Height);
        _board.Draw(session, _camera.View(pivot, distance), CameraRig.Projection(viewport.AspectRatio), _ticks);
        _ui.Begin();
        DrawHud(session);
        if (session.Board.IsCleared && session.ClearTicks > 0)
        {
            DrawCelebration(session);
        }
        else if (session.Ticks < IntroTicks)
        {
            DrawIntro(session);
        }

        if (_nav.Current == Screen.Paused)
        {
            DrawPaused();
        }

        _ui.End();
    }

    private void DrawHud(PlaySession session)
    {
        var pack = _manifest.Packs.First(p => p.Stages.Contains(session.Stage));
        _ui.Panel(20, 18, 460, 96, _ui["ink"] * 0.72f);
        _ui.Text(Font.Title, 16, $"{pack.Title.ToUpperInvariant()}  ·  STAGE {session.Index + 1} OF {session.Count}", new Vector2(40, 32), _ui["bandOnDark"], spacing: 1);
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

        List<(string, string)> hints = [("W A S D", "Move"), ("Q E", "Turn"), ("V", "View"), ("Z", "Undo"), ("R", "Restart")];
        if (several)
        {
            hints.Add(("C", "Switch"));
        }

        hints.Add(("Esc", "Pause"));
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

    private void DrawIntro(PlaySession session)
    {
        var fade = Math.Clamp((IntroTicks - session.Ticks) / 15f, 0, 1);
        _ui.Panel(340, 520, 600, 110, _ui["ink"] * (0.8f * fade));
        _ui.Text(Font.Title, 18, $"STAGE {session.Index + 1}", new Vector2(640, 536), _ui["ember"] * fade, 0.5f, 3);
        _ui.Text(Font.BodyBold, 40, session.Stage.Title, new Vector2(640, 562), _ui["cream"] * fade, 0.5f);
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
        var next = session.HasNext ? $"Next: {session.StageAt(session.Index + 1).Title}" : "Pack complete!";
        _ui.Text(Font.Body, 24, next, new Vector2(640, 390), _ui["bandOnDark"] * rise, 0.5f);
    }
}
