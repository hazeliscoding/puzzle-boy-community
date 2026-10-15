using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Content;

/// <summary>
/// The fingerprint list in <c>scripts/known-atlus-layouts.txt</c>: one <c>full &lt;sha256&gt;</c> or
/// <c>terrain &lt;sha256&gt;</c> line per known layout, from <see cref="LayoutFingerprint"/>.
/// </summary>
internal static class KnownLayouts
{
    /// <summary>Reads the list's lines, ignoring blank ones.</summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    public static HashSet<string> Read(string path) =>
        File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();

    /// <summary>Which of <paramref name="stage"/>'s fingerprints are in <paramref name="known"/>: <c>full</c>, <c>terrain</c>, both or neither.</summary>
    public static IEnumerable<string> Matches(IReadOnlySet<string> known, Stage stage)
    {
        if (known.Contains($"full {LayoutFingerprint.Full(stage)}"))
        {
            yield return "full";
        }

        if (known.Contains($"terrain {LayoutFingerprint.Terrain(stage)}"))
        {
            yield return "terrain";
        }
    }
}
