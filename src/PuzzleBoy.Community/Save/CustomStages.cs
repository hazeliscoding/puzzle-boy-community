using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Save;

/// <summary>A stage the player made, as found in the custom folder.</summary>
/// <param name="Name">The file name without <c>.json</c>; always a <see cref="CustomStages.IsSafeName"/> name.</param>
/// <param name="Title">The stage's title, or its name when it has none or can't be read.</param>
/// <param name="Stage">The stage, or null when the file can't be read as one.</param>
/// <param name="Problem">Why the file can't be read, or null.</param>
internal sealed record CustomStage(string Name, string Title, Stage? Stage, string? Problem)
{
    /// <summary>The stage's id in progress and stage select: <c>custom/name</c>.</summary>
    public string Id => $"{CustomStages.PackId}/{Name}";
}

/// <summary>
/// The player's own stages: engine stage JSON files in <c>&lt;data dir&gt;/custom</c>, named from their titles. A name
/// is only ever lowercase ASCII letters and digits joined by single dashes, never a reserved Windows device name, so
/// nothing typed can reach outside the folder. Nothing here throws for a bad file or a folder that can't be written:
/// reads report a problem per file, and writes return an error message.
/// </summary>
internal static partial class CustomStages
{
    /// <summary>The pack id the player's stages go under in stage select and progress.</summary>
    public const string PackId = "custom";

    /// <summary>The longest file name, without <c>.json</c>.</summary>
    public const int MaxName = 48;

    // The engine refuses a larger stage file too; checking first avoids reading a huge one.
    private const int MaxBytes = 64 * 1024;

    /// <summary>The custom folder inside the data folder <paramref name="dataDir"/>.</summary>
    public static string Folder(string dataDir) => Path.Combine(dataDir, PackId);

    /// <summary>
    /// A file name for <paramref name="title"/>: letters lose their accents and become lowercase, digits stay, and every
    /// other run of characters becomes one dash, with none at either end. It's cut to <see cref="MaxName"/>, a name with
    /// nothing left is <c>stage</c>, and a Windows device name such as <c>con</c> or <c>com1</c> gets <c>-stage</c>.
    /// </summary>
    public static string SafeName(string title)
    {
        var name = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                name.Append(lower);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && name.Length > 0 && name[^1] != '-')
            {
                name.Append('-');
            }
        }

        var safe = name.ToString(0, Math.Min(name.Length, MaxName)).TrimEnd('-');
        if (safe.Length == 0)
        {
            return "stage";
        }

        return IsReserved(safe) ? $"{safe}-stage" : safe;
    }

    /// <summary>True when <paramref name="name"/> is a name <see cref="SafeName"/> could give, and so safe as a file name anywhere.</summary>
    public static bool IsSafeName(string name) => NamePattern().IsMatch(name) && !IsReserved(name);

    /// <summary>
    /// The path of stage <paramref name="name"/> in the custom folder of <paramref name="dataDir"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't <see cref="IsSafeName"/>.</exception>
    public static string PathFor(string dataDir, string name)
    {
        if (!IsSafeName(name))
        {
            throw new ArgumentException($"\"{name}\" isn't a safe stage name.", nameof(name));
        }

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder(dataDir)));
        var path = Path.GetFullPath(Path.Combine(folder, $"{name}.json"));

        // A safe name can't leave the folder; this says so where it matters.
        return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"\"{name}\" leads outside the custom folder.", nameof(name));
    }

    /// <summary>
    /// A name for <paramref name="title"/> no stage in the custom folder has yet: <see cref="SafeName"/>, then with
    /// <c>-2</c>, <c>-3</c> and so on.
    /// </summary>
    public static string UniqueName(string dataDir, string title)
    {
        var name = SafeName(title);
        for (var n = 2; File.Exists(PathFor(dataDir, name)); n++)
        {
            var suffix = $"-{n}";
            var stem = SafeName(title);
            name = $"{stem[..Math.Min(stem.Length, MaxName - suffix.Length)].TrimEnd('-')}{suffix}";
        }

        return name;
    }

    /// <summary>
    /// Every stage in the custom folder of <paramref name="dataDir"/>, by name. Files without a safe name, links and
    /// anything that isn't a top-level <c>.json</c> file are left out; a file that isn't a valid stage is listed with
    /// its <see cref="CustomStage.Problem"/>. Empty when the folder doesn't exist or can't be read.
    /// </summary>
    public static List<CustomStage> List(string dataDir)
    {
        var stages = new List<CustomStage>();
        try
        {
            var folder = Folder(dataDir);
            if (!Directory.Exists(folder))
            {
                return stages;
            }

            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Directory };
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", options))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsSafeName(name) && Path.GetExtension(file) == ".json")
                {
                    stages.Add(Read(dataDir, name));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder lists as empty; saving there will say what's wrong.
        }

        stages.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return stages;
    }

    /// <summary>Reads stage <paramref name="name"/>; a file that can't be read comes back with its <see cref="CustomStage.Problem"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't <see cref="IsSafeName"/>.</exception>
    public static CustomStage Read(string dataDir, string name)
    {
        var problems = new List<string>();
        if (ContentFile.Read(PathFor(dataDir, name), MaxBytes, $"{name}.json", problems) is not { } bytes)
        {
            return new CustomStage(name, name, null, problems.FirstOrDefault());
        }

        try
        {
            var document = StageDocument.Parse(Encoding.UTF8.GetString(bytes));
            return new CustomStage(name, string.IsNullOrWhiteSpace(document.Title) ? name : document.Title, document.Stage, null);
        }
        catch (StageFormatException e)
        {
            return new CustomStage(name, name, null, e.Problems.Count > 0 ? e.Problems[0].ToString() : e.Message);
        }
    }

    /// <summary>
    /// Writes <paramref name="document"/> as stage <paramref name="name"/>, replacing any file of that name: to a
    /// temporary file first, then moved over, so a failed write never leaves half a stage. Returns null, or why it
    /// failed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't <see cref="IsSafeName"/>.</exception>
    public static string? Write(string dataDir, string name, StageDocument document)
    {
        var path = PathFor(dataDir, name);
        try
        {
            var json = document.ToJson();
            Directory.CreateDirectory(Folder(dataDir));
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return $"The stage couldn't be saved: {e.Message}";
        }
    }

    /// <summary>
    /// Copies stage <paramref name="name"/> to a new name with " copy" added to its title. Returns the new name, or null
    /// with <paramref name="error"/> saying why.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't <see cref="IsSafeName"/>.</exception>
    public static string? Duplicate(string dataDir, string name, out string? error)
    {
        var original = Read(dataDir, name);
        if (original.Stage is null)
        {
            error = $"It can't be copied: {original.Problem}";
            return null;
        }

        var title = $"{original.Title} copy";
        var copy = UniqueName(dataDir, title);
        error = Write(dataDir, copy, new StageDocument(original.Stage, title));
        return error is null ? copy : null;
    }

    /// <summary>Deletes stage <paramref name="name"/>. Returns null, or why it failed.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't <see cref="IsSafeName"/>.</exception>
    public static string? Delete(string dataDir, string name)
    {
        try
        {
            File.Delete(PathFor(dataDir, name));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"The stage couldn't be deleted: {e.Message}";
        }
    }

    /// <summary>The readable stages of <paramref name="stages"/> as the "My Stages" pack.</summary>
    public static StagePack AsPack(IEnumerable<CustomStage> stages) =>
        new(PackId, "My Stages", "You", "NOASSERTION", [.. stages.Where(s => s.Stage is not null).Select(s => new PackStage(s.Id, s.Title, s.Stage!))]);

    // Windows opens a device, not a file, for these names with any extension.
    private static bool IsReserved(string name) =>
        name is "con" or "prn" or "aux" or "nul"
        || (name.Length == 4 && (name.StartsWith("com", StringComparison.Ordinal) || name.StartsWith("lpt", StringComparison.Ordinal)) && char.IsAsciiDigit(name[3]));

    // \z, not $, which would also accept a trailing newline.
    [GeneratedRegex(@"^(?=.{1,48}\z)[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex NamePattern();
}
