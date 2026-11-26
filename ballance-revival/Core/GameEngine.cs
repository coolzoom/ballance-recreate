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
    private RenderPipeline? _pipeline;
    private ShadowMap? _shadowMap;
    private Texture2D _vignette;

    private float _totalTime = 0f;
    private bool _playing;
    private bool _quit;
    private bool _disposed;
    private Vector2 _mousePressPos;
    private float _mouseDragDist;
    private bool _leftButtonTracking;

    public string? ScreenshotPath { get; init; }
    public int StartLevel { get; init; } = 1;
    public BallMaterial? StartMaterial { get; init; }
    public PostProcessDebugMode? InitialDebugMode { get; init; }
    public float? InitialMood { get; init; }
    public float? InitialSnow { get; init; }
    public float? InitialGodRays { get; init; }
    public float? InitialFog { get; init; }
    public float? InitialClouds { get; init; }
    public bool? InitialSsr { get; init; }
    public bool StartInOptions { get; init; }
    private GraphicsSettings _settings = new();

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
        _settings = GraphicsSettings.Load();
        if (InitialDebugMode.HasValue) _settings.DebugMode = InitialDebugMode.Value;
        if (InitialMood.HasValue) _settings.AtmosphereMood = InitialMood.Value;
        if (InitialSnow.HasValue) _settings.SnowIntensity = InitialSnow.Value;
        if (InitialGodRays.HasValue) _settings.GodRaysIntensity = InitialGodRays.Value;
        if (InitialFog.HasValue) _settings.VolumetricFogIntensity = InitialFog.Value;
        if (InitialClouds.HasValue) _settings.VolumetricCloudsCoverage = InitialClouds.Value;
        if (InitialSsr.HasValue) _settings.EnableSsr = InitialSsr.Value;
        _settings.Clamp();

        _pipeline = new RenderPipeline
        {
            DebugMode = _settings.DebugMode,
            BloomIntensity = _settings.BloomIntensity,
            GodRaysIntensity = _settings.GodRaysIntensity,
            VolumetricFogIntensity = _settings.VolumetricFogIntensity,
            VolumetricCloudsCoverage = _settings.VolumetricCloudsCoverage,
            EnableSsr = _settings.EnableSsr
        };
        _shadowMap = new ShadowMap();
        _textureManager = new TextureManager(texturesDir);
        _audioManager = new AudioManager(soundsDir);
        _skybox = new SkyboxRenderer(_textureManager);
        _particles = new ParticleSystem();
        _hud = new GameHUD(texturesDir);
        _menu = new MainMenu(texturesDir, soundsDir, _settings);

        // --shot of a level skips the menu. Level 0 captures the menu itself.
        bool shotIntoLevel = ScreenshotPath != null && StartLevel > 0;
        _levelManager = new LevelManager(_gameRoot, _textureManager, _audioManager, _particles, _hud, announceStart: shotIntoLevel && StartLevel <= 1);
        if (shotIntoLevel && StartLevel > 1) _levelManager.LoadLevel(StartLevel);
        if (StartMaterial.HasValue) _levelManager.Player.SetMaterial(StartMaterial.Value);
        AimCamera();
        UpdateSkyTheme(_levelManager.CurrentLevelNumber);
        if (StartInOptions)
        {
            _playing = false;
            _menu.ShowOptions(shotIntoLevel ? MenuPage.Pause : MenuPage.Main);
        }
        else
        {
            _playing = shotIntoLevel;
        }

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

        // Cycle HDR Post-Process Debug View (F1: Off -> Linear Depth -> World Normal -> World Pos -> Bloom)
        if (Raylib.IsKeyPressed(KeyboardKey.F1))
        {
            _settings.CycleDebugMode();
            _settings.Save();
            _hud?.ShowBanner($"POST DEBUG: {_settings.DebugMode.ToString().ToUpper()}", Color.SkyBlue, 1.8f);
        }

        // Toggle Moody / Night High-Contrast Atmosphere (F2)
        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            _settings.ToggleAtmosphere();
            _settings.Save();
            _hud?.ShowBanner(_settings.AtmosphereMood > 0.5f ? "ATMOSPHERE: NIGHT / MOODY" : "ATMOSPHERE: CLASSIC", Color.Gold, 1.8f);
        }

        // Cycle HDR Bloom intensity (F3)
        if (Raylib.IsKeyPressed(KeyboardKey.F3))
        {
            _settings.CycleBloom();
            _settings.Save();
            _hud?.ShowBanner($"HDR BLOOM: {_settings.BloomLabel.ToUpper()}", Color.SkyBlue, 1.5f);
        }

        // Cycle Stage 2 Dynamic Y-Up Snow Accumulation (F4)
        if (Raylib.IsKeyPressed(KeyboardKey.F4))
        {
            _settings.CycleSnow();
            _settings.Save();
            _hud?.ShowBanner($"SNOW ACCUMULATION: {_settings.SnowLabel.ToUpper()}", Color.RayWhite, 1.5f);
        }

        // Cycle Stage 4A Volumetric God Rays (F5)
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            _settings.CycleGodRays();
            _settings.Save();
            _hud?.ShowBanner($"GOD RAYS: {_settings.GodRaysLabel.ToUpper()}", Color.Gold, 1.5f);
        }

        // Toggle Stage 4C Metallic SSR (F6)
        if (Raylib.IsKeyPressed(KeyboardKey.F6))
        {
            _settings.ToggleSsr();
            _settings.Save();
            _hud?.ShowBanner($"METALLIC SSR: {_settings.SsrLabel.ToUpper()}", Color.SkyBlue, 1.5f);
        }

        // Cycle Stage 4A Volumetric Fog (F7)
        if (Raylib.IsKeyPressed(KeyboardKey.F7))
        {
            _settings.CycleVolumetricFog();
            _settings.Save();
            _hud?.ShowBanner($"VOLUMETRIC FOG: {_settings.VolumetricFogLabel.ToUpper()}", Color.SkyBlue, 1.5f);
        }

        // Cycle Stage 4B Volumetric Clouds (F8)
        if (Raylib.IsKeyPressed(KeyboardKey.F8))
        {
            _settings.CycleVolumetricClouds();
            _settings.Save();
            _hud?.ShowBanner($"VOLUMETRIC CLOUDS: {_settings.VolumetricCloudsLabel.ToUpper()}", Color.RayWhite, 1.5f);
        }

        // Toggle Real-Time Directional Shadows (F9)
        if (Raylib.IsKeyPressed(KeyboardKey.F9))
        {
            _settings.ToggleShadows();
            _settings.Save();
            _hud?.ShowBanner($"DYNAMIC SHADOWS: {_settings.ShadowsLabel.ToUpper()}", Color.Gold, 1.5f);
        }
    }

    private void Update(float dt)
    {
        if (_levelManager == null || _camera == null || _particles == null || _hud == null || _menu == null) return;

        if (!_playing)
        {
            _audioManager?.StopLevelMusic();
            _menu.SetAmbient(!_menu.CoversGame);
            ApplyMenu(_menu.Update(dt));
            return;
        }

        _menu.SetAmbient(false);
        _audioManager?.UpdateLevelMusic(_levelManager.CurrentLevelNumber);

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

        // Mouse / trackpad camera rotation:
        // - Hold Left or Right button and drag: smooth yaw rotation
        // - Quick Left click / trackpad tap (drag < 8px): snap 90° left (left half) or right (right half)
        // - Two-finger horizontal trackpad scroll: smooth yaw rotation
        float mouseDeltaX = 0f;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _mousePressPos = Raylib.GetMousePosition();
            _mouseDragDist = 0f;
            _leftButtonTracking = true;
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Left) && _leftButtonTracking)
        {
            Vector2 delta = Raylib.GetMouseDelta();
            _mouseDragDist += delta.Length();
            if (_mouseDragDist > 6f)
                mouseDeltaX += delta.X;
        }
        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && _leftButtonTracking)
        {
            _leftButtonTracking = false;
            if (_mouseDragDist <= 8f)
            {
                if (_mousePressPos.X < Raylib.GetScreenWidth() * 0.5f)
                    _camera.RotateLeft90();
                else
                    _camera.RotateRight90();
            }
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            mouseDeltaX += Raylib.GetMouseDelta().X;
        }
        float wheelX = Raylib.GetMouseWheelMoveV().X;
        if (MathF.Abs(wheelX) > 0.01f)
        {
            mouseDeltaX -= wheelX * 28f;
        }

        bool isSpace = Raylib.IsKeyDown(KeyboardKey.Space);

        // Update player ball physics (paused while Transformer levitation animation runs)
        if (!_levelManager.IsTransforming)
            _levelManager.Player.Update(dt, moveInput, _camera.Forward, _camera.Right, _levelManager.Collider, _levelManager.Boxes);

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

        // Sync live graphics settings to pipeline every frame (reflects Options menu & F1-F8 immediately)
        if (_pipeline != null)
        {
            _pipeline.DebugMode = _settings.DebugMode;
            _pipeline.BloomIntensity = _settings.BloomIntensity;
            _pipeline.GodRaysIntensity = _settings.GodRaysIntensity;
            _pipeline.VolumetricFogIntensity = _settings.VolumetricFogIntensity;
            _pipeline.VolumetricCloudsCoverage = _settings.VolumetricCloudsCoverage;
            _pipeline.EnableSsr = _settings.EnableSsr;
        }
        float mood = _settings.AtmosphereMood;
        float snow = _settings.SnowIntensity;

        // 0. Real-Time Directional Shadow Pass (2048x2048 orthographic depth map following the player)
        bool hasShadows = false;
        if (_settings.EnableShadows && _shadowMap != null && _shadowMap.BeginShadowPass(_levelManager.Player.Position, LitShader.SunDir))
        {
            _levelManager.DrawShadowCasters(_totalTime);
            _shadowMap.EndShadowPass();
            hasShadows = true;
        }
        LitShader.SetShadowMap(
            hasShadows ? _shadowMap!.DepthTexture : default,
            hasShadows ? _shadowMap!.LightViewProjection : Matrix4x4.Identity,
            hasShadows);

        // 3D Scene Rendering -> HDR RGBA16F FBO + sampleable Depth Texture
        int renderWidth = Math.Max(Raylib.GetRenderWidth(), screenWidth);
        int renderHeight = Math.Max(Raylib.GetRenderHeight(), screenHeight);
        bool offscreen = _pipeline != null && _pipeline.BeginScene(renderWidth, renderHeight);

        Raylib.BeginMode3D(_camera.Camera);
        RenderQueue.BeginFrame(_camera.Camera, screenWidth / (float)Math.Max(screenHeight, 1));

        Vector3 activeFog = Vector3.Lerp(_skybox.FogColor, new Vector3(0.08f, 0.10f, 0.18f), mood);
        LitShader.SetFog(_camera.Camera.Position, activeFog, 0.0045f + mood * 0.0015f);
        LitShader.SetEnvironment(_skybox.SkyTint, _skybox.GroundTint, _skybox.SunTint);
        LitShader.SetAtmosphereMood(mood);
        LitShader.SetSnowIntensity(snow);
        LitShader.SetFogHeight(_levelManager.FloorMinY - 6f, 70f);

        // 1. Skybox
        _skybox.Draw(_camera.Camera.Position, mood);

        // 2. Track & Objects
        _levelManager.DrawLevel(_camera.Camera, _totalTime);
        RenderQueue.EndFrame();

        // 3. Particles
        _particles.Draw(_camera.Camera);

        Raylib.EndMode3D();

        if (offscreen)
        {
            _pipeline!.EndScene();
            // Stage 1-4 Composite: Bloom + Volumetric Fog & God Rays + Volumetric Clouds + Metallic SSR + ACES -> screen
            _pipeline.ComposeToScreen(
                screenWidth,
                screenHeight,
                _skybox.SunTint,
                _skybox.SkyTint,
                activeFog,
                mood,
                _levelManager.FloorMinY,
                _totalTime);
        }

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
        _skybox.Draw(cam.Position, _settings.AtmosphereMood);
        Raylib.EndMode3D();
    }

    private void UpdateSkyTheme(int levelNum)
    {
        if (_skybox == null) return;

        // Exact skybox mapping from original Levelinit.nmo AllLevel table
        string theme = levelNum switch
        {
            1 => "Sky_L",
            2 => "Sky_E",
            3 => "Sky_A",
            4 => "Sky_F",
            5 => "Sky_C",
            6 => "Sky_H",
            7 => "Sky_D",
            8 => "Sky_G",
            9 => "Sky_K",
            10 => "Sky_B",
            11 => "Sky_J",
            _ => "Sky_I"
        };
        _skybox.SetTheme(theme);
    }

    private void AimCamera()
    {
        if (_levelManager == null) return;
        _camera = new Camera3DController(_levelManager.Player.Position);

        // Align cleanly along the start platform's forward axis (+Z row of PS_Levelstart),
        // snapped to exact 90° cardinal orientation matching the original Ballance camera.
        var start = _levelManager.CurrentLevel?.StartPoint;
        if (start != null)
        {
            var m = start.WorldMatrix;
            Vector3 startFwd = new(m.M31, 0f, m.M33);
            if (startFwd.LengthSquared() > 1e-4f)
            {
                _camera.LookAlong(startFwd, snapTo90: true);
                return;
            }
        }

        var resets = _levelManager.CurrentLevel?.ResetPointEntities;
        if (resets != null && resets.Count >= 2)
            _camera.LookAlong(resets[1].Position - resets[0].Position, snapTo90: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _menu?.Dispose();
        _hud?.Dispose();
        _shadowMap?.Dispose();
        _pipeline?.Dispose();
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
