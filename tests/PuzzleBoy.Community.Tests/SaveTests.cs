using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Tests;

public class SaveTests
{
    private static readonly StagePack FirstSteps = ContentManifest.Load(Repo.Content, [])!.Packs[0];

    private static string Id(int index) => FirstSteps.Stages[index].Id;

    [Fact]
    public void A_fresh_profile_starts_with_defaults_and_no_notice()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(Path.Combine(temp.Path, "new"));
        Assert.Empty(store.Data.Stages);
        Assert.False(store.Data.Settings.Fullscreen);
        Assert.Null(store.Notice);
        Assert.False(File.Exists(store.MainPath));
    }

    [Fact]
    public void Progress_and_settings_survive_a_relaunch()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 7, 4.25);
        store.Data.Settings.MusicVolume = 0.3f;
        store.Data.Settings.HighContrast = true;
        Assert.True(store.Save());

        var again = SaveStore.Load(temp.Path);
        Assert.True(again.Data.IsCompleted(Id(0)));
        Assert.Equal(7, again.Data.Stages[Id(0)].BestMoves);
        Assert.Equal(4.25, again.Data.Stages[Id(0)].BestTime);
        Assert.Equal(0.3f, again.Data.Settings.MusicVolume);
        Assert.True(again.Data.Settings.HighContrast);
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(again.MainPath));
    }

    [Fact]
    public void A_write_keeps_the_previous_save_as_the_backup_and_leaves_no_temp_file()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 9, 5);
        store.Save();
        store.Data.Record(Id(1), 12, 8);
        store.Save();

        Assert.False(File.Exists(store.TempPath));
        var backup = File.ReadAllText(store.BackupPath);
        Assert.Contains(Id(0), backup);
        Assert.DoesNotContain(Id(1), backup);
    }

    [Fact]
    public void A_crash_before_the_replace_leaves_the_old_save_whole_and_the_next_launch_reads_it()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 9, 5);
        store.Save();

        store.Data.Record(Id(1), 12, 8);
        store.BeforeReplace = () => throw new IOException("power cut");
        Assert.False(store.Save());
        Assert.True(File.Exists(store.TempPath));

        var after = SaveStore.Load(temp.Path);
        Assert.True(after.Data.IsCompleted(Id(0)));
        Assert.False(after.Data.IsCompleted(Id(1)));
        Assert.Null(after.Notice);
    }

    [Fact]
    public void A_half_written_temp_file_left_by_a_crash_is_ignored_and_replaced_by_the_next_write()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 9, 5);
        store.Save();
        File.WriteAllText(store.TempPath, "{\"schemaVersion\": 1, \"stages\": {\"first-st");

        var after = SaveStore.Load(temp.Path);
        Assert.True(after.Data.IsCompleted(Id(0)));
        Assert.Null(after.Notice);
        Assert.True(after.Save());
        Assert.False(File.Exists(after.TempPath));
    }

    [Fact]
    public void A_corrupted_save_falls_back_to_the_backup_with_a_notice_and_keeps_a_copy()
    {
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 9, 5);
        store.Save();
        store.Data.Record(Id(1), 12, 8);
        store.Save();
        File.WriteAllText(store.MainPath, "{ not json");

        var after = SaveStore.Load(temp.Path);
        Assert.True(after.Data.IsCompleted(Id(0)));
        Assert.False(after.Data.IsCompleted(Id(1)));
        Assert.Contains("backup was loaded", after.Notice);
        Assert.Equal("{ not json", File.ReadAllText(after.MainPath + ".damaged"));

        // The next write mustn't turn the damaged file into the backup.
        Assert.True(after.Save());
        Assert.Contains(Id(0), File.ReadAllText(after.BackupPath));
        Assert.Null(SaveStore.Load(temp.Path).Notice);
    }

    [Fact]
    public void A_save_another_program_holds_loads_the_backup_without_ever_replacing_the_save()
    {
        // Only Windows refuses a read of a file another handle opened with FileShare.None.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows file sharing.");
        using var temp = new TempFolder();
        var store = SaveStore.Load(temp.Path);
        store.Data.Record(Id(0), 9, 5);
        store.Save();
        store.Data.Record(Id(1), 12, 8);
        store.Save();

        SaveStore held;
        using (new FileStream(store.MainPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            held = SaveStore.Load(temp.Path);
        }

        Assert.True(held.Data.IsCompleted(Id(0)));
        Assert.False(held.Data.IsCompleted(Id(1)));
        Assert.True(held.ReadOnly);
        Assert.Contains("left as it was", held.Notice);
        Assert.DoesNotContain(".damaged", held.Notice);
        Assert.False(File.Exists(store.MainPath + ".damaged"));

        held.Data.Record(Id(2), 1, 1);
        Assert.False(held.Save());
        Assert.True(SaveStore.Load(temp.Path).Data.IsCompleted(Id(1)));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"schemaVersion\": \"one\"}")]
    [InlineData("{\"schemaVersion\": 1, \"stages\": [1]}")]
    [InlineData("{\"schemaVersion\": 1, \"schemaVersion\": 1}")]
    [InlineData("{\"schemaVersion\": 1, \"stages\": {}, \"stages\": {}}")]
    public void A_damaged_save_with_no_good_backup_starts_fresh_with_a_notice(string text)
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, text);
        temp.Write(SaveStore.FileName + ".bak", "\0\0\0");

        var store = SaveStore.Load(temp.Path);
        Assert.Empty(store.Data.Stages);
        Assert.Contains("starts fresh", store.Notice);
        Assert.True(store.Save());
    }

    [Fact]
    public void A_legacy_schema_0_save_migrates_its_clears_and_volume()
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, $"{{\"cleared\": [\"{Id(0)}\", \"{Id(1)}\"], \"volume\": 0.4}}");

        var store = SaveStore.Load(temp.Path);
        Assert.Null(store.Notice);
        Assert.True(store.Data.IsCompleted(Id(0)));
        Assert.True(store.Data.IsCompleted(Id(1)));
        Assert.Null(store.Data.Stages[Id(0)].BestMoves);
        Assert.Equal(0.4f, store.Data.Settings.MusicVolume);
        Assert.Equal(0.4f, store.Data.Settings.SfxVolume);

        store.Save();
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(store.MainPath));
        Assert.True(SaveStore.Load(temp.Path).Data.IsCompleted(Id(1)));

        // The first real clear of a migrated stage sets its first best.
        Assert.True(store.Data.Record(Id(0), 30, 20));
    }

    [Fact]
    public void A_schema_1_save_migrates_to_schema_2_with_its_progress_and_settings_and_the_theme_on_auto()
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, $"{{\"schemaVersion\": 1, \"settings\": {{\"musicVolume\": 0.3, \"highContrast\": true}}, \"stages\": {{\"{Id(0)}\": {{\"completed\": true, \"bestMoves\": 12, \"bestTime\": 9.5}}, \"classic/original/02\": {{\"completed\": true}}}}}}");

        var store = SaveStore.Load(temp.Path);
        Assert.Null(store.Notice);
        Assert.Equal(Themes.Auto, store.Data.Settings.Theme);
        Assert.Equal((0.3f, true), (store.Data.Settings.MusicVolume, store.Data.Settings.HighContrast));
        Assert.Equal((12, 9.5), (store.Data.Stages[Id(0)].BestMoves, store.Data.Stages[Id(0)].BestTime));
        Assert.True(store.Data.IsCompleted("classic/original/02"));

        store.Data.Settings.Theme = Themes.Classic;
        Assert.True(store.Save());
        var written = File.ReadAllText(store.MainPath);
        Assert.Contains("\"schemaVersion\": 2", written);
        Assert.Contains("\"theme\": \"classic\"", written);
        Assert.Equal(Themes.Classic, SaveStore.Load(temp.Path).Data.Settings.Theme);
    }

    [Theory]
    [InlineData("\"fancy\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void A_theme_this_version_doesnt_know_reads_as_auto_and_keeps_the_rest_of_the_save(string theme)
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, $"{{\"schemaVersion\": 2, \"settings\": {{\"theme\": {theme}, \"mute\": true}}, \"stages\": {{\"{Id(0)}\": {{\"completed\": true}}}}}}");

        var store = SaveStore.Load(temp.Path);
        Assert.Null(store.Notice);
        Assert.Equal(Themes.Auto, store.Data.Settings.Theme);
        Assert.True(store.Data.Settings.Mute);
        Assert.True(store.Data.IsCompleted(Id(0)));
    }

    [Fact]
    public void A_save_an_editor_gave_a_byte_order_mark_still_loads()
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, $"﻿{{\"schemaVersion\": 1, \"stages\": {{\"{Id(0)}\": {{\"completed\": true}}}}}}");

        var store = SaveStore.Load(temp.Path);
        Assert.Null(store.Notice);
        Assert.True(store.Data.IsCompleted(Id(0)));
    }

    [Fact]
    public void A_save_from_a_newer_version_is_never_overwritten()
    {
        using var temp = new TempFolder();
        var path = temp.Write(SaveStore.FileName, "{\"schemaVersion\": 99, \"future\": true}");

        var store = SaveStore.Load(temp.Path);
        Assert.True(store.ReadOnly);
        Assert.Contains("newer version", store.Notice);
        store.Data.Record(Id(0), 1, 1);
        Assert.False(store.Save());
        Assert.Equal("{\"schemaVersion\": 99, \"future\": true}", File.ReadAllText(path));
    }

    [Fact]
    public void Out_of_range_values_in_a_hand_edited_save_are_brought_back_in_range()
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, "{\"schemaVersion\": 1, \"settings\": {\"musicVolume\": 7, \"sfxVolume\": -1, \"windowScale\": 9, \"uiScale\": 0.1}, \"stages\": {\"a/b\": {\"completed\": true, \"bestMoves\": -3}}}");

        var data = SaveStore.Load(temp.Path).Data;
        Assert.Equal(1f, data.Settings.MusicVolume);
        Assert.Equal(0f, data.Settings.SfxVolume);
        Assert.Equal(2f, data.Settings.WindowScale);
        Assert.Equal(0.9f, data.Settings.UiScale);
        Assert.Empty(data.Stages);
    }

    [Fact]
    public void A_best_time_too_big_to_write_back_is_dropped_on_load()
    {
        using var temp = new TempFolder();
        temp.Write(SaveStore.FileName, "{\"schemaVersion\": 1, \"stages\": {\"a/b\": {\"completed\": true, \"bestTime\": 1e400}}}");

        var store = SaveStore.Load(temp.Path);
        Assert.Empty(store.Data.Stages);
        Assert.True(store.Save());
    }

    [Fact]
    public void A_folder_that_cant_be_written_gives_a_notice_instead_of_a_crash()
    {
        using var temp = new TempFolder();
        var file = temp.Write("in-the-way", "a file where the folder should be");
        var store = SaveStore.Load(file);
        Assert.False(store.Save());
        Assert.Contains("couldn't be saved", store.Notice);
    }

    [Theory]
    [InlineData(true, false, null, @"C:\Users\p\AppData\Roaming", "PuzzleBoyCommunity")]
    [InlineData(false, true, null, "/Users/p", "/Users/p/Library/Application Support/PuzzleBoyCommunity")]
    [InlineData(false, false, "/data", "/home/p", "/data/puzzle-boy-community")]
    [InlineData(false, false, null, "/home/p", "/home/p/.local/share/puzzle-boy-community")]
    [InlineData(false, false, "relative/ignored", "/home/p", "/home/p/.local/share/puzzle-boy-community")]
    public void The_save_folder_follows_each_os_convention(bool windows, bool mac, string? xdg, string home, string expected)
    {
        var folder = SaveStore.DefaultFolder(windows, mac, appData: home, home, xdg).Replace('\\', '/');
        Assert.EndsWith(expected.Replace('\\', '/'), folder);
    }

    [Fact]
    public void A_clear_opens_the_two_stages_after_it_so_one_stage_can_be_skipped()
    {
        var data = new SaveData();
        Assert.Equal([true, false, false, false], Unlocked(data));
        data.Record(Id(0), 3, 1);
        Assert.Equal([true, true, true, false], Unlocked(data));

        // Skipping stage 2 and clearing stage 3 goes on from there; a stage cleared out of order stays playable.
        var skipped = new SaveData();
        skipped.Record(Id(2), 3, 1);
        Assert.Equal([true, false, true, true], Unlocked(skipped));
    }

    [Fact]
    public void Play_starts_the_first_unlocked_stage_not_yet_cleared_or_the_last_when_all_are()
    {
        var data = new SaveData();
        Assert.Equal(0, data.NextToPlay([FirstSteps]).Index);
        data.Record(Id(0), 3, 1);
        Assert.Equal(1, data.NextToPlay([FirstSteps]).Index);
        foreach (var stage in FirstSteps.Stages)
        {
            data.Record(stage.Id, 3, 1);
        }

        Assert.Equal(FirstSteps.Stages.Count - 1, data.NextToPlay([FirstSteps]).Index);
    }

    [Fact]
    public void A_clear_keeps_the_best_moves_and_time_separately_and_says_when_one_improved()
    {
        var data = new SaveData();
        Assert.True(data.Record("p/s", 10, 9.0));
        Assert.False(data.Record("p/s", 12, 9.5));
        Assert.True(data.Record("p/s", 11, 6.0));
        Assert.Equal(10, data.Stages["p/s"].BestMoves);
        Assert.Equal(6.0, data.Stages["p/s"].BestTime);
    }

    [Fact]
    public void The_stage_clock_stops_at_the_clear()
    {
        var session = new PlaySession(FirstSteps.Stages, 0);
        var solution = File.ReadAllText(Path.Combine(Repo.Root, "tests", "PuzzleBoy.Community.Tests", "Solutions", $"{Id(0)}.txt")).Trim();
        foreach (var move in solution)
        {
            session.Step(move switch { 'R' => Direction.PlusX, 'L' => Direction.MinusX, 'D' => Direction.PlusY, _ => Direction.MinusY });
            while (session.Animation is not null)
            {
                session.Tick();
            }
        }

        Assert.True(session.Board.IsCleared);
        var atClear = session.ElapsedTicks;
        Assert.True(atClear > 0);
        for (var i = 0; i < 40; i++)
        {
            session.Tick();
        }

        Assert.Equal(atClear, session.ElapsedTicks);
        session.Restart();
        Assert.Equal(0, session.ElapsedTicks);
    }

    [Theory]
    [InlineData(1f, 1920, 1080, 1280, 720)]
    [InlineData(1.5f, 1920, 1080, 1728, 972)]
    [InlineData(2f, 1920, 1080, 1728, 972)]
    [InlineData(1f, 1280, 720, 1152, 648)]
    [InlineData(0.75f, 1366, 768, 960, 540)]
    public void A_window_scale_too_big_for_the_display_shrinks_to_nine_tenths_of_it(float scale, int displayWidth, int displayHeight, int width, int height) =>
        Assert.Equal((width, height), GameSettings.WindowSize(scale, displayWidth, displayHeight));

    [Fact]
    public void Stepping_an_option_holds_at_the_ends()
    {
        Assert.Equal(1.25f, GameSettings.Step(GameSettings.WindowScales, 1f, 1));
        Assert.Equal(0.75f, GameSettings.Step(GameSettings.WindowScales, 0.75f, -1));
        Assert.Equal(2f, GameSettings.Step(GameSettings.WindowScales, 2f, 1));
    }

    [Fact]
    public void Times_read_as_minutes_seconds_and_tenths()
    {
        Assert.Equal("0:04.2", CommunityGame.FormatTime(4.25));
        Assert.Equal("1:05.0", CommunityGame.FormatTime(65));
    }

    // The first three stages of the pack are enough to show the rule.
    private static bool[] Unlocked(SaveData data) => [.. Enumerable.Range(0, 4).Select(i => data.IsUnlocked(FirstSteps.Stages, i))];
}
