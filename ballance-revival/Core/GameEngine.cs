using System.Numerics;
using BallanceRevival.Gameplay;
using BallanceRevival.Rendering;
using Raylib_cs;

namespace BallanceRevival.Core;

public class GameEngine : IDisposable
{
    private readonly string _gameRoot;
    private readonly int _width;
    private readonly int _height;

    private TextureManager? _textureManager;
    private AudioManager? _audioManager;
    private SkyboxRenderer? _skybox;
    private ParticleSystem? _particles;
    private GameHUD? _hud;
    private Camera3DController? _camera;
    private LevelManager? _levelManager;
    private MainMenu? _menu;
    private Texture2D _vignette;

    private float _totalTime = 0f;
    private bool _playing;
    private bool _quit;
    private bool _disposed;

    public string? ScreenshotPath { get; init; }
    public int StartLevel { get; init; } = 1;

    public GameEngine(string gameRoot, int width = 1280, int height = 720)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _width = width;
        _height = height;
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(_width, _height, "Ballance Revival - .NET Core");
        Raylib.SetTargetFPS(60);
        // Raylib's default 0.01 near plane wastes depth precision and makes the baked shadow decals z-fight
        Rlgl.SetClipPlanes(RenderQueue.NearPlane, RenderQueue.FarPlane);
        CreateVignette();

        string texturesDir = Path.Combine(_gameRoot, "Textures");
        string soundsDir = Path.Combine(_gameRoot, "Sounds");

        LitShader.Load();
        _textureManager = new TextureManager(texturesDir);
        _audioManager = new AudioManager(soundsDir);
        _skybox = new SkyboxRenderer(_textureManager);
        _particles = new ParticleSystem();
        _hud = new GameHUD();
        _menu = new MainMenu(texturesDir, soundsDir);

        // --shot of a level skips the menu. Level 0 captures the menu itself.
        bool shotIntoLevel = ScreenshotPath != null && StartLevel > 0;
        _levelManager = new LevelManager(_gameRoot, _textureManager, _audioManager, _particles, _hud, announceStart: shotIntoLevel && StartLevel <= 1);
        if (shotIntoLevel && StartLevel > 1) _levelManager.LoadLevel(StartLevel);
        AimCamera();
        UpdateSkyTheme(_levelManager.CurrentLevelNumber);
        _playing = shotIntoLevel;

        int frame = 0;
        while (!Raylib.WindowShouldClose() && !_quit)
        {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
            _totalTime += dt;

            ProcessInput(dt);
            Update(dt);
            Render();

            if (ScreenshotPath != null && ++frame == 20)
            {
                Image img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, ScreenshotPath);
                Raylib.UnloadImage(img);
                break;
            }
        }

        Dispose();
    }

    private void ProcessInput(float dt)
    {
        if (!_playing || _camera == null || _levelManager == null) return;

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _playing = false;
            _menu?.Show(MenuPage.Pause);
            return;
        }

        // Shift key check
        bool isShift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        // Camera rotation in 90 degree increments (Shift + Left / Right or Q / E)
        if (isShift)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A))
                _camera.RotateLeft90();
            if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D))
                _camera.RotateRight90();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Q))
            _camera.RotateLeft90();
        if (Raylib.IsKeyPressed(KeyboardKey.E))
            _camera.RotateRight90();

        // Level selection (1 - 9)
        for (int i = 1; i <= 9; i++)
        {
            if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.One + (i - 1))))
            {
                _levelManager.LoadLevel(i);
                AimCamera();
                UpdateSkyTheme(i);
                break;
            }
        }

        // Restart from checkpoint
        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            _levelManager.RespawnAtCheckpoint();
        }
    }

    private void Update(float dt)
    {
        if (_levelManager == null || _camera == null || _particles == null || _hud == null || _menu == null) return;

        if (!_playing)
        {
            _menu.SetAmbient(!_menu.CoversGame);
            ApplyMenu(_menu.Update(dt));
            return;
        }

        _menu.SetAmbient(false);

        bool isShift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        // Movement input (WASD or Arrows, only when not holding Shift for camera rotation)
        Vector2 moveInput = Vector2.Zero;
        if (!isShift)
        {
            if (Raylib.IsKeyDown(KeyboardKey.Up) || Raylib.IsKeyDown(KeyboardKey.W)) moveInput.Y += 1f;
            if (Raylib.IsKeyDown(KeyboardKey.Down) || Raylib.IsKeyDown(KeyboardKey.S)) moveInput.Y -= 1f;
            if (Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.A)) moveInput.X -= 1f;
            if (Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.D)) moveInput.X += 1f;
        }

        // Mouse look support (holding Right Mouse Button)
        float mouseDeltaX = 0f;
        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            mouseDeltaX = Raylib.GetMouseDelta().X;
        }

        bool isSpace = Raylib.IsKeyDown(KeyboardKey.Space);

        // Update player ball physics
        _levelManager.Player.Update(dt, moveInput, _camera.Forward, _camera.Right, _levelManager.Collider);

        // Update level events and triggers
        _levelManager.Update(dt, _totalTime);

        // Update camera tracking
        _camera.Update(dt, _levelManager.Player.Position, isSpace, mouseDeltaX);

        // Update particles and HUD
        _particles.Update(dt);
        _hud.Update(dt);
    }

    private void ApplyMenu(MenuCommand cmd)
    {
        if (_levelManager == null || _menu == null) return;

        switch (cmd)
        {
            case MenuCommand.Quit:
                _quit = true;
                break;
            case MenuCommand.StartLevel:
                _levelManager.LoadLevel(_menu.ChosenLevel);
                AimCamera();
                UpdateSkyTheme(_menu.ChosenLevel);
                _playing = true;
                break;
            case MenuCommand.Resume:
                _playing = true;
                break;
            case MenuCommand.RestartLevel:
                _levelManager.LoadLevel(_levelManager.CurrentLevelNumber);
                AimCamera();
                UpdateSkyTheme(_levelManager.CurrentLevelNumber);
                _playing = true;
                break;
            case MenuCommand.LeaveLevel:
                _menu.Show(MenuPage.Main);
                _playing = false;
                break;
        }
    }

    private void Render()
    {
        if (_camera == null || _levelManager == null || _skybox == null || _particles == null || _hud == null || _menu == null) return;

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(135, 206, 235, 255));

        if (!_playing && !_menu.CoversGame)
        {
            DrawMenuSky();
            _menu.Draw(screenWidth, screenHeight);
            Raylib.EndDrawing();
            return;
        }

        // 3D Scene Rendering
        Raylib.BeginMode3D(_camera.Camera);
        RenderQueue.BeginFrame(_camera.Camera, screenWidth / (float)Math.Max(screenHeight, 1));

        LitShader.SetFog(_camera.Camera.Position, _skybox.FogColor, 0.0045f);
        LitShader.SetEnvironment(_skybox.SkyTint, _skybox.GroundTint, _skybox.SunTint);
        LitShader.SetFogHeight(_levelManager.FloorMinY - 6f, 70f);

        // 1. Skybox
        _skybox.Draw(_camera.Camera.Position);

        // 2. Track & Objects
        _levelManager.DrawLevel(_camera.Camera, _totalTime);
        RenderQueue.EndFrame();

        // 3. Particles
        _particles.Draw(_camera.Camera);

        Raylib.EndMode3D();

        if (_vignette.Id != 0)
        {
            Raylib.DrawTexturePro(_vignette, new Rectangle(0, 0, _vignette.Width, _vignette.Height),
                new Rectangle(0, 0, screenWidth, screenHeight), Vector2.Zero, 0f, Color.White);
        }

        // 2D HUD Rendering
        float speed = _levelManager.Player.Velocity.Length();
        _hud.Draw(
            screenWidth,
            screenHeight,
            _levelManager.CurrentLevelNumber,
            _levelManager.Score,
            _levelManager.Lives,
            _levelManager.Player.CurrentMaterial,
            speed
        );

        if (!_playing)
            _menu.Draw(screenWidth, screenHeight);

        Raylib.EndDrawing();
    }

    private void CreateVignette()
    {
        Image img = Raylib.GenImageGradientRadial(256, 256, 0.55f, Color.Blank, new Color((byte)0, (byte)0, (byte)0, (byte)90));
        _vignette = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_vignette, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_vignette, TextureWrap.Clamp);
    }

    private void DrawMenuSky()
    {
        if (_skybox == null) return;
        var cam = new Camera3D
        {
            Position = Vector3.Zero,
            Target = new Vector3(0f, 0.25f, 1f),
            Up = Vector3.UnitY,
            FovY = 60f,
            Projection = CameraProjection.Perspective
        };
        Raylib.BeginMode3D(cam);
        _skybox.Draw(cam.Position);
        Raylib.EndMode3D();
    }

    private void UpdateSkyTheme(int levelNum)
    {
        if (_skybox == null) return;

        string theme = levelNum switch
        {
            1 or 2 => "Sky_A",
            3 or 4 => "Sky_B",
            5 or 6 => "Sky_C",
            7 or 8 => "Sky_D",
            9 or 10 => "Sky_E",
            _ => "Sky_F"
        };
        _skybox.SetTheme(theme);
    }

    private void AimCamera()
    {
        if (_levelManager == null) return;
        _camera = new Camera3DController(_levelManager.Player.Position);
        var resets = _levelManager.CurrentLevel?.ResetPointEntities;
        if (resets != null && resets.Count >= 2)
            _camera.LookAlong(resets[1].Position - resets[0].Position);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _menu?.Dispose();
        _textureManager?.Dispose();
        _audioManager?.Dispose();
        _skybox?.Dispose();
        _particles?.Dispose();
        if (_vignette.Id != 0) Raylib.UnloadTexture(_vignette);
        _levelManager?.CurrentRenderer?.Dispose();
        _levelManager?.Prefabs.Dispose();
        _levelManager?.DisposeEffects();
        LitShader.Unload();

        if (Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
        }
    }
}
