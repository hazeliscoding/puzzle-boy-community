using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Content;

/// <summary>A stage from a pack.</summary>
/// <param name="Id">The pack-scoped id, <c>pack-id/stage-id</c>.</param>
/// <param name="Title">The name the pack gives it.</param>
/// <param name="Stage">The stage, validated by the engine.</param>
/// <param name="Hint">A nudge the help screen shows, or null.</param>
internal sealed record PackStage(string Id, string Title, Stage Stage, string? Hint = null);

/// <summary>
/// A folder of stages described by its <c>pack.json</c>: format <c>puzzle-boy-pack</c>, schema version 1. Each stage is
/// an engine stage JSON file inside the folder.
/// </summary>
/// <param name="PackId">The pack's id, <c>[a-z0-9-]{1,48}</c>.</param>
/// <param name="Title">The pack's name.</param>
/// <param name="Creator">Who made it.</param>
/// <param name="License">Its license, an SPDX identifier.</param>
/// <param name="Stages">Its stages in play order. Only those that loaded; a stage with a problem is left out.</param>
internal sealed partial record StagePack(string PackId, string Title, string Creator, string License, IReadOnlyList<PackStage> Stages)
{
    /// <summary>The largest <c>pack.json</c> accepted.</summary>
    public const int MaxPackBytes = 64 * 1024;

    /// <summary>The most stages one pack may list.</summary>
    public const int MaxStages = 200;

    /// <summary>The longest hint a stage may have, in characters.</summary>
    public const int MaxHint = 200;

    // The engine refuses a larger stage file too; checking first avoids reading a huge one.
    private const int MaxStageBytes = 64 * 1024;

    /// <summary>
    /// Reads the pack in <paramref name="folder"/>. Returns null when <c>pack.json</c> itself can't be used; otherwise the
    /// pack with every stage that loaded. Every problem is added to <paramref name="problems"/>; nothing throws.
    /// </summary>
    /// <param name="folder">The pack's folder.</param>
    /// <param name="label">How problems name the pack, such as <c>stages/first-steps</c>.</param>
    /// <param name="problems">Receives a sentence for each thing wrong.</param>
    public static StagePack? Load(string folder, string label, List<string> problems)
    {
        var where = $"{label}/pack.json";
        if (ContentFile.Resolve(folder, "pack.json", where, problems) is not { } file
            || ContentFile.ReadJson(file, MaxPackBytes, where, problems) is not { } root
            || !ContentFile.CheckHeader(root, "puzzle-boy-pack", where, problems))
        {
            return null;
        }

        var packId = ContentFile.String(root, "packId");
        var title = ContentFile.String(root, "title");
        var creator = ContentFile.String(root, "creator");
        var license = ContentFile.String(root, "license");
        var before = problems.Count;
        if (packId is null || !IdPattern().IsMatch(packId))
        {
            problems.Add($"{where}: \"packId\" should be 1 to 48 lowercase letters, digits or dashes.");
        }

        Require(title, "title");
        Require(creator, "creator");
        Require(license, "license");
        if (!root.TryGetProperty("stages", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxStages)
        {
            problems.Add($"{where}: \"stages\" should list 1 to {MaxStages} stages.");
        }

        if (problems.Count > before)
        {
            return null;
        }

        var stages = new List<PackStage>();
        var ids = new HashSet<string>();
        var number = 0;
        foreach (var entry in list.EnumerateArray())
        {
            number++;
            var id = ContentFile.String(entry, "id");
            var stageTitle = ContentFile.String(entry, "title");
            var stageWhere = $"{where}, stage {number}";
            if (id is null || !IdPattern().IsMatch(id))
            {
                problems.Add($"{stageWhere}: \"id\" should be 1 to 48 lowercase letters, digits or dashes.");
                continue;
            }

            stageWhere = $"{label}, stage \"{id}\"";
            if (!ids.Add(id))
            {
                problems.Add($"{stageWhere}: another stage in this pack has the same id.");
                continue;
            }

            if (stageTitle is null)
            {
                problems.Add($"{stageWhere}: \"title\" is missing.");
                continue;
            }

            var hint = ContentFile.String(entry, "hint");
            if (hint is { Length: > MaxHint })
            {
                problems.Add($"{stageWhere}: \"hint\" is longer than {MaxHint} characters.");
                continue;
            }

            if (ContentFile.Resolve(folder, ContentFile.String(entry, "file"), stageWhere, problems) is not { } stageFile
                || ContentFile.Read(stageFile, MaxStageBytes, stageWhere, problems) is not { } bytes)
            {
                continue;
            }

            try
            {
                var document = StageDocument.Parse(Encoding.UTF8.GetString(bytes));
                stages.Add(new PackStage($"{packId}/{id}", stageTitle, document.Stage, hint));
            }
            catch (StageFormatException e)
            {
                problems.AddRange(e.Problems.Select(p => $"{stageWhere}: {p}"));
            }
        }

        return new StagePack(packId!, title!, creator!, license!, stages);

        void Require(string? value, string name)
        {
            if (value is null)
            {
                problems.Add($"{where}: \"{name}\" is missing.");
            }
        }
    }

    // \z, not $, which would also accept a trailing newline.
    [GeneratedRegex(@"^[a-z0-9-]{1,48}\z")]
    private static partial Regex IdPattern();
}
