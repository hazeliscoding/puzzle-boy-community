namespace PuzzleBoy.Community.Tests;

/// <summary>Paths in this checkout, found by walking up from the test binaries to the solution file.</summary>
internal static class Repo
{
    public static readonly string Root = FindRoot();

    public static string Content => Path.Combine(Root, "content");

    public static string KnownLayouts => Path.Combine(Root, "scripts", "known-atlus-layouts.txt");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PuzzleBoy.Community.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests run from inside the repo, below PuzzleBoy.Community.slnx.");
    }
}

/// <summary>A scratch folder, deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("puzzle-boy-community-").FullName;

    /// <summary>A copy of the repo's content/ in a fresh scratch folder.</summary>
    public static TempFolder WithContent()
    {
        var temp = new TempFolder();
        foreach (var file in Directory.EnumerateFiles(Repo.Content, "*", SearchOption.AllDirectories))
        {
            var target = System.IO.Path.Combine(temp.Path, System.IO.Path.GetRelativePath(Repo.Content, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return temp;
    }

    public string Write(string relative, string text)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary>
    /// Makes <paramref name="link"/> a link to the folder <paramref name="target"/>: a symbolic link, or on Windows without
    /// the right to make one, a junction. Skips the test when neither works.
    /// </summary>
    public static void LinkFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip($"This system won't create a symbolic link: {e.Message}");
            }

            using var mklink = System.Diagnostics.Process.Start("cmd.exe", ["/c", "mklink", "/J", link, target]);
            mklink.WaitForExit();
            if (mklink.ExitCode != 0)
            {
                Assert.Skip("This system won't create a symbolic link or a junction.");
            }
        }
    }

    public void Dispose()
    {
        // A recursive delete is refused a junction on Windows; removing the link alone works.
        foreach (var link in new DirectoryInfo(Path).EnumerateDirectories("*", SearchOption.AllDirectories).Where(d => d.LinkTarget is not null).ToList())
        {
            link.Delete();
        }

        Directory.Delete(Path, recursive: true);
    }
}
