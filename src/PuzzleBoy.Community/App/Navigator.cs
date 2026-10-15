namespace PuzzleBoy.Community.App;

/// <summary>The game's screens.</summary>
internal enum Screen
{
    /// <summary>The title menu, the root every Back leads to.</summary>
    Title,

    /// <summary>The list of bundled stages.</summary>
    StageSelect,

    /// <summary>A stage in play.</summary>
    Playing,

    /// <summary>The pause menu over a stage in play.</summary>
    Paused,

    /// <summary>The settings.</summary>
    Settings,

    /// <summary>The credits.</summary>
    Credits,

    /// <summary>The level editor.</summary>
    Editor,

    /// <summary>The optional import from the player's own Nocturne install.</summary>
    ImportClassic,
}

/// <summary>
/// The screen stack: opening a screen goes forward and Back returns to the one before. The title is always at the
/// bottom, so Back never leaves the game.
/// </summary>
internal sealed class Navigator
{
    private readonly List<Screen> _stack = [Screen.Title];

    /// <summary>The screen showing.</summary>
    public Screen Current => _stack[^1];

    /// <summary>The screen under the current one, or null on the title.</summary>
    public Screen? Under => _stack.Count > 1 ? _stack[^2] : null;

    /// <summary>The screens from the title to the current one.</summary>
    public IReadOnlyList<Screen> Stack => _stack;

    /// <summary>Opens <paramref name="screen"/> over the current one. Opening the title goes back to it instead.</summary>
    public void Open(Screen screen)
    {
        if (screen == Screen.Title)
        {
            ReturnTo(Screen.Title);
            return;
        }

        _stack.Add(screen);
    }

    /// <summary>Returns to the previous screen. False, changing nothing, on the title.</summary>
    public bool Back()
    {
        if (_stack.Count == 1)
        {
            return false;
        }

        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>
    /// Goes back to the nearest <paramref name="screen"/> on the stack, or, when it isn't there, to the title with
    /// <paramref name="screen"/> opened over it.
    /// </summary>
    public void ReturnTo(Screen screen)
    {
        var index = _stack.LastIndexOf(screen);
        if (index < 0)
        {
            _stack.RemoveRange(1, _stack.Count - 1);
            _stack.Add(screen);
            return;
        }

        _stack.RemoveRange(index + 1, _stack.Count - index - 1);
    }
}
