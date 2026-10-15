using PuzzleBoy.Community;

if (args.Contains("--smoke-test"))
{
    // shortcut: a WinExe has no console, so on Windows the report shows only when output is redirected (as in CI); the
    // exit code always works. Attach to the parent console if players ever need to read it.
    return SmokeTest.Run(Path.Combine(AppContext.BaseDirectory, "content"), Console.Out);
}

using var game = new CommunityGame();
game.Run();
return 0;
