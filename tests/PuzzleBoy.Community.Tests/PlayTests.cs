using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class PlayTests
{
    private static readonly IReadOnlyList<PackStage> FirstSteps = ContentManifest.Load(Repo.Content, [])!.Packs[0].Stages;

    /// <summary>Takes a step and plays out its animation, as the game does between inputs.</summary>
    private static StepResult Play(PlaySession session, Direction direction)
    {
        var result = session.Step(direction);
        while (session.Animation is not null)
        {
            session.Tick();
        }

        return result;
    }

    private static PlaySession At(string id) => new(FirstSteps, FirstSteps.ToList().FindIndex(s => s.Id == id));

    private static void Settle(CameraRig camera)
    {
        while (camera.IsBusy)
        {
            camera.Tick();
        }
    }

    [Theory]
    [InlineData(0, 0, Direction.PlusY)]
    [InlineData(0, 1, Direction.PlusX)]
    [InlineData(1, 0, Direction.MinusX)]
    [InlineData(1, 1, Direction.PlusY)]
    [InlineData(2, 0, Direction.MinusY)]
    [InlineData(3, 0, Direction.PlusX)]
    [InlineData(3, 3, Direction.PlusY)]
    public void Input_follows_the_view_after_each_right_turn(int rightTurns, int screen, Direction expected)
    {
        var camera = new CameraRig();
        for (var i = 0; i < rightTurns; i++)
        {
            Assert.True(camera.TurnRight());
            Settle(camera);
        }

        Assert.Equal(expected, camera.DirectionFor(screen));
    }

    [Fact]
    public void A_left_turn_undoes_a_right_turn()
    {
        var camera = new CameraRig();
        camera.TurnRight();
        Settle(camera);
        camera.TurnLeft();
        Assert.Equal(0, camera.Mode);
    }

    [Fact]
    public void The_view_takes_no_new_turn_until_the_last_one_lands()
    {
        var camera = new CameraRig();
        Assert.True(camera.TurnRight());
        Assert.False(camera.TurnRight());
        Assert.False(camera.ToggleOverhead());
        Assert.Equal(1, camera.Mode);
        Settle(camera);
        Assert.True(camera.ToggleOverhead());
    }

    [Fact]
    public void Steps_wait_for_the_animation_and_every_animation_is_short()
    {
        var session = At("first-steps/nudge");
        Assert.Equal(StepResult.Walked, session.Step(Direction.PlusX));
        Assert.Equal(StepResult.Refused, session.Step(Direction.PlusX));
        var ticks = 0;
        while (session.Animation is not null)
        {
            session.Tick();
            ticks++;
        }

        Assert.InRange(ticks, 4, 9);
        Assert.Equal(StepResult.Pushed, Play(session, Direction.PlusX));
    }

    [Fact]
    public void Undo_takes_back_a_push_with_the_walking_since_and_restores_the_move_count()
    {
        var session = At("first-steps/nudge");
        Play(session, Direction.PlusX);
        Assert.Equal(StepResult.Pushed, Play(session, Direction.PlusX));
        Assert.Equal(StepResult.Pushed, Play(session, Direction.PlusX));
        Assert.Equal(StepResult.Walked, Play(session, Direction.PlusY));
        Assert.Equal((4, 2), (session.Moves, session.UndoAvailable));

        Assert.True(session.Undo());
        Assert.Equal((2, 1), (session.Moves, session.UndoAvailable));
        Assert.Equal((9, 8), (session.Board.Active.X, session.Board.Active.Y));
        Assert.True(session.Undo());
        Assert.Equal((1, 0), (session.Moves, session.UndoAvailable));
        Assert.Equal((8, 8), (session.Board.Active.X, session.Board.Active.Y));
        Assert.False(session.Undo());
    }

    [Fact]
    public void Restart_puts_the_stage_back_and_zeroes_the_count()
    {
        var session = At("first-steps/nudge");
        Play(session, Direction.PlusX);
        session.Restart();
        Assert.Equal(0, session.Moves);
        Assert.Equal((7, 8), (session.Board.Active.X, session.Board.Active.Y));
    }

    [Fact]
    public void Undo_after_a_restart_brings_back_the_board_the_count_and_the_undo_history()
    {
        var session = At("first-steps/nudge");
        Play(session, Direction.PlusX);
        Play(session, Direction.PlusX);
        Play(session, Direction.PlusX);
        Assert.Equal((3, 2), (session.Moves, session.UndoAvailable));

        session.Restart();
        session.Restart();
        Assert.Equal((0, 1), (session.Moves, session.UndoAvailable));
        Play(session, Direction.PlusY);

        Assert.True(session.Undo());
        Assert.Equal((3, 2), (session.Moves, session.UndoAvailable));
        Assert.Equal((10, 8), (session.Board.Active.X, session.Board.Active.Y));
        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.False(session.Undo());
        Assert.Equal((1, 0), (session.Moves, session.UndoAvailable));
    }

    [Fact]
    public void Switching_needs_more_than_one_visible_character() =>
        Assert.False(At("first-steps/nudge").Switch());

    [Fact]
    public void A_cleared_stage_celebrates_then_continues_to_the_next_stage_of_the_pack()
    {
        var session = At("first-steps/nudge");
        foreach (var step in "RRRRDR")
        {
            Play(session, step == 'R' ? Direction.PlusX : Direction.PlusY);
        }

        session.Step(Direction.PlusY);
        Assert.True(session.Board.IsCleared);
        var events = PlayEvents.None;
        while (!session.ReadyToContinue)
        {
            events |= session.Tick();
        }

        Assert.Equal(PlaySession.CelebrationTicks, session.ClearTicks);
        Assert.Equal(PlayEvents.ReachedGoal | PlayEvents.Cleared, events);
        Assert.True(session.Next());
        Assert.Equal("first-steps/look-before-you-push", session.Stage.Id);
    }

    [Fact]
    public void The_last_stage_of_a_pack_has_no_next()
    {
        var session = new PlaySession(FirstSteps, FirstSteps.Count - 1);
        Assert.False(session.HasNext);
        Assert.False(session.Next());
        Assert.Equal(FirstSteps.Count - 1, session.Index);
    }

    [Fact]
    public void Next_starts_a_fresh_board_of_the_following_stage()
    {
        var session = new PlaySession(FirstSteps, 0);
        Play(session, Direction.PlusX);
        Assert.True(session.Next());
        Assert.Equal((1, 0, FirstSteps[1].Id), (session.Index, session.Moves, session.Stage.Id));
    }
}
