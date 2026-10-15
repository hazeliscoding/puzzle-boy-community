using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Play;

/// <summary>
/// One tick of play input, the same in both themes: the play camera (<see cref="CameraRig"/>) turns the controls with the
/// view and holds steps while it turns, whichever theme draws. A theme only draws and sounds what this did.
/// </summary>
internal static class PlayInput
{
    /// <summary>
    /// Ticks after a stage starts or restarts before input is read, as in the original: it reads its first input on the
    /// third tick.
    /// </summary>
    public const int StartLock = 2;

    /// <summary>
    /// Applies <paramref name="controls"/> to <paramref name="session"/> through <paramref name="camera"/>: scroll mode,
    /// view turns, undo, restart, switch, then a held direction. Like the original, it reads nothing at all while a step
    /// plays: a press then is lost, not queued. Returns the sounds that go with what happened, in order.
    /// </summary>
    public static List<Sfx> Apply(PlaySession session, CameraRig camera, Controls controls)
    {
        var sounds = new List<Sfx>();
        if (session.Busy || session.Ticks <= StartLock)
        {
            return sounds;
        }

        if (controls.Pressed(Command.Scroll))
        {
            session.ToggleScroll();
            sounds.Add(Sfx.View);
        }

        if ((controls.Pressed(Command.TurnLeft) && camera.TurnLeft())
            || (controls.Pressed(Command.TurnRight) && camera.TurnRight())
            || (controls.Pressed(Command.Overhead) && camera.ToggleOverhead()))
        {
            sounds.Add(Sfx.View);
        }

        if (controls.Pressed(Command.Undo))
        {
            sounds.Add(session.Undo() ? Sfx.Cancel : Sfx.Buzz);
        }

        if (controls.Pressed(Command.Restart))
        {
            session.Restart();
            sounds.Add(Sfx.Confirm);
        }

        if (controls.Pressed(Command.Switch))
        {
            sounds.Add(session.Switch() ? Sfx.View : Sfx.Buzz);
        }

        // Input follows the view; a held direction keeps walking, or in scroll mode moves the view a cell a tick, as in
        // the original. Steps wait for a turn of the view to finish.
        if (session.Scroll is not null && controls.HeldDirection() is { } look)
        {
            session.ScrollBy(camera.DirectionFor(look));
        }
        else if (session.Scroll is null && !camera.IsBusy && controls.HeldDirection() is { } screen)
        {
            var result = session.Step(camera.DirectionFor(screen));
            if (result != StepResult.Refused)
            {
                sounds.Add(result switch { StepResult.Walked => Sfx.Step, StepResult.Pushed => Sfx.Push, _ => Sfx.Turn });
            }
        }

        return sounds;
    }
}
