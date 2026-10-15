using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

/// <summary>
/// The theme only picks the presenter: the same keys, ticked the way the game ticks them, give the same boards, move
/// counts, undo, clock and saved progress whether the Classic theme's view watches the stage or not.
/// </summary>
public class ThemeSwitchTests
{
    private static readonly List<PackStage> Bundled = [.. ContentManifest.Load(Repo.Content, [])!.Packs.SelectMany(p => p.Stages)];

    public static TheoryData<string> BundledIds => [.. Bundled.Select(s => s.Id)];

    private sealed record Outcome(string Board, int Moves, int Undo, int ElapsedTicks, string Progress, List<Sfx> Sounds);

    /// <summary>
    /// The stage's recorded solution with view turns and overhead switches mixed in, so the controls turn with the view:
    /// <c>E</c>, <c>Q</c> and <c>V</c> press those keys, <c>S</c> switches, <c>z</c> undoes and <c>r</c> restarts.
    /// </summary>
    private static List<char> Plan(string id)
    {
        var moves = File.ReadAllText(Path.Combine(Repo.Root, "tests", "PuzzleBoy.Community.Tests", "Solutions", $"{id}.txt")).Trim();
        var plan = new List<char>();
        for (var i = 0; i < moves.Length; i++)
        {
            plan.AddRange(i % 5 == 2 ? "E" : i % 7 == 4 ? "Q" : i == 3 ? "V" : i == 8 ? "V" : "");
            plan.Add(moves[i]);
        }

        return plan;
    }

    /// <summary>
    /// Plays <paramref name="stage"/> by <paramref name="plan"/> through <see cref="PlayTick"/>, the tick the game's
    /// UpdatePlaying runs, after the play camera's tick as the game's Update does, and with the Classic view drawn after
    /// each tick when there is one. The clear is recorded by that tick, as in the game.
    /// </summary>
    private static Outcome Replay(PackStage stage, List<char> plan, bool classic, out ClassicStageView? view, out CameraRig rig)
    {
        var session = new PlaySession([stage], 0);
        var camera = rig = new CameraRig();
        view = classic ? new ClassicStageView(ClassicFixture.Character) : null;
        var save = new SaveData();
        var sounds = new List<Sfx>();
        var next = 0;
        Keys[] previous = [];
        for (var tick = 0; tick < 30000; tick++)
        {
            Keys[] held = [];
            var step = next < plan.Count ? plan[next] : ' ';
            var key = step switch
            {
                'E' => Keys.E,
                'Q' => Keys.Q,
                'V' => Keys.V,
                'S' => Keys.C,
                'z' => Keys.Z,
                'r' => Keys.R,
                'R' or 'L' or 'D' or 'U' => new[] { Keys.Up, Keys.Right, Keys.Down, Keys.Left }[Enumerable.Range(0, 4).First(s => camera.DirectionFor(s) == step switch
                {
                    'R' => Direction.PlusX,
                    'L' => Direction.MinusX,
                    'D' => Direction.PlusY,
                    _ => Direction.MinusY,
                })],
                _ => Keys.None,
            };

            // A press needs the key up the tick before; a direction may be held.
            if (key != Keys.None && (step is 'R' or 'L' or 'D' or 'U' || !previous.Contains(key)))
            {
                held = [key];
            }

            var moves = session.Moves;
            camera.Tick();
            var heard = new List<Sfx>();
            var after = PlayTick.Run(session, camera, view, Controls.FromKeys(held, previous), playtest: false, save, heard, [], out _);
            sounds.AddRange(heard);
            if (after == PlayNext.Continue)
            {
                break;
            }

            if (view is not null)
            {
                view.Pose(session);
                view.View(session);
            }

            if (session.Moves > moves || (step is 'E' or 'Q' or 'V' or 'S' && heard.Contains(Sfx.View)) || (step is 'z' or 'r' && heard.Count > 0))
            {
                next++;

                // Let go after each input, so the next one is read fresh.
                held = [];
            }

            previous = held;
        }

        var board = string.Join(',', Enumerable.Range(0, Stage.Width * Stage.Height).Select(i => session.Board[i % Stage.Width, i / Stage.Width]))
            + string.Join(';', session.Board.Characters) + session.Board.IsCleared;
        return new Outcome(board, session.Moves, session.UndoAvailable, session.ElapsedTicks, JsonSerializer.Serialize(save.Stages), sounds);
    }

    [Theory]
    [MemberData(nameof(BundledIds))]
    public void Every_bundled_stage_plays_out_the_same_in_both_themes(string id)
    {
        var stage = Bundled.Single(s => s.Id == id);
        var plan = Plan(id);
        var standard = Replay(stage, plan, classic: false, out _, out _);
        var classic = Replay(stage, plan, classic: true, out var view, out var camera);

        Assert.EndsWith("True", standard.Board);
        Assert.Equal(standard.Board, classic.Board);
        Assert.Equal((standard.Moves, standard.Undo, standard.ElapsedTicks), (classic.Moves, classic.Undo, classic.ElapsedTicks));
        Assert.Equal(standard.Progress, classic.Progress);
        Assert.Contains(id, classic.Progress);
        Assert.Equal(standard.Sounds, classic.Sounds);

        // The original's camera, slower to turn, catches up with the play camera the controls follow.
        var idle = new PlaySession([stage], 0);
        for (var tick = 0; tick < 40; tick++)
        {
            view!.Tick(idle, camera.Mode, camera.Overhead);
        }

        Assert.Equal(camera.Mode, view!.Camera.Mode);
    }

    [Fact]
    public void Undo_and_restart_play_out_the_same_in_both_themes()
    {
        var stage = Bundled.Single(s => s.Id == "first-steps/nudge");
        var plan = Plan(stage.Id);
        foreach (var undo in new[] { "r", "rzr", "zr" })
        {
            // Three inputs, then the undos and restarts, which end on a fresh start, then the whole solution.
            var mixed = plan.Take(3).Concat(undo).Concat(plan).ToList();
            var standard = Replay(stage, mixed, classic: false, out _, out _);
            var classic = Replay(stage, mixed, classic: true, out _, out _);
            Assert.EndsWith("True", standard.Board);
            Assert.Equal((standard.Board, standard.Moves, standard.Undo, standard.ElapsedTicks, standard.Progress), (classic.Board, classic.Moves, classic.Undo, classic.ElapsedTicks, classic.Progress));
            Assert.Equal(standard.Sounds, classic.Sounds);
        }
    }

    [Fact]
    public void The_classic_view_sounds_a_fall_on_its_fourth_tick_and_the_goal_on_the_third_as_the_original_does()
    {
        var push = new PlaySession([new PackStage("t/fall", "Fall", SolverTests.Draw("1bo.G"))], 0);
        var view = new ClassicStageView(ClassicFixture.Character);
        Assert.Equal(StepResult.Pushed, push.Step(Direction.PlusX));
        var heard = new List<(int Tick, int Sound)>();
        for (var tick = 1; tick <= 10; tick++)
        {
            push.Tick();
            heard.AddRange(view.Tick(push, 0, false).Select(s => (tick, s)));
        }

        Assert.Equal([(4, 3)], heard);

        var walk = new PlaySession([new PackStage("t/goal", "Goal", SolverTests.Draw("1G"))], 0);
        heard.Clear();
        Assert.Equal(StepResult.Walked, walk.Step(Direction.PlusX));
        for (var tick = 1; tick <= 6; tick++)
        {
            walk.Tick();
            heard.AddRange(view.Tick(walk, 0, false).Select(s => (tick, s)));
        }

        Assert.Equal([(3, 9)], heard);
    }

    [Fact]
    public void The_classic_theme_maps_communitys_sounds_to_the_originals_numbered_effects()
    {
        Assert.Equal([0, 1, 2, null, null, null, 8, 6, 4, 5, 7], Enum.GetValues<Sfx>().Select(ClassicPresenter.Effect));
    }

    [Fact]
    public void The_classic_camera_catches_up_the_way_the_player_turned_even_overhead_where_it_turns_slower()
    {
        var session = new PlaySession([new PackStage("t/turn", "Turn", SolverTests.Draw("1G"))], 0);
        var rig = new CameraRig();
        var view = new ClassicStageView(ClassicFixture.Character);
        Assert.True(rig.ToggleOverhead());
        var (lefts, lastLeft) = (0, 0);
        List<float> yaws = [];
        for (var tick = 1; tick <= 90; tick++)
        {
            rig.Tick();
            view.Tick(session, rig.Mode, rig.Overhead);
            yaws.Add(view.Camera.Yaw);
            if (lefts < 4 && rig.TurnLeft())
            {
                (lefts, lastLeft) = (lefts + 1, tick);
            }
        }

        // Four quick left turns pile up behind the original's 12-tick overhead turn; it makes every one of them to the left,
        // a full turn round, and never the shorter-looking way back to the right.
        Assert.Equal(4, lefts);
        // (An overhead turn's 12 steps of 8.19° overshoot by 0.09° and snap back on the last, as the original's do.)
        Assert.All(yaws.Zip(yaws.Skip(1)), pair => Assert.True(pair.Second <= pair.First + 0.1f, $"the view turned right, {pair.First} to {pair.Second}"));
        Assert.Equal(-360f, yaws[^1]);
        Assert.Equal(rig.Mode, view.Camera.Mode);

        // The overhead lag: the play camera finished its last turn 7 ticks after it started; the original's view, a
        // turn of 12 ticks per quarter, ends well after.
        Assert.True(yaws.FindIndex(y => y == -360f) + 1 > lastLeft + 7);
    }
}
