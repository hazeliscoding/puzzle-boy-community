using Microsoft.Xna.Framework;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Play;

/// <summary>
/// The play camera: quarter turns and an overhead view, eased over a few ticks. Distances are in cells, and grid +y runs
/// toward -z, away from the starting view, so the stage looks as the engine docs describe (+y up the screen).
/// </summary>
internal sealed class CameraRig
{
    private const int TurnTicks = 7;
    private const int AngleTicks = 7;

    private float _fromYaw;
    private float _toYaw;
    private int _turnAge = TurnTicks;
    private bool _overhead;
    private int _angleAge = AngleTicks;

    /// <summary>Right turns minus left turns, mod 4. It turns the controls with the view.</summary>
    public int Mode { get; private set; }

    /// <summary>True while a turn or a change of angle is running; new ones wait for it.</summary>
    public bool IsBusy => _turnAge < TurnTicks || _angleAge < AngleTicks;

    /// <summary>True when the overhead view is on or turning on.</summary>
    public bool Overhead => _overhead;

    /// <summary>The view's yaw in degrees now: 0 looks toward grid +y, and each right turn adds 90.</summary>
    public float Yaw => MathHelper.Lerp(_fromYaw, _toYaw, Ease(_turnAge / (float)TurnTicks));

    /// <summary>How far the view is toward overhead now, 0 to 1.</summary>
    public float OverheadAmount
    {
        get
        {
            var toward = Ease(_angleAge / (float)AngleTicks);
            return _overhead ? toward : 1 - toward;
        }
    }

    /// <summary>
    /// The grid direction for a screen direction (0 up, 1 right, 2 down, 3 left). Up is always away from the camera,
    /// as in the original: direction = (screen - mode) mod 4.
    /// </summary>
    public Direction DirectionFor(int screen) => (Direction)((((screen - Mode) % 4) + 4) % 4);

    /// <summary>Turns the view a quarter turn clockwise seen from above. False while busy.</summary>
    public bool TurnLeft() => Turn(-1);

    /// <summary>The opposite of <see cref="TurnLeft"/>.</summary>
    public bool TurnRight() => Turn(1);

    /// <summary>Switches between the normal and the overhead view. False while busy.</summary>
    public bool ToggleOverhead()
    {
        if (IsBusy)
        {
            return false;
        }

        _overhead = !_overhead;
        _angleAge = 0;
        return true;
    }

    /// <summary>Advances any turn or change of angle by one tick.</summary>
    public void Tick()
    {
        _turnAge = Math.Min(_turnAge + 1, TurnTicks);
        _angleAge = Math.Min(_angleAge + 1, AngleTicks);
    }

    /// <summary>The view matrix looking at <paramref name="pivot"/> from <paramref name="distance"/> cells away.</summary>
    public Matrix View(Vector3 pivot, float distance)
    {
        var overhead = OverheadAmount;
        var pitch = MathHelper.ToRadians(MathHelper.Lerp(58, 89.5f, overhead));
        var yaw = MathHelper.ToRadians(Yaw);
        var away = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        var position = pivot + (away * MathF.Cos(pitch) * distance) + (Vector3.Up * MathF.Sin(pitch) * distance);
        var up = (-away * MathF.Sin(pitch)) + (Vector3.Up * MathF.Cos(pitch));
        return Matrix.CreateLookAt(position, pivot, up);
    }

    private bool Turn(int sense)
    {
        if (IsBusy)
        {
            return false;
        }

        Mode = (Mode + sense + 4) % 4;
        _fromYaw = _toYaw;
        _toYaw += sense * 90;
        _turnAge = 0;
        return true;
    }

    // Ease-out cubic: quick to start, gentle to land.
    private static float Ease(float t) => 1 - MathF.Pow(1 - Math.Clamp(t, 0, 1), 3);
}
