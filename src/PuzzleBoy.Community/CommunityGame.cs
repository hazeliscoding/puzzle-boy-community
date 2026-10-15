using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PuzzleBoy.Community.App;
using PuzzleBoy.Community.Audio;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Editor;
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
    private readonly MenuList _classicPauseMenu = new(ClassicPauseActions.Length);
    private readonly MenuList _settingsMenu = new(SettingsMenu.Labels.Length);
    private readonly MenuList _confirmMenu = new(2);
    private readonly MenuList _backMenu = new(1);
    private readonly List<(RectangleF Box, int Tab)> _tabBoxes = [];
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
    private StagePack? _playPack;
    private bool _playtest;
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
    private List<StagePack> _selectPacks = [];
    private List<(StagePack Pack, int Index)> _selectable = [];
    private MenuList _selectMenu = new(1);
    private int _selectTab;
    private List<StagePack> _importedPacks = [];
    private readonly List<string> _classicProblems = [];
    private List<string> _importZips = [];
    private string? _importPending;
    private string? _importStatus;
    private MenuList _importMenu = new(1);
    private bool _confirmReset;
    private bool _helpOpen;
    private int _helpPage;
    private int _ticks;
    private int _noticeTicks;
    private string? _notice;
    private bool _newBest;

    // The Classic theme: the presenter of the classic pack last loaded, and the one the stage in play uses (null for the
    // Default theme). Which one is chosen when a stage starts, after a frame that says it's loading.
    private ClassicPresenter? _classicPresenter;
    private ClassicPresenter? _classic;
    private ClassicStageView? _classicStage;
    private bool _themePending;
    private bool _themeShown;
    private bool _classicPlaying;

    // The Nocturne-styled interface: checked once the window is up, and again after a theme change or an import; it loads
    // after a frame that says so. A schema 1 pack's owner is told once a session to import again.
    private bool _skinChecked;
    private bool _skinPending;
    private bool _skinShown;
    private bool _reimportTold;

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
            // ApplyChanges raises this event again, endlessly in fullscreen, so only a window the player resized applies.
            var (width, height) = (Window.ClientBounds.Width, Window.ClientBounds.Height);
            if (!_graphics.IsFullScreen && width > 0 && height > 0
                && (width != _graphics.PreferredBackBufferWidth || height != _graphics.PreferredBackBufferHeight))
            {
                _graphics.PreferredBackBufferWidth = width;
                _graphics.PreferredBackBufferHeight = height;
                _graphics.ApplyChanges();
            }
        };

        // Typed text for the editor's title, a pack's and the classic game's folder; a smoke run takes none from the real keyboard.
        Window.TextInput += (_, e) =>
        {
            if (_smoke is null && (_overlay is EditorOverlay.Title or EditorOverlay.ExportText || (_nav.Current == Screen.ImportClassic && _classicPath is not null)))
            {
                _typed.Append(e.Character);
            }
        };

        Exiting += HoldExitForUnsavedChanges;
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

    /// <summary>The pack stage select shows, or null before it first opens or on the Import tab.</summary>
    internal StagePack? SelectPack => _selectTab < _selectPacks.Count ? _selectPacks[_selectTab] : null;

    /// <summary>Stage select's Import tab, after every pack's.</summary>
    internal int ImportTab => _selectPacks.Count;

    /// <summary>True when stage select shows its Import tab.</summary>
    internal bool ImportTabOpen => _nav.Current == Screen.StageSelect && _selectTab == ImportTab;

    /// <summary>True while an import waits for the player to choose between replacing a pack and keeping both.</summary>
    internal bool ImportConflict => _importPending is not null;

    /// <summary>The imported packs, as stage select last read them.</summary>
    internal IReadOnlyList<StagePack> ImportedPacks => _importedPacks;

    /// <summary>The loaded content.</summary>
    internal ContentManifest Manifest => _manifest;

    /// <summary>The play camera, which maps screen directions to grid directions.</summary>
    internal CameraRig Camera => _camera;

    /// <summary>True while the stage in play is drawn in the Classic theme.</summary>
    internal bool PlaysClassic => _classic is not null && !_themePending;

    /// <summary>
    /// True while play shows the original's HUD, □ menu and help: the Classic theme with a schema 2 pack, while the
    /// interface is Nocturne-styled (a theme changed mid-stage turns it back to Community's at once).
    /// </summary>
    internal bool ClassicMenus => PlaysClassic && _classic!.HasHud && _ui.Skin is not null;

    /// <summary>True while Community's screens are drawn in the Nocturne-styled interface.</summary>
    internal bool Skinned => _ui.Skin is not null;

    /// <summary>Strings the Nocturne-styled interface's font couldn't draw, which Community's font drew instead.</summary>
    internal IReadOnlyCollection<string> SkinFallbacks => _ui.SkinFallbacks;

    /// <summary>What the pause menu's items do, in order, in the theme in play.</summary>
    internal IReadOnlyList<PauseAction> PauseMenuActions => ClassicMenus ? ClassicPauseActions : PauseActions;

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
        _classicPresenter?.Dispose();
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
        _mouse = mouse;

        // The repeat counts every tick, whichever screen reads it.
        _menuUp = _up.Fires(controls.Held(Command.Up));
        _menuDown = _down.Fires(controls.Held(Command.Down));
        _menuLeft = _left.Fires(controls.Held(Command.Left));
        _menuRight = _right.Fires(controls.Held(Command.Right));
        _ticks++;
        _camera.Tick();
        if (!_skinChecked)
        {
            _skinChecked = true;
            RequestSkin();
        }

        if (_skinPending)
        {
            // The frame that says the interface is loading has shown; nothing else runs until it's loaded.
            if (_skinShown)
            {
                (_skinPending, _skinShown) = (false, false);
                LoadSkin();
            }

            (_previousKeys, _previousPad, _previousMouse) = (keys, pad, mouse);
            base.Update(gameTime);
            return;
        }

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
            case Screen.Editor:
                UpdateEditor(controls);
                break;
            case Screen.ImportClassic:
                UpdateImportClassic(controls);
                break;
            default:
                // Credits holds a single Back.
                if (RunMenu(_backMenu, controls, CreditsBack) is not null || Cancelled(controls))
                {
                    GoBack();
                }

                break;
        }

        // The Classic theme's music plays while its stage or pause shows; Community's loop everywhere else.
        var classicPlaying = PlaysClassic && _nav.Current is Screen.Playing or Screen.Paused;
        if (classicPlaying != _classicPlaying)
        {
            _classicPlaying = classicPlaying;
            if (classicPlaying)
            {
                _sounds.StopMusic();
                var settings = _save.Data.Settings;
                _classic!.Enter(settings.SfxVolume, settings.MusicVolume, settings.Mute);
            }
            else
            {
                _classicPresenter?.Leave();
                _sounds.StartMusic();
            }
        }

        // The original HUD's timers run once a tick, after play and the menu.
        if (ClassicMenus && _session is { } session && _nav.Current is Screen.Playing or Screen.Paused)
        {
            _classic!.Clock.Tick(session, _camera, _nav.Current == Screen.Playing ? ClassicOverlay.None : _helpOpen ? ClassicOverlay.Help : ClassicOverlay.Menu);
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
                DrawEditor();
                break;
            case Screen.ImportClassic:
                DrawImportClassic();
                break;
        }

        if (_skinPending)
        {
            _ui.Begin();
            _ui.Panel(390, 320, 500, 80, _ui["ink"] * 0.85f);
            _ui.Text(Font.Title, 18, "LOADING THE CLASSIC THEME", new Vector2(640, 348), _ui["ember"], 0.5f, 2);
            _ui.End();
            _skinShown = true;
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
        if ((_notice ?? _save.Notice) is not { } notice)
        {
            return;
        }

        const int Shown = 30 * 8;
        if (++_noticeTicks > Shown)
        {
            if (_notice is not null)
            {
                _notice = null;
            }
            else
            {
                _save.ClearNotice();
            }

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

    /// <summary>
    /// Starts stage <paramref name="index"/> of <paramref name="pack"/> and opens the play screen. Every stage source starts
    /// here, so the theme is chosen here: at once for Default, or after a loading frame when a classic pack may be used.
    /// </summary>
    private void StartStage(StagePack pack, int index)
    {
        (_playPack, _playtest, _newBest) = (pack, false, false);
        _session = new PlaySession(pack.Stages, index);
        (_classic, _themePending, _themeShown) = (null, false, false);
        if (_save.Data.Settings.Theme != Themes.Default && ClassicPack.IsIn(ClassicStages.PackFolder(_save.Folder)))
        {
            _themePending = true;
        }
        else
        {
            ChooseTheme();
        }

        _nav.Open(Screen.Playing);
    }

    /// <summary>
    /// Picks the theme for the stage starting: the Classic theme when the setting allows it and the classic pack passes its
    /// check and decodes, else the Default theme, with a notice saying why when the player should know.
    /// </summary>
    private void ChooseTheme()
    {
        _classic = null;
        var setting = _save.Data.Settings.Theme;
        var folder = ClassicStages.PackFolder(_save.Folder);
        var present = ClassicPack.IsIn(folder);
        string? problem = null;
        var pack = setting != Themes.Default && present ? ClassicPack.Open(folder, checkHashes: true, out problem) : null;
        if (!ClassicTheme.Choose(setting, present, problem, out var notice))
        {
            _ui.Skin = null;
            ShowNotice(notice);
            return;
        }

        _classic = Presenter(pack!);
        _ui.Skin = _classic?.Skin;
        if (_classic is not null && !_classic.HasHud)
        {
            TellReimport(pack!);
        }
    }

    /// <summary>
    /// The presenter for <paramref name="pack"/>: the one already made when it decodes to the same assets, else a new one.
    /// Null, with a notice, when the pack doesn't decode.
    /// </summary>
    private ClassicPresenter? Presenter(ClassicPack pack)
    {
        try
        {
            var assets = ClassicAssetCache.Load(pack);
            if (!ReferenceEquals(_classicPresenter?.Assets, assets))
            {
                // The skin is the old presenter's; it goes with it, and LoadSkin puts the new one's in place.
                _ui.Skin = null;
                _classicPresenter?.Dispose();
                _classicPresenter = new ClassicPresenter(GraphicsDevice, assets);
                _classicStage = new ClassicStageView(assets.Character);
            }

            return _classicPresenter;
        }
        catch (Exception e)
        {
            // The bundle reader throws all sorts for a damaged bundle; any of them means the same to the player.
            ShowNotice($"The Classic theme couldn't be loaded ({e.GetType().Name}), so this plays with the Default theme. Import your classic pack again.");
            return null;
        }
    }

    /// <summary>
    /// Asks for the Nocturne-styled interface to match the theme setting and the classic pack: dropped at once when they
    /// rule it out, else loaded after a frame that says so.
    /// </summary>
    private void RequestSkin()
    {
        if (!ClassicTheme.UsesInterface(_save.Data.Settings.Theme, ClassicPack.IsIn(ClassicStages.PackFolder(_save.Folder))))
        {
            _ui.Skin = null;
            return;
        }

        _skinPending = true;
    }

    /// <summary>
    /// Loads the Nocturne-styled interface for Community's screens from the classic pack, when the setting allows the
    /// Classic theme and the pack passes its check, decodes and has the interface. Otherwise Community's own interface
    /// shows: a pack that fails its check gets its notice when a stage starts, and one without the interface says so here.
    /// </summary>
    private void LoadSkin()
    {
        _ui.Skin = null;
        var folder = ClassicStages.PackFolder(_save.Folder);
        if (_save.Data.Settings.Theme == Themes.Default || ClassicPack.Open(folder, checkHashes: true, out _) is not { } pack)
        {
            return;
        }

        if (Presenter(pack) is not { } presenter)
        {
            return;
        }

        _ui.Skin = presenter.Skin;
        if (presenter.Skin is null)
        {
            TellReimport(pack);
        }
    }

    /// <summary>
    /// Says once a session why the classic pack's interface isn't shown: a schema 1 pack has none, and a schema 2 pack's
    /// didn't decode.
    /// </summary>
    private void TellReimport(ClassicPack pack)
    {
        if (_reimportTold)
        {
            return;
        }

        _reimportTold = true;
        ShowNotice(pack.HasUi
            ? $"The Nocturne-styled menus couldn't be loaded ({_classicPresenter?.Assets.UiProblem}), so Community's menus show. Import your classic pack again."
            : ReimportNotice);
    }

    /// <summary>Shows <paramref name="notice"/> over the game for a few seconds, if there is one.</summary>
    private void ShowNotice(string? notice)
    {
        if (notice is not null)
        {
            (_notice, _noticeTicks) = (notice, 0);
        }
    }

    /// <summary>
    /// Plays <paramref name="sfx"/>: in the Classic theme's play and pause, the original's effect for it, if it has one;
    /// everywhere else, Community's.
    /// </summary>
    private void Sound(Sfx sfx)
    {
        if (PlaysClassic && _nav.Current is Screen.Playing or Screen.Paused)
        {
            if (ClassicPresenter.Effect(sfx) is { } number)
            {
                _classic!.Play(number);
            }
        }
        else
        {
            _sounds.Play(sfx);
        }
    }

    private void GoBack()
    {
        if (_nav.Back())
        {
            Sound(Sfx.Cancel);
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
            Sound(Sfx.MenuMove);
        }

        var hit = menu.HitTest(_pointer, layout.Left, layout.Top - (first * layout.Spacing), layout.Width, layout.Spacing);
        if (hit < first || hit >= first + visible)
        {
            hit = -1;
        }

        if (_pointerMoved && hit >= 0 && menu.Set(hit))
        {
            Sound(Sfx.MenuMove);
        }

        if (controls.Pressed(Command.Confirm) || (_clicked && hit >= 0 && hit == menu.Focus))
        {
            Sound(Sfx.Confirm);
            return menu.Focus;
        }

        return null;
    }

    /// <summary>Where a vertical menu's items are, in virtual pixels: each <see cref="Spacing"/> tall, stacked from <see cref="Top"/>.</summary>
    private readonly record struct MenuLayout(float Left, float Top, float Width, float Spacing);
}
