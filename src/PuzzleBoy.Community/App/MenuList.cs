namespace PuzzleBoy.Community.App;

/// <summary>A vertical menu's focus: one of <see cref="Count"/> items, wrapping at both ends.</summary>
/// <param name="count">How many items the menu has, at least 1.</param>
internal sealed class MenuList(int count)
{
    /// <summary>How many items the menu has.</summary>
    public int Count { get; } = count > 0 ? count : throw new ArgumentOutOfRangeException(nameof(count), count, "A menu needs an item.");

    /// <summary>The focused item, 0 to <see cref="Count"/> - 1.</summary>
    public int Focus { get; private set; }

    /// <summary>Moves the focus <paramref name="by"/> items, wrapping around.</summary>
    public void Move(int by) => Focus = (((Focus + by) % Count) + Count) % Count;

    /// <summary>Focuses item <paramref name="index"/>, as the mouse does. False, changing nothing, when it's out of range or already focused.</summary>
    public bool Set(int index)
    {
        if (index < 0 || index >= Count || index == Focus)
        {
            return false;
        }

        Focus = index;
        return true;
    }

    /// <summary>The item under virtual-screen point <paramref name="point"/> for items stacked from <paramref name="top"/>, or -1.</summary>
    /// <param name="point">The pointer, in virtual-screen pixels.</param>
    /// <param name="left">The items' left edge.</param>
    /// <param name="top">The first item's top edge.</param>
    /// <param name="width">Each item's width.</param>
    /// <param name="spacing">The distance from one item's top to the next; each item is that tall.</param>
    public int HitTest(Microsoft.Xna.Framework.Vector2 point, float left, float top, float width, float spacing)
    {
        if (point.X < left || point.X >= left + width || point.Y < top)
        {
            return -1;
        }

        var index = (int)((point.Y - top) / spacing);
        return index < Count ? index : -1;
    }
}
