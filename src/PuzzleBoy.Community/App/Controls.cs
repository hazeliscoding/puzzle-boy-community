using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace PuzzleBoy.Community.App;

/// <summary>What the player asks for. A key can mean several; each screen reads only the ones it uses.</summary>
internal enum Command
{
    /// <summary>Screen up: a step away from the camera, or the menu item above.</summary>
    Up,

    /// <summary>Screen right.</summary>
    Right,

    /// <summary>Screen down: a step toward the camera, or the menu item below.</summary>
    Down,

    /// <summary>Screen left.</summary>
    Left,

    /// <summary>Chooses the focused menu item.</summary>
    Confirm,

    /// <summary>Returns to the previous screen or closes an overlay.</summary>
    Back,

    /// <summary>Turns the view a quarter turn one way.</summary>
    TurnLeft,

    /// <summary>Turns the view a quarter turn the other way.</summary>
    TurnRight,

    /// <summary>Switches between the normal and the overhead view.</summary>
    Overhead,

    /// <summary>Hands control to the next character, on stages with more than one.</summary>
    Switch,

    /// <summary>Takes back the last push or turn.</summary>
    Undo,

    /// <summary>Starts the stage again.</summary>
    Restart,

    /// <summary>Opens the pause menu.</summary>
    Pause,
}

/// <summary>
/// One tick of input: the keyboard and the first gamepad now and a tick ago, read as <see cref="Command"/>s. Plain data,
/// so tests build it from key lists.
/// </summary>
/// <param name="Keys">The keyboard now.</param>
/// <param name="PreviousKeys">The keyboard a tick ago.</param>
/// <param name="Pad">The first gamepad now; a disconnected pad reads as nothing held.</param>
/// <param name="PreviousPad">The first gamepad a tick ago.</param>
internal readonly record struct Controls(KeyboardState Keys, KeyboardState PreviousKeys, GamePadState Pad, GamePadState PreviousPad)
{
    private const float StickThreshold = 0.5f;

    /// <summary>The keys for each command. Esc and Backspace mean Back in menus, and Pause and Undo in play.</summary>
    public static readonly IReadOnlyDictionary<Command, Keys[]> KeyMap = new Dictionary<Command, Keys[]>
    {
        [Command.Up] = [Microsoft.Xna.Framework.Input.Keys.Up, Microsoft.Xna.Framework.Input.Keys.W],
        [Command.Right] = [Microsoft.Xna.Framework.Input.Keys.Right, Microsoft.Xna.Framework.Input.Keys.D],
        [Command.Down] = [Microsoft.Xna.Framework.Input.Keys.Down, Microsoft.Xna.Framework.Input.Keys.S],
        [Command.Left] = [Microsoft.Xna.Framework.Input.Keys.Left, Microsoft.Xna.Framework.Input.Keys.A],
        [Command.Confirm] = [Microsoft.Xna.Framework.Input.Keys.Enter, Microsoft.Xna.Framework.Input.Keys.Space],
        [Command.Back] = [Microsoft.Xna.Framework.Input.Keys.Escape, Microsoft.Xna.Framework.Input.Keys.Back],
        [Command.TurnLeft] = [Microsoft.Xna.Framework.Input.Keys.Q],
        [Command.TurnRight] = [Microsoft.Xna.Framework.Input.Keys.E],
        [Command.Overhead] = [Microsoft.Xna.Framework.Input.Keys.V],
        [Command.Switch] = [Microsoft.Xna.Framework.Input.Keys.C],
        [Command.Undo] = [Microsoft.Xna.Framework.Input.Keys.Z, Microsoft.Xna.Framework.Input.Keys.Back],
        [Command.Restart] = [Microsoft.Xna.Framework.Input.Keys.R],
        [Command.Pause] = [Microsoft.Xna.Framework.Input.Keys.Escape, Microsoft.Xna.Framework.Input.Keys.P],
    };

    /// <summary>The gamepad buttons for each command; the left stick moves too. B means Back in menus and Undo in play.</summary>
    public static readonly IReadOnlyDictionary<Command, Buttons[]> PadMap = new Dictionary<Command, Buttons[]>
    {
        [Command.Up] = [Buttons.DPadUp],
        [Command.Right] = [Buttons.DPadRight],
        [Command.Down] = [Buttons.DPadDown],
        [Command.Left] = [Buttons.DPadLeft],
        [Command.Confirm] = [Buttons.A],
        [Command.Back] = [Buttons.B],
        [Command.TurnLeft] = [Buttons.LeftShoulder],
        [Command.TurnRight] = [Buttons.RightShoulder],
        [Command.Overhead] = [Buttons.Y],
        [Command.Switch] = [Buttons.X],
        [Command.Undo] = [Buttons.B],
        [Command.Restart] = [Buttons.Back],
        [Command.Pause] = [Buttons.Start],
    };

    // The four directions as screen directions: 0 up, 1 right, 2 down, 3 left.
    private static readonly Command[] Directions = [Command.Up, Command.Right, Command.Down, Command.Left];

    /// <summary>Keyboard-only input: <paramref name="now"/> held this tick and <paramref name="before"/> the tick before.</summary>
    public static Controls FromKeys(Keys[] now, Keys[]? before = null) =>
        new(new KeyboardState(now), new KeyboardState(before ?? []), default, default);

    /// <summary>True while the command's key, button or stick direction is held.</summary>
    public bool Held(Command command) => HeldIn(command, Keys, Pad);

    /// <summary>True on the tick the command goes from not held to held.</summary>
    public bool Pressed(Command command) => HeldIn(command, Keys, Pad) && !HeldIn(command, PreviousKeys, PreviousPad);

    /// <summary>The screen direction held, 0 up, 1 right, 2 down, 3 left, or null; the first in that order wins.</summary>
    public int? HeldDirection()
    {
        for (var screen = 0; screen < 4; screen++)
        {
            if (Held(Directions[screen]))
            {
                return screen;
            }
        }

        return null;
    }

    private static bool HeldIn(Command command, KeyboardState keys, GamePadState pad)
    {
        if (KeyMap[command].Any(keys.IsKeyDown))
        {
            return true;
        }

        if (!pad.IsConnected)
        {
            return false;
        }

        // The stick's y is positive up.
        var stick = pad.ThumbSticks.Left;
        return PadMap[command].Any(pad.IsButtonDown) || command switch
        {
            Command.Up => stick.Y > StickThreshold,
            Command.Down => stick.Y < -StickThreshold,
            Command.Right => stick.X > StickThreshold,
            Command.Left => stick.X < -StickThreshold,
            _ => false,
        };
    }
}

/// <summary>A held direction's repeat in menus: it fires on the first tick, then every third tick once held more than 9.</summary>
internal sealed class KeyRepeat
{
    private int _held;

    /// <summary>Counts one tick of the input being <paramref name="down"/>, and says whether it fires.</summary>
    public bool Fires(bool down)
    {
        _held = down ? _held + 1 : 0;
        return _held == 1 || (_held > 9 && _held % 3 == 0);
    }
}

/// <summary>Fits the 1280×720 virtual screen the UI is laid out on into a window of any size.</summary>
internal static class VirtualScreen
{
    /// <summary>The virtual width in pixels.</summary>
    public const int Width = 1280;

    /// <summary>The virtual height in pixels.</summary>
    public const int Height = 720;

    /// <summary>The uniform scale and offset that fit the virtual screen, centered, in a <paramref name="width"/>×<paramref name="height"/> window.</summary>
    public static (float Scale, Vector2 Offset) Fit(int width, int height)
    {
        var scale = Math.Min(width / (float)Width, height / (float)Height);
        return (scale, new Vector2((width - (Width * scale)) / 2, (height - (Height * scale)) / 2));
    }

    /// <summary>A window pixel as a virtual-screen point.</summary>
    public static Vector2 ToVirtual(Point window, int width, int height)
    {
        var (scale, offset) = Fit(width, height);
        return (new Vector2(window.X, window.Y) - offset) / scale;
    }
}
