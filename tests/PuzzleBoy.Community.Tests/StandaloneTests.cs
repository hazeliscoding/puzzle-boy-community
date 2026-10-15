using System.Reflection;

namespace PuzzleBoy.Community.Tests;

public class StandaloneTests
{
    // The preservation port's readers for a Nocturne install. Community Edition must start without one.
    private static readonly string[] InstallReaders = ["AssetsTools.NET", "Fmod5Sharp"];

    private static string[] ReferencesAmong(Assembly assembly, IEnumerable<string> names) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Intersect(names).ToArray();

    [Fact]
    public void The_game_references_no_reader_for_a_Nocturne_install() =>
        Assert.Empty(ReferencesAmong(typeof(CommunityGame).Assembly, InstallReaders));

    [Fact]
    public void The_reference_check_finds_a_reference_that_is_there() =>
        Assert.Equal(["PuzzleBoyCommunity"], ReferencesAmong(typeof(StandaloneTests).Assembly, [.. InstallReaders, "PuzzleBoyCommunity"]));
}
