using Microsoft.Xna.Framework;
using PuzzleBoy.Community.App;
using PuzzleBoy.Rules;

namespace PuzzleBoy.Community.Play;

/// <summary>
/// The play camera: quarter turns and an overhead view, eased over a few ticks. Distances are in cells, and grid +y runs
/// toward -z, away from the starting view, so the stage looks as the engine docs describe (+y up the screen).
/// </summary>
internal sealed class CameraRig
{
    /// <summary>The play view's vertical field of view, in degrees.</summary>
    public const float FieldOfView = 34;

    /// <summary>
    /// Where the board must stay on the 1280×720 virtual screen: below the HUD's stat panels (which end at y = 114) and
    /// above its hint bar (which starts at y = 672).
    /// </summary>
    public static readonly Rectangle SafeArea = new(16, 120, 1248, 548);

    private const int TurnTicks = 7;
    private const int AngleTicks = 7;

    // The nearest the camera comes, so a small stage isn't blown up; the farthest, inside the far plane.
    private const float MinDistance = 9;
    private const float MaxDistance = 50;

    // How high a stage's contents reach: blocks, swivel centers, the goal's gem and a hopping Pixel Jack.
    private const float ContentTop = 1.1f;

    private float _fromYaw;
    private float _toYaw;
    private int _turnAge = TurnTicks;
    private bool _overhead;
    private int _angleAge = AngleTicks;

    /// <summary>True makes turns and changes of angle land at once, for players who reduce motion.</summary>
    public bool Instant { get; set; }

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
        _angleAge = Instant ? AngleTicks : 0;
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

    /// <summary>
    /// The pivot and distance, in cells, that show the whole play area from (<paramref name="minX"/>,
    /// <paramref name="minY"/>) to (<paramref name="maxX"/>, <paramref name="maxY"/>) inside <see cref="SafeArea"/> with
    /// the view as it is now, in a viewport of <paramref name="width"/>×<paramref name="height"/> pixels.
    /// </summary>
    public (Vector3 Pivot, float Distance) Frame(int minX, int minY, int maxX, int maxY, int width, int height)
    {
        var pivot = new Vector3((minX + maxX) / 2f, 0, -(minY + maxY) / 2f);
        Vector3[] corners =
        [
            .. from x in new[] { minX - 0.5f, maxX + 0.5f }
               from y in new[] { minY - 0.5f, maxY + 0.5f }
               from top in new[] { 0, ContentTop }
               select new Vector3(x, top, -y),
        ];
        bool Fits(float distance) => corners.All(c => ToScreen(c, pivot, distance, width, height) is { } p && SafeArea.Contains(p));

        // The projected size shrinks as the camera backs off, so a binary search finds the nearest distance that fits.
        var (near, far) = (MinDistance, MaxDistance);
        if (Fits(near))
        {
            return (pivot, near);
        }

        for (var i = 0; i < 20; i++)
        {
            var middle = (near + far) / 2;
            (near, far) = Fits(middle) ? (near, middle) : (middle, far);
        }

        return (pivot, far);
    }

    /// <summary>
    /// Where <paramref name="world"/> appears on the 1280×720 virtual screen with the view as it is now, looking at
    /// <paramref name="pivot"/> from <paramref name="distance"/> cells in a <paramref name="width"/>×<paramref name="height"/>
    /// viewport; null when it's behind the camera.
    /// </summary>
    public Vector2? ToScreen(Vector3 world, Vector3 pivot, float distance, int width, int height)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), View(pivot, distance) * Projection(width / (float)height));
        if (clip.W <= 0)
        {
            return null;
        }

        var window = new Vector2(((clip.X / clip.W) + 1) / 2 * width, (1 - (clip.Y / clip.W)) / 2 * height);
        var (scale, offset) = VirtualScreen.Fit(width, height);
        return (window - offset) / scale;
    }

    /// <summary>The play view's projection for a viewport of <paramref name="aspect"/> width over height.</summary>
    public static Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FieldOfView), aspect, 0.05f, 60f);

    private bool Turn(int sense)
    {
        if (IsBusy)
        {
            return false;
        }

        Mode = (Mode + sense + 4) % 4;
        _fromYaw = _toYaw;
        _toYaw += sense * 90;
        _turnAge = Instant ? TurnTicks : 0;
        return true;
    }

    // Ease-out cubic: quick to start, gentle to land.
    private static float Ease(float t) => 1 - MathF.Pow(1 - Math.Clamp(t, 0, 1), 3);
}
