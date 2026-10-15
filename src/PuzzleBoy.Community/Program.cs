using PuzzleBoy.Community;
using PuzzleBoy.Community.Content;

var content = Path.Combine(AppContext.BaseDirectory, "content");
if (args.Contains("--smoke-test"))
{
    // shortcut: a WinExe has no console, so on Windows the report shows only when output is redirected (as in CI); the
    // exit code always works. Attach to the parent console if players ever need to read it.
    return SmokeTest.Run(content, Console.Out);
}

SmokeGui? smoke = null;
if (Array.IndexOf(args, "--smoke-gui") is var at and >= 0)
{
    if (at + 1 >= args.Length)
    {
        Console.Error.WriteLine("--smoke-gui needs the path of the PNG to write.");
        return 2;
    }

    smoke = new SmokeGui(args[at + 1]);
}

var problems = new List<string>();
var manifest = ContentManifest.Load(content, problems);
if (manifest is null || !manifest.Packs.Any(p => p.Stages.Count > 0))
{
    // shortcut: only a damaged install gets here, and it says why on stderr alone; a message box can come with C5.
    Console.Error.WriteLine($"Puzzle Boy Community Edition can't start: the content beside it is damaged ({content}).");
    foreach (var problem in problems)
    {
        Console.Error.WriteLine($"problem: {problem}");
    }

    return 1;
}

using var game = new CommunityGame(manifest, smoke);
game.Run();
return game.ExitCode;
