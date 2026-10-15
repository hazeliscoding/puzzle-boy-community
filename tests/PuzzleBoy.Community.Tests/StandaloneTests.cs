using System.Reflection;
using System.Text.Json;

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
    public void The_third_party_notices_sit_beside_the_game_and_name_every_package_it_ships()
    {
        var notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.txt"));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE")));
        using var deps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PuzzleBoyCommunity.deps.json")));
        var packages = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(l => l.Value.GetProperty("type").GetString() == "package")
            .Select(l => l.Name.Replace('/', ' '))
            .ToList();
        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.Contains(p, notices));
    }

    [Fact]
    public void The_reference_check_finds_a_reference_that_is_there() =>
        Assert.Equal(["PuzzleBoyCommunity"], ReferencesAmong(typeof(StandaloneTests).Assembly, [.. InstallReaders, "PuzzleBoyCommunity"]));
}
