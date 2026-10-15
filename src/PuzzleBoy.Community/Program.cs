using Microsoft.Xna.Framework.Graphics;
using PuzzleBoy.Community;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Save;

var content = Path.Combine(AppContext.BaseDirectory, "content");
if (args.Contains("--smoke-test"))
{
    // shortcut: a WinExe has no console, so on Windows the report shows only when output is redirected (as in CI); the
    // exit code always works. Attach to the parent console if players ever need to read it.
    var smokeData = Array.IndexOf(args, "--data-dir") is var smokeDataAt and >= 0 && smokeDataAt + 1 < args.Length ? args[smokeDataAt + 1] : null;
    return SmokeTest.Run(content, Console.Out, smokeData);
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

IDriver? driver = smoke;
if (Array.IndexOf(args, "--record") is var recordAt and >= 0)
{
    if (recordAt + 2 >= args.Length)
    {
        Console.Error.WriteLine("--record needs the script to play and the folder to save the frames in.");
        return 2;
    }

    var every = Array.IndexOf(args, "--every") is var everyAt and >= 0 ? (everyAt + 1 < args.Length && int.TryParse(args[everyAt + 1], out var n) && n > 0 ? n : 0) : 1;
    var size = Array.IndexOf(args, "--size") is var sizeAt and >= 0 ? (sizeAt + 1 < args.Length ? args[sizeAt + 1] : "") : $"{VirtualScreen.Width}x{VirtualScreen.Height}";
    var wh = size.Split('x');
    if (every == 0 || wh.Length != 2 || !int.TryParse(wh[0], out var width) || !int.TryParse(wh[1], out var height) || width < 64 || height < 36)
    {
        Console.Error.WriteLine("--every needs a whole number of ticks, at least 1, and --size a size such as 1280x720.");
        return 2;
    }

    try
    {
        driver = new DemoRecorder(DemoRecorder.Parse(File.ReadAllText(args[recordAt + 1])), args[recordAt + 2], every, (width, height));
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
    {
        Console.Error.WriteLine($"--record can't use {args[recordAt + 1]}: {e.Message}");
        return 2;
    }
}

string dataDir;
if (Array.IndexOf(args, "--data-dir") is var dataAt and >= 0)
{
    // A blank path or a NUL would make Path.GetFullPath throw in SaveStore.Load.
    if (dataAt + 1 >= args.Length || string.IsNullOrWhiteSpace(args[dataAt + 1]) || args[dataAt + 1].Contains('\0'))
    {
        Console.Error.WriteLine("--data-dir needs the folder to keep the save in.");
        return 2;
    }

    dataDir = args[dataAt + 1];
}
else
{
    // A smoke run or a recording never reads or writes the player's own save.
    dataDir = driver is null ? SaveStore.DefaultFolder() : Directory.CreateTempSubdirectory("puzzle-boy-community-smoke-").FullName;
}

if (Array.IndexOf(args, "--import-classic") is var classicAt and >= 0)
{
    if (classicAt + 1 >= args.Length)
    {
        Console.Error.WriteLine("--import-classic needs the game's folder (the one that holds smt3hd.exe), or a classic pack's zip or folder.");
        return 2;
    }

    return ImportClassic(args[classicAt + 1], dataDir);
}

if (args.Contains("--export-classic"))
{
    return ExportClassic(dataDir);
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

if (Array.IndexOf(args, "--import-pack") is var importAt and >= 0)
{
    if (importAt + 1 >= args.Length)
    {
        Console.Error.WriteLine("--import-pack needs the .zip to import.");
        return 2;
    }

    // Titles can hold any letter, and the console's own code page might not.
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var choice = args.Contains("--replace") ? ImportChoice.Replace : args.Contains("--keep-both") ? ImportChoice.KeepBoth : ImportChoice.Ask;
    var result = PackArchive.Import(args[importAt + 1], dataDir, PackArchive.ReadKnown(), PackArchive.Reserved(manifest.Packs), choice);
    Console.WriteLine(result.Conflict ? $"{result.Message} Run again with --replace or --keep-both." : result.Message);
    foreach (var warning in result.Warnings)
    {
        Console.WriteLine($"warning: {warning}");
    }

    return result.PackId is null ? 1 : 0;
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
    using var game = new CommunityGame(manifest, save, driver, multisampling);
    game.Run();
    return game.ExitCode;
}

// The only ways a launch reaches the classic import or export: the player asked for it by name.
static int ImportClassic(string folder, string dataDir)
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var result = ClassicImport.FromFolder(folder, dataDir);
    Console.WriteLine(result.Message);
    return result.Imported ? 0 : 1;
}

static int ExportClassic(string dataDir)
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var zip = ClassicImport.Export(dataDir, out var message);
    Console.WriteLine(message);
    return zip is null ? 1 : 0;
}
