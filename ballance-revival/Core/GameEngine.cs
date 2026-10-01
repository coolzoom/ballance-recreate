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

    private float _totalTime = 0f;

    public GameEngine(string gameRoot, int width = 1280, int height = 720)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _width = width;
        _height = height;
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(_width, _height, "Ballance Revival - .NET Core");
        Raylib.SetTargetFPS(60);

        string texturesDir = Path.Combine(_gameRoot, "Textures");
        string soundsDir = Path.Combine(_gameRoot, "Sounds");

        _textureManager = new TextureManager(texturesDir);
        _audioManager = new AudioManager(soundsDir);
        _skybox = new SkyboxRenderer(_textureManager);
        _particles = new ParticleSystem();
        _hud = new GameHUD();

        _levelManager = new LevelManager(_gameRoot, _textureManager, _audioManager, _particles, _hud);
        _camera = new Camera3DController(_levelManager.Player.Position);

        UpdateSkyTheme(_levelManager.CurrentLevelNumber);

        while (!Raylib.WindowShouldClose())
        {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
            _totalTime += dt;

            ProcessInput(dt);
            Update(dt);
            Render();
        }

        Dispose();
    }

    private void ProcessInput(float dt)
    {
        if (_camera == null || _levelManager == null) return;

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
                _camera = new Camera3DController(_levelManager.Player.Position);
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
        if (_levelManager == null || _camera == null || _particles == null || _hud == null) return;

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

    private void Render()
    {
        if (_camera == null || _levelManager == null || _skybox == null || _particles == null || _hud == null) return;

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(135, 206, 235, 255));

        // 3D Scene Rendering
        Raylib.BeginMode3D(_camera.Camera);

        // 1. Skybox
        _skybox.Draw(_camera.Camera.Position);

        // 2. Track & Objects
        _levelManager.DrawLevel();

        // 3. Particles
        _particles.Draw();

        Raylib.EndMode3D();

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

        Raylib.EndDrawing();
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

    public void Dispose()
    {
        _textureManager?.Dispose();
        _audioManager?.Dispose();
        _skybox?.Dispose();
        _levelManager?.CurrentRenderer?.Dispose();

        if (Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
        }
    }
}
