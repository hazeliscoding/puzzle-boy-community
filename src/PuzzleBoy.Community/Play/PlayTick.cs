using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.Play;

/// <summary>What the play screen does after a tick of play.</summary>
internal enum PlayNext
{
    /// <summary>Play goes on.</summary>
    Play,

    /// <summary>The clear's celebration is over: on to the next stage, the stage list or the editor.</summary>
    Continue,

    /// <summary>The player left a playtest for the editor.</summary>
    LeavePlaytest,

    /// <summary>The player paused.</summary>
    Pause,
}

/// <summary>
/// One tick of the play screen with no graphics device, as <c>CommunityGame</c> runs it after the play camera's tick: the
/// session, the Classic view when that theme draws, the clear's record, then input. The game plays the sounds and does
/// what it returns; tests run the same tick with and without a Classic view to show that the theme changes nothing.
/// </summary>
internal static class PlayTick
{
    /// <summary>
    /// Runs one tick of <paramref name="session"/>. <paramref name="classic"/> is the Classic theme's view, or null in the
    /// Default theme. A clear is recorded in <paramref name="progress"/> (null in a playtest, which records nothing), and
    /// <paramref name="newBest"/> then says whether it was a new best, else it's null. Community's sounds go to
    /// <paramref name="sounds"/> and the Classic view's own numbered effects to <paramref name="effects"/>, in order.
    /// </summary>
    public static PlayNext Run(PlaySession session, CameraRig camera, ClassicStageView? classic, Controls controls, bool playtest, SaveData? progress, List<Sfx> sounds, List<int> effects, out bool? newBest)
    {
        newBest = null;
        var events = session.Tick();
        if (classic is not null)
        {
            effects.AddRange(classic.Tick(session, camera.Mode, camera.Overhead));
        }

        if (events.HasFlag(PlayEvents.FallStarted))
        {
            sounds.Add(Sfx.Fall);
        }

        if (events.HasFlag(PlayEvents.Cleared))
        {
            sounds.Add(Sfx.Clear);
            if (progress is not null)
            {
                newBest = progress.Record(session.Stage.Id, session.Moves, session.ElapsedTicks / 30.0);
            }
        }
        else if (events.HasFlag(PlayEvents.ReachedGoal))
        {
            sounds.Add(Sfx.Goal);
        }

        if (session.ReadyToContinue)
        {
            return PlayNext.Continue;
        }

        // Esc goes straight back to the editor from a playtest; P still pauses. Backspace and B also mean Undo, so not them.
        if (playtest && controls.Pressed(Command.Back) && !controls.Pressed(Command.Undo))
        {
            return PlayNext.LeavePlaytest;
        }

        if (controls.Pressed(Command.Pause))
        {
            sounds.Add(Sfx.Confirm);
            return PlayNext.Pause;
        }

        sounds.AddRange(PlayInput.Apply(session, camera, controls));
        return PlayNext.Play;
    }
}
