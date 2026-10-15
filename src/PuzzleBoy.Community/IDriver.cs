using PuzzleBoy.Community.App;

namespace PuzzleBoy.Community;

/// <summary>Plays the window in place of the player's keyboard, pad and mouse: a smoke run or a recording.</summary>
internal interface IDriver
{
    /// <summary>The process exit code the run ends with: 0 once it has finished well.</summary>
    int ExitCode { get; }

    /// <summary>True once the run has finished or failed and is only waiting to exit.</summary>
    bool Done { get; }

    /// <summary>The input for this tick, in place of the real keyboard and pad.</summary>
    Controls Input(CommunityGame game);

    /// <summary>Called after each frame is drawn, before it's shown.</summary>
    void AfterDraw(CommunityGame game);
}
