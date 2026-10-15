using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Save;

var content = Path.Combine(AppContext.BaseDirectory, "content");
if (args.Contains("--smoke-test"))
{
    // shortcut: a WinExe has no console, so on Windows the report shows only when output is redirected (as in CI); the
    // exit code always works. Attach to the parent console if players ever need to read it.
    return SmokeTest.Run(content, Console.Out);
}

SmokeGui? smoke = null;
string? screens = null;
if (Array.IndexOf(args, "--smoke-gui-screens") is var screensAt and >= 0)
{
    if (screensAt + 1 >= args.Length)
    {
        Console.Error.WriteLine("--smoke-gui-screens needs the folder to save the screenshots in.");
        return 2;
    }

    screens = args[screensAt + 1];
}

if (Array.IndexOf(args, "--smoke-gui") is var at and >= 0)
{
    if (at + 1 >= args.Length)
    {
        Console.Error.WriteLine("--smoke-gui needs the path of the PNG to write.");
        return 2;
    }

    smoke = new SmokeGui(args[at + 1], screens);
}
else if (screens is not null)
{
    smoke = new SmokeGui(Path.Combine(screens, "smoke.png"), screens);
}

string dataDir;
if (Array.IndexOf(args, "--data-dir") is var dataAt and >= 0)
{
    if (dataAt + 1 >= args.Length)
    {
        Console.Error.WriteLine("--data-dir needs the folder to keep the save in.");
        return 2;
    }

    dataDir = args[dataAt + 1];
}
else
{
    // A smoke run never reads or writes the player's own save.
    dataDir = smoke is null ? SaveStore.DefaultFolder() : Directory.CreateTempSubdirectory("puzzle-boy-community-smoke-").FullName;
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

var save = SaveStore.Load(dataDir);

// Some GL drivers (Mesa under WSLg, for one) offer no multisampled context, and MonoGame then can't create a device at
// all. The game looks a little rougher without it but plays the same.
try
{
    return Run(multisampling: true);
}
catch (NoSuitableGraphicsDeviceException)
{
    Console.Error.WriteLine("The graphics driver offers no antialiasing, so the game starts without it.");
    try
    {
        return Run(multisampling: false);
    }
    catch (NoSuitableGraphicsDeviceException e)
    {
        Console.Error.WriteLine($"Puzzle Boy Community Edition can't start: no graphics device works here ({e.Message}).");
        return 1;
    }
}

int Run(bool multisampling)
{
    using var game = new CommunityGame(manifest, save, smoke, multisampling);
    game.Run();
    return game.ExitCode;
}
