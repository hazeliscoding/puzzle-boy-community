using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Play;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community;

/// <summary>
/// The Community Edition window: the screens, their input and their drawing. Everything it shows comes from this
/// repo's <c>content/</c> or is drawn and synthesized in code; it never looks for a game install. One Update is one
/// tick, 30 a second like the original.
/// </summary>
internal sealed partial class CommunityGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly ContentManifest _manifest;
    private readonly Navigator _nav = new();
    private readonly MenuList _titleMenu = new(TitleItems.Length);
    private readonly MenuList _pauseMenu = new(PauseItems.Length);
    private readonly MenuList _settingsMenu = new(SettingsMenu.Labels.Length);
    private readonly MenuList _confirmMenu = new(2);
    private readonly MenuList _backMenu = new(1);
    private readonly MenuList _selectMenu;
    private readonly List<(StagePack Pack, int Index)> _selectable;
    private readonly KeyRepeat _up = new();
    private readonly KeyRepeat _down = new();
    private readonly KeyRepeat _left = new();
    private readonly KeyRepeat _right = new();
    private readonly CameraRig _camera = new();
    private readonly SmokeGui? _smoke;
    private readonly SaveStore _save;
    private Theme _theme = null!;
    private Ui _ui = null!;
    private BoardRenderer _board = null!;
    private Sounds _sounds = null!;
    private PlaySession? _session;
    private KeyboardState _previousKeys;
    private GamePadState _previousPad;
    private MouseState _previousMouse;
    private Vector2 _pointer;
    private bool _pointerMoved;
    private bool _clicked;
    private bool _rightClicked;
    private bool _menuUp;
    private bool _menuDown;
    private bool _menuLeft;
    private bool _menuRight;
    private int _wheel;
    private int _selectScroll;
    private bool _confirmReset;
    private bool _helpOpen;
    private Vector3 _pivot;
    private float _distance;
    private int _ticks;
    private int _noticeTicks;
    private bool _newBest;

    /// <summary>Opens a 1280×720 window on the title screen.</summary>
    /// <param name="manifest">The loaded content; it must hold at least one stage.</param>
    /// <param name="save">The player's settings and progress; clears are recorded and written to it.</param>
    /// <param name="smoke">The <c>--smoke-gui</c> run to drive, or null to play normally.</param>
    /// <param name="multisampling">
    /// False asks for no antialiased back buffer, for a driver that offers none; <see cref="Game.Run()"/> throws
    /// <see cref="NoSuitableGraphicsDeviceException"/> when the device can't be made.
    /// </param>
    public CommunityGame(ContentManifest manifest, SaveStore save, SmokeGui? smoke = null, bool multisampling = true)
    {
        _manifest = manifest;
        _save = save;
        _smoke = smoke;
        _selectable = [.. manifest.Packs.SelectMany(p => p.Stages.Select((_, i) => (p, i)))];
        _selectMenu = new MenuList(Math.Max(1, _selectable.Count));
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VirtualScreen.Width,
            PreferredBackBufferHeight = VirtualScreen.Height,
            PreferMultiSampling = multisampling,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        Window.Title = "Puzzle Boy Community Edition";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) =>
        {
            if (Window.ClientBounds.Width > 0 && Window.ClientBounds.Height > 0)
            {
                _graphics.PreferredBackBufferWidth = Window.ClientBounds.Width;
                _graphics.PreferredBackBufferHeight = Window.ClientBounds.Height;
                _graphics.ApplyChanges();
            }
        };

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 30);
        IsMouseVisible = true;
    }

    /// <summary>The process exit code a <c>--smoke-gui</c> run ends with; 0 for a normal run.</summary>
    public int ExitCode => _smoke?.ExitCode ?? 0;

    /// <summary>The screen showing.</summary>
    internal Screen CurrentScreen => _nav.Current;

    /// <summary>The stage in play, or null when none is.</summary>
    internal PlaySession? Session => _session;

    /// <summary>The player's settings and progress.</summary>
    internal SaveStore Save => _save;

    /// <summary>The play camera, which maps screen directions to grid directions.</summary>
    internal CameraRig Camera => _camera;

    /// <inheritdoc />
    protected override void LoadContent()
    {
        _theme = Theme.Load(_manifest.Theme);
        _ui = new Ui(GraphicsDevice, _theme, _manifest.Fonts);
        _board = new BoardRenderer(GraphicsDevice, _theme);
        _sounds = new Sounds();
        ApplySettings(display: true);
        _sounds.StartMusic();
    }

    /// <inheritdoc />
    protected override void UnloadContent()
    {
        _ui.Dispose();
        _board.Dispose();
        _sounds.Dispose();
    }

    /// <inheritdoc />
    protected override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        var pad = GamePad.GetState(PlayerIndex.One);
        var mouse = Mouse.GetState();
        var controls = new Controls(keys, _previousKeys, pad, _previousPad);
        if (_smoke is not null)
        {
            // A smoke run ignores the real keyboard, pad and mouse, so nothing on the desk can steer it.
            controls = _smoke.Input(this);
            mouse = _previousMouse;
        }

        var pp = GraphicsDevice.PresentationParameters;
        _pointer = VirtualScreen.ToVirtual(mouse.Position, pp.BackBufferWidth, pp.BackBufferHeight);
        _pointerMoved = IsActive && mouse.Position != _previousMouse.Position && _ticks > 0;
        _clicked = IsActive && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        _rightClicked = IsActive && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        _wheel = IsActive ? Math.Sign(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue) : 0;

        // The repeat counts every tick, whichever screen reads it.
        _menuUp = _up.Fires(controls.Held(Command.Up));
        _menuDown = _down.Fires(controls.Held(Command.Down));
        _menuLeft = _left.Fires(controls.Held(Command.Left));
        _menuRight = _right.Fires(controls.Held(Command.Right));
        _ticks++;
        _camera.Tick();
        switch (_nav.Current)
        {
            case Screen.Title:
                UpdateTitle(controls);
                break;
            case Screen.StageSelect:
                UpdateStageSelect(controls);
                break;
            case Screen.Playing:
                UpdatePlaying(controls);
                break;
            case Screen.Paused:
                UpdatePaused(controls);
                break;
            case Screen.Settings:
                UpdateSettings(controls);
                break;
            default:
                // Credits and the placeholders hold a single Back.
                if (RunMenu(_backMenu, controls, BackButton) is not null || Cancelled(controls))
                {
                    GoBack();
                }

                break;
        }

        _previousKeys = keys;
        _previousPad = pad;
        _previousMouse = mouse;
        base.Update(gameTime);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_theme["night"]);
        _ui.Backdrop(_ticks);
        switch (_nav.Current)
        {
            case Screen.Title:
                DrawTitle();
                break;
            case Screen.Playing:
            case Screen.Paused:
                DrawPlaying();
                break;
            case Screen.StageSelect:
                DrawStageSelect();
                break;
            case Screen.Settings:
                DrawSettings();
                break;
            case Screen.Credits:
                DrawCredits();
                break;
            case Screen.Editor:
                DrawPlaceholder("LEVEL EDITOR", "Coming soon. You'll paint stages tile by tile, test them as you go, and share them as packs.");
                break;
            default:
                DrawPlaceholder("IMPORT CLASSIC", "Coming later, and always optional. If you own Shin Megami Tensei III: Nocturne, you'll be able to use the classic look and its bonus stages from your own copy. Nothing from it ships with this game.");
                break;
        }

        DrawNotice();
        base.Draw(gameTime);
        _smoke?.AfterDraw(this);
    }

    /// <summary>
    /// Shows the save's notice, such as a damaged file recovered, over any screen for a few seconds. It never takes
    /// input, so play goes on under it.
    /// </summary>
    private void DrawNotice()
    {
        if (_save.Notice is not { } notice)
        {
            return;
        }

        const int Shown = 30 * 8;
        if (++_noticeTicks > Shown)
        {
            _save.ClearNotice();
            _noticeTicks = 0;
            return;
        }

        _ui.Begin();
        var fade = Math.Clamp((Shown - _noticeTicks) / 15f, 0, 1);
        _ui.Panel(240, 128, 800, 96, _ui["ink"] * (0.95f * fade));
        _ui.Rect(240, 128, 8, 96, _ui["ember"] * fade);
        _ui.Wrapped(Font.Body, 19, notice, new Vector2(266, 140), 754, _ui["cream"] * fade, 1.3f);
        _ui.End();
    }

    /// <summary>Ticks for decorative motion: frozen at 0 when the player reduces motion.</summary>
    private int Motion => _save.Data.Settings.ReduceMotion ? 0 : _ticks;

    /// <summary>Puts the saved settings into effect; <paramref name="display"/> also resizes the window or goes fullscreen.</summary>
    private void ApplySettings(bool display)
    {
        var settings = _save.Data.Settings;
        _ui.TextScale = settings.UiScale;
        _ui.HighContrast = settings.HighContrast;
        _ui.ReduceMotion = settings.ReduceMotion;
        _board.HighContrast = settings.HighContrast;
        _camera.Instant = settings.ReduceMotion;
        _sounds.SetVolumes(settings.MusicVolume, settings.SfxVolume, settings.Mute);
        if (!display)
        {
            return;
        }

        // Borderless fullscreen at the desktop's size: no mode switch, so alt-tab and a second display behave.
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        var (width, height) = settings.Fullscreen ? (mode.Width, mode.Height) : GameSettings.WindowSize(settings.WindowScale, mode.Width, mode.Height);
        _graphics.HardwareModeSwitch = false;
        _graphics.IsFullScreen = settings.Fullscreen;
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.ApplyChanges();
    }

    /// <summary>Starts stage <paramref name="index"/> of <paramref name="pack"/> and opens the play screen.</summary>
    private void StartStage(StagePack pack, int index)
    {
        _session = new PlaySession(pack.Stages, index);
        _nav.Open(Screen.Playing);
        FrameStage();
    }

    /// <summary>Points the camera at a newly started stage.</summary>
    private void FrameStage()
    {
        var (minX, minY, maxX, maxY) = BoardRenderer.PlayBounds(_session!.Board);
        var extent = Math.Max(maxX - minX, (maxY - minY) * 1.4f) + 1;
        _distance = Math.Clamp((extent * 1.05f) + 4, 9, 22);
        _pivot = Pivot();
    }

    /// <summary>
    /// Where the camera looks: the middle of a stage that fits the view, or else the active character, held inside the
    /// play area.
    /// </summary>
    private Vector3 Pivot()
    {
        var (minX, minY, maxX, maxY) = BoardRenderer.PlayBounds(_session!.Board);
        var center = new Vector3((minX + maxX) / 2f, 0, -(minY + maxY) / 2f);
        if (maxX - minX <= 12 && maxY - minY <= 9)
        {
            return center;
        }

        // shortcut: large stages follow the character within the play area; a smarter framing can come with C2 stages.
        var (x, y, _) = _session.Where(_session.Board.ActiveIndex);
        return new Vector3(Math.Clamp(x, minX + 4, Math.Max(minX + 4, maxX - 4)), 0, -Math.Clamp(y, minY + 3, Math.Max(minY + 3, maxY - 3)));
    }

    private void GoBack()
    {
        if (_nav.Back())
        {
            _sounds.Play(Sfx.Cancel);
        }
    }

    private bool Cancelled(Controls controls) => controls.Pressed(Command.Back) || _rightClicked;

    /// <summary>
    /// One tick of a vertical menu laid out as <paramref name="layout"/>: arrows, the wheel and the mouse move the focus,
    /// Confirm or a click on the focused item chooses. Returns the chosen item, or null. A scrolled list shows
    /// <paramref name="visible"/> items from <paramref name="first"/>, the first of them at the layout's top.
    /// </summary>
    private int? RunMenu(MenuList menu, Controls controls, MenuLayout layout, int first = 0, int visible = int.MaxValue)
    {
        if (_menuUp || _menuDown || _wheel != 0)
        {
            menu.Move(_menuUp || _wheel > 0 ? -1 : 1);
            _sounds.Play(Sfx.MenuMove);
        }

        var hit = menu.HitTest(_pointer, layout.Left, layout.Top - (first * layout.Spacing), layout.Width, layout.Spacing);
        if (hit < first || hit >= first + visible)
        {
            hit = -1;
        }

        if (_pointerMoved && hit >= 0 && menu.Set(hit))
        {
            _sounds.Play(Sfx.MenuMove);
        }

        if (controls.Pressed(Command.Confirm) || (_clicked && hit >= 0 && hit == menu.Focus))
        {
            _sounds.Play(Sfx.Confirm);
            return menu.Focus;
        }

        return null;
    }

    /// <summary>Where a vertical menu's items are, in virtual pixels: each <see cref="Spacing"/> tall, stacked from <see cref="Top"/>.</summary>
    private readonly record struct MenuLayout(float Left, float Top, float Width, float Spacing);
}
