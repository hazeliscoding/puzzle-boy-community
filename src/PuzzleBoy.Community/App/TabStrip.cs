namespace PuzzleBoy.Community.App;

/// <summary>
/// Which of a row of tabs fit in a strip: the strip shows whole tabs from <c>first</c> on, sliding so the selected tab
/// always shows, so no tab is ever cut at an edge.
/// </summary>
internal static class TabStrip
{
    /// <summary>
    /// The first tab to show so <paramref name="selected"/> fits, moving from <paramref name="first"/> (the last frame's)
    /// as little as it can, and showing earlier tabs again when the end of the row leaves room for them.
    /// </summary>
    /// <param name="widths">Each tab's width, in pixels.</param>
    /// <param name="gap">The space between two tabs, in pixels.</param>
    /// <param name="width">The strip's width, in pixels.</param>
    /// <param name="selected">The selected tab, 0 to the tab count - 1.</param>
    /// <param name="first">The first tab shown last time; any value is clamped.</param>
    public static int First(IReadOnlyList<float> widths, float gap, float width, int selected, int first)
    {
        first = Math.Clamp(first, 0, selected);
        while (first < selected && Span(widths, gap, first, selected) > width)
        {
            first++;
        }

        while (first > 0 && Span(widths, gap, first - 1, widths.Count - 1) <= width)
        {
            first--;
        }

        return first;
    }

    /// <summary>How many tabs from <paramref name="first"/> on fit in <paramref name="width"/> pixels; at least one.</summary>
    public static int Count(IReadOnlyList<float> widths, float gap, float width, int first)
    {
        var last = first;
        while (last + 1 < widths.Count && Span(widths, gap, first, last + 1) <= width)
        {
            last++;
        }

        return last - first + 1;
    }

    /// <summary>
    /// What tells apart packs that share a title, such as copies imported with Keep Both: "" for a title's first pack,
    /// then " (2)", " (3)" and so on in list order. Titles match ignoring case, as the tabs show them in capitals.
    /// </summary>
    public static string[] Suffixes(IReadOnlyList<string> titles)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return [.. titles.Select(t => (seen[t] = seen.GetValueOrDefault(t) + 1) is var n and > 1 ? $" ({n})" : "")];
    }

    // The width tabs from..to take side by side.
    private static float Span(IReadOnlyList<float> widths, float gap, int from, int to)
    {
        var span = gap * (to - from);
        for (var i = from; i <= to; i++)
        {
            span += widths[i];
        }

        return span;
    }
}
