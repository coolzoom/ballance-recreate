using System.Numerics;
using BallanceRevival.Core;
using BallanceRevival.Nmo;
using BallanceRevival.Physics;
using BallanceRevival.Rendering;
using Raylib_cs;

namespace BallanceRevival.Gameplay;

public class LevelManager
{
    private readonly string _gameRoot;
    private readonly TextureManager _textureManager;
    private readonly AudioManager _audioManager;
    private readonly ParticleSystem _particles;
    private readonly GameHUD _hud;

    public NmoLevel? CurrentLevel { get; private set; }
    public MeshRenderer? CurrentRenderer { get; private set; }
    public TriangleMeshCollider Collider { get; private set; } = new();
    public BallPlayer Player { get; private set; }

    public PrefabLibrary Prefabs { get; }
    private Prefab? _ballWood;
    private Prefab? _ballStone;
    private Prefab? _ballPaper;

    private readonly FlameRenderer _flames;
    private Texture2D _pointTex;
    private struct FlameSource
    {
        public Vector3 Base;
        public CheckpointTrigger? Checkpoint;
        public bool Big;
    }

    private readonly List<FlameSource> _flameSources = [];
    private readonly List<Vector4> _visibleFlames = [];

    private readonly List<(Vector3 Position, float Radius)> _flameLights = [];
    private readonly Vector4[] _lightPos = new Vector4[LitShader.MaxLights];
    private readonly Vector3[] _lightColor = new Vector3[LitShader.MaxLights];
    private readonly List<int> _lightOrder = [];
    private static readonly Vector3 FlameLightColor = new(1.0f, 0.38f, 0.8f);

    /// <summary>Lowest floor/rail origin; height fog starts just below it.</summary>
    public float FloorMinY { get; private set; }

    public int CurrentLevelNumber { get; private set; } = 1;
    public int Score { get; private set; } = 1000;
    public int Lives { get; private set; } = 3;
    public float KillY { get; private set; } = -100.0f;

    public Vector3 ActiveRespawnPosition { get; private set; }

    public List<CheckpointTrigger> Checkpoints { get; } = [];
    public List<PickupItem> Pickups { get; } = [];
    public List<TransformerTrigger> Transformers { get; } = [];
    public List<MovableBox> Boxes { get; } = [];

    public bool IsLevelComplete { get; private set; }
    public float LevelCompleteTimer { get; private set; } = 0f;

    public LevelManager(string gameRoot, TextureManager textureManager, AudioManager audioManager, ParticleSystem particles, GameHUD hud, bool announceStart = true)
    {
        _gameRoot = gameRoot;
        _textureManager = textureManager;
        _audioManager = audioManager;
        _particles = particles;
        _hud = hud;

        Prefabs = new PrefabLibrary(Path.Combine(_gameRoot, "3D Entities", "PH"), textureManager);
        _flames = new FlameRenderer(Path.Combine(_gameRoot, "Textures"));
        _pointTex = Billboard.LoadMasked(Path.Combine(_gameRoot, "Textures", "ExtraParticle.bmp"));
        LoadBallMeshes();
        Player = new BallPlayer(Vector3.Zero, BallMaterial.Wood);
        LoadLevel(1, announceStart);
    }

    private void LoadBallMeshes()
    {
        string ballsPath = Path.Combine(_gameRoot, "3D Entities", "Balls.nmo");
        if (!File.Exists(ballsPath)) return;

        var balls = NmoLevel.Load(ballsPath);
        // Ball entities sit at arbitrary positions in Balls.nmo; strip translation so they draw at the origin
        Prefab Make(string entityName)
        {
            var prefab = new Prefab(balls, _textureManager, e => e.Name == entityName);
            foreach (var part in prefab.Parts)
            {
                var m = part.WorldMatrix;
                m.Translation = Vector3.Zero;
                part.WorldMatrix = m;
            }
            return prefab;
        }

        _ballWood = Make("Ball_Wood");
        _ballStone = Make("Ball_Stone");
        _ballPaper = Make("Ball_Paper");

        // The ball never receives its own blob shadow
        _ballWood.SetSurface(new Vector3(0.22f, 20f, 0f));
        _ballStone.SetSurface(new Vector3(0.45f, 48f, 0f));
        _ballPaper.SetSurface(new Vector3(0.06f, 8f, 0f));
        Prefabs.Get("P_Extra_Point")?.SetSurface(new Vector3(0.6f, 64f, 1f));
        Prefabs.Get("P_Extra_Life")?.SetSurface(new Vector3(0.6f, 64f, 1f));
    }

    public void LoadLevel(int levelNum, bool announce = true)
    {
        CurrentLevelNumber = levelNum;
        IsLevelComplete = false;
        LevelCompleteTimer = 0f;
        Score = 1000 + (levelNum - 1) * 200;

        string levelFileName = $"Level_{levelNum:D2}.NMO";
        string levelPath = Path.Combine(_gameRoot, "3D Entities", "Level", levelFileName);

        if (!File.Exists(levelPath))
        {
            levelFileName = $"Level_{levelNum:D2}.nmo";
            levelPath = Path.Combine(_gameRoot, "3D Entities", "Level", levelFileName);
        }

        if (!File.Exists(levelPath))
        {
            Console.WriteLine($"Level file not found: {levelPath}");
            return;
        }

        CurrentRenderer?.Dispose();
        CurrentLevel = NmoLevel.Load(levelPath);
        CurrentRenderer = new MeshRenderer(_textureManager, CurrentLevel);

        // Build physics collider
        Collider = new TriangleMeshCollider(8.0f);
        float minY = float.MaxValue;

        foreach (var floor in CurrentLevel.FloorColliders)
        {
            Collider.AddEntity(floor);
            if (floor.Position.Y < minY) minY = floor.Position.Y;
        }
        foreach (var rail in CurrentLevel.RailColliders)
        {
            Collider.AddEntity(rail);
            if (rail.Position.Y < minY) minY = rail.Position.Y;
        }

        FloorMinY = minY == float.MaxValue ? 0f : minY;
        KillY = minY - 30.0f;

        // Determine spawn position
        Vector3 spawnPos = Vector3.Zero;
        if (CurrentLevel.ResetPointEntities.Count > 0)
        {
            spawnPos = CurrentLevel.ResetPointEntities[0].Position;
        }
        else if (CurrentLevel.StartPoint != null)
        {
            spawnPos = CurrentLevel.StartPoint.Position + new Vector3(0f, 3f, 0f);
        }
        ActiveRespawnPosition = spawnPos;

        // Reset player
        Player.SetMaterial(BallMaterial.Wood);
        Player.Respawn(ActiveRespawnPosition);

        // Build gameplay elements
        BuildInteractiveElements();
        BuildFlameSources();

        if (announce)
        {
            _audioManager.PlayLevelStart();
            _hud.ShowBanner($"LEVEL {CurrentLevelNumber:D2} - START", Color.Gold, 2.5f);
        }
    }

    private void BuildInteractiveElements()
    {
        Checkpoints.Clear();
        Pickups.Clear();
        Transformers.Clear();
        Boxes.Clear();

        if (CurrentLevel == null) return;

        // 1. Checkpoints
        for (int i = 0; i < CurrentLevel.CheckpointEntities.Count; i++)
        {
            var cpEntity = CurrentLevel.CheckpointEntities[i];
            Vector3 spawn = (i + 1 < CurrentLevel.ResetPointEntities.Count)
                ? CurrentLevel.ResetPointEntities[i + 1].Position
                : cpEntity.Position + new Vector3(0f, 3f, 0f);

            Checkpoints.Add(new CheckpointTrigger(cpEntity.Name, cpEntity.Position, spawn, cpEntity));
        }

        // 2. Extra Points
        foreach (var p in CurrentLevel.ExtraPointEntities)
        {
            Pickups.Add(new PickupItem(PickupType.Point, p.Position, p));
        }

        // 3. Extra Lives
        foreach (var l in CurrentLevel.ExtraLifeEntities)
        {
            Pickups.Add(new PickupItem(PickupType.Life, l.Position, l));
        }

        // 4. Transformers
        foreach (var tw in CurrentLevel.TrafoWoodEntities)
            Transformers.Add(new TransformerTrigger(BallMaterial.Wood, tw.Position, tw));
        foreach (var ts in CurrentLevel.TrafoStoneEntities)
            Transformers.Add(new TransformerTrigger(BallMaterial.Stone, ts.Position, ts));
        foreach (var tp in CurrentLevel.TrafoPaperEntities)
            Transformers.Add(new TransformerTrigger(BallMaterial.Paper, tp.Position, tp));

        // 5. Boxes
        foreach (var box in CurrentLevel.BoxEntities)
        {
            Boxes.Add(new MovableBox(box.Position, box));
        }
    }

    /// <summary>Every flame grate in the level; checkpoints must already be built.</summary>
    private void BuildFlameSources()
    {
        _flameSources.Clear();
        if (CurrentLevel == null) return;

        foreach (var entity in CurrentLevel.AllRenderables)
        {
            if (!CurrentLevel.Placeholders.ContainsKey(entity)) continue;
            var prefab = Prefabs.Get(PrefabKey(entity.Name));
            if (prefab == null) continue;
            var checkpoint = Checkpoints.Find(c => c.Entity == entity);
            for (int i = 0; i < prefab.FlamePoints.Count; i++)
            {
                _flameSources.Add(new FlameSource
                {
                    Base = Vector3.Transform(prefab.FlamePoints[i], entity.WorldMatrix),
                    Checkpoint = checkpoint,
                    Big = prefab.FlameNames[i].EndsWith("_Big", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
    }

    /// <summary>
    /// A checkpoint's big center flame burns until the ball reaches it, then the two small side
    /// flames take over. Start platform flames always burn.
    /// </summary>
    private static bool IsLit(FlameSource f) =>
        f.Checkpoint == null || f.Big != f.Checkpoint.IsActivated;

    /// <summary>Uploads the flame lights nearest the ball plus the ball shadow caster.</summary>
    private void ApplyLighting(float time)
    {
        Vector3 focus = Player.Position;
        _flameLights.Clear();
        foreach (var f in _flameSources)
            if (IsLit(f)) _flameLights.Add((f.Base + new Vector3(0f, f.Big ? 2.0f : 1.2f, 0f), f.Big ? 15f : 11f));

        _lightOrder.Clear();
        for (int i = 0; i < _flameLights.Count; i++) _lightOrder.Add(i);
        _lightOrder.Sort((a, b) =>
            Vector3.DistanceSquared(_flameLights[a].Position, focus).CompareTo(Vector3.DistanceSquared(_flameLights[b].Position, focus)));

        int count = Math.Min(_lightOrder.Count, LitShader.MaxLights);
        for (int i = 0; i < count; i++)
        {
            var (pos, radius) = _flameLights[_lightOrder[i]];
            float flick = 0.85f + 0.1f * MathF.Sin(time * 11f + pos.X * 0.7f) + 0.05f * MathF.Sin(time * 23f + pos.Z);
            _lightPos[i] = new Vector4(pos, radius);
            _lightColor[i] = FlameLightColor * (0.85f * flick);
        }
        LitShader.SetLights(_lightPos, _lightColor, count);
        LitShader.SetBall(Player.Position, Player.Properties.Radius);
    }

    public void Update(float dt, float totalTime)
    {
        // Score decay (1 point every 1.5 seconds)
        if (Score > 0 && !IsLevelComplete)
        {
            Score = Math.Max(0, Score - (int)(dt * 0.7f));
        }

        // Check fall death
        if (Player.Position.Y < KillY)
        {
            OnPlayerFall();
            return;
        }

        // Play impact sound
        if (Player.LastImpactSpeed > 2.0f)
        {
            _audioManager.PlayImpact(Player.CurrentMaterial, Player.LastImpactSpeed);
            _particles.EmitBurst(Player.Position - Player.LastContactNormal * Player.Properties.Radius, Color.LightGray, 6, 4.0f, 0.2f);
        }

        // Rolling sound
        _audioManager.PlayRollSound(Player.CurrentMaterial, Player.Velocity.Length(), Player.IsGrounded);

        // Update Checkpoints
        foreach (var cp in Checkpoints)
        {
            if (cp.Check(Player.Position))
            {
                cp.IsActivated = true;
                ActiveRespawnPosition = cp.SpawnPosition;
                _audioManager.PlayCheckpoint();
                _particles.EmitBurst(cp.TriggerPosition, Color.Lime, 30, 10.0f, 0.4f, 1.2f);
                _hud.ShowBanner("CHECKPOINT!", Color.Lime, 2.0f);
            }
        }

        // Update Pickups
        foreach (var p in Pickups)
        {
            p.Update(dt, totalTime);
            if (p.Check(Player.Position))
            {
                p.IsCollected = true;
                if (p.Type == PickupType.Point)
                {
                    Score += 200;
                    _audioManager.PlayExtraPoint();
                    _particles.EmitBurst(p.CurrentPosition, Color.SkyBlue, 20, 8.0f, 0.35f);
                    _hud.ShowBanner("+200 POINTS", Color.SkyBlue, 1.5f);
                }
                else
                {
                    Lives++;
                    _audioManager.PlayExtraLife();
                    _particles.EmitBurst(p.CurrentPosition, Color.Gold, 25, 9.0f, 0.4f);
                    _hud.ShowBanner("EXTRA LIFE!", Color.Gold, 2.0f);
                }
            }
        }

        // Update Transformers
        foreach (var t in Transformers)
        {
            t.Update(dt);
            if (t.Check(Player.Position, Player.CurrentMaterial))
            {
                Player.SetMaterial(t.TargetMaterial);
                t.Cooldown = 3.0f;
                _audioManager.PlayTransformer();
                Color glow = t.TargetMaterial switch
                {
                    BallMaterial.Stone => Color.Gray,
                    BallMaterial.Paper => Color.RayWhite,
                    _ => Color.Orange
                };
                _particles.EmitBurst(Player.Position, glow, 35, 12.0f, 0.5f, 1.0f);
                _hud.ShowBanner($"TRANSFORMED: {t.TargetMaterial.ToString().ToUpper()} BALL", glow, 2.0f);
            }
        }

        // Update Movable Boxes
        foreach (var box in Boxes)
        {
            box.Update(dt, Player);
        }

        // Check goal (PE_Balloon UFO platform)
        if (!IsLevelComplete)
        {
            Vector3? goalPos = CurrentLevel?.EndBalloon?.Position;
            if (goalPos.HasValue && Vector3.Distance(Player.Position, goalPos.Value) < 7.0f)
            {
                IsLevelComplete = true;
                LevelCompleteTimer = 0f;
                _audioManager.PlayLevelComplete();
                _particles.EmitBurst(goalPos.Value, Color.Gold, 60, 15.0f, 0.6f, 2.0f);
                _hud.ShowBanner("LEVEL COMPLETED!", Color.Gold, 4.0f);
            }
        }
        else
        {
            LevelCompleteTimer += dt;
            if (LevelCompleteTimer > 3.5f)
            {
                // Advance to next level
                int nextLevel = (CurrentLevelNumber % 12) + 1;
                LoadLevel(nextLevel);
            }
        }
    }

    public void OnPlayerFall()
    {
        _audioManager.PlayFall();
        Lives--;
        _hud.ShowBanner("YOU FELL!", Color.Red, 2.0f);
        if (Lives <= 0)
        {
            Lives = 3;
            Score = Math.Max(0, Score - 500);
            _hud.ShowBanner("GAME OVER - RESTARTING", Color.Red, 2.5f);
        }

        Player.Respawn(ActiveRespawnPosition);
    }

    public void DisposeEffects() => _flames.Dispose();

    public void RespawnAtCheckpoint()
    {
        Player.Respawn(ActiveRespawnPosition);
        _hud.ShowBanner("RESPAWNED", Color.White, 1.0f);
    }

    public void DrawLevel(Camera3D camera, float time)
    {
        Vector3 cameraPos = camera.Position;
        if (CurrentRenderer == null || CurrentLevel == null) return;

        ApplyLighting(time);

        foreach (var entity in CurrentLevel.AllRenderables)
        {
            if (!CurrentLevel.Placeholders.TryGetValue(entity, out string? group))
            {
                CurrentRenderer.DrawEntity(entity);
                continue;
            }

            bool dynamic = group is "P_Extra_Point" or "P_Extra_Life" or "P_Box";
            var prefab = Prefabs.Get(PrefabKey(entity.Name));
            // Level copies of empty prefabs are untextured editor markers (reset arrows, flame volumes)
            if (prefab == null || prefab.Parts.Count == 0) continue;

            if (!dynamic)
                prefab.Draw(entity.WorldMatrix);
        }

        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null) continue;

            Matrix4x4 world = pickup.Entity.WorldMatrix;
            world.Translation = pickup.CurrentPosition;
            world = Matrix4x4.CreateRotationY(pickup.SpinAngle * (MathF.PI / 180f)) * world;

            string key = pickup.Type == PickupType.Point ? "P_Extra_Point" : "P_Extra_Life";
            Prefabs.Get(key)?.Draw(world);
        }

        foreach (var box in Boxes)
        {
            if (box.Entity == null) continue;
            Matrix4x4 world = box.Entity.WorldMatrix;
            world.Translation = box.Position;
            Prefabs.Get("P_Box")?.Draw(world);
        }

        DrawPlayerBall();
        RenderQueue.FlushTransparent();
        DrawQueuedFlames(camera, time);
    }

    private void DrawQueuedFlames(Camera3D camera, float time)
    {
        Vector3 cameraPos = camera.Position;
        // Drawn after the level and the transparent pass without depth writes,
        // otherwise the sprite quads occlude whatever is behind them.
        _visibleFlames.Clear();
        foreach (var f in _flameSources)
        {
            if (!IsLit(f)) continue;
            float scale = f.Big ? 1.7f : 1f;
            if (RenderQueue.IsVisible(f.Base + new Vector3(0f, 3f * scale, 0f), 5f * scale))
                _visibleFlames.Add(new Vector4(f.Base, scale));
        }
        _flames.DrawAll(camera, _visibleFlames, time);

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);

        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null || pickup.Type != PickupType.Point) continue;
            float spin = pickup.SpinAngle * (MathF.PI / 180f);
            for (int i = 0; i < 3; i++)
            {
                float t = spin + i * MathF.PI * 2f / 3f;
                var p = pickup.CurrentPosition + new Vector3(MathF.Cos(t) * 0.7f, 1.6f, MathF.Sin(t) * 0.7f);
                Billboard.Draw(_pointTex, p, cameraPos, 1.6f, 1.6f, new Color((byte)140, (byte)210, (byte)255, (byte)220));
            }
        }
        Raylib.EndBlendMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    private static string PrefabKey(string entityName)
    {
        int split = entityName.LastIndexOf('_');
        if (split > 0 && entityName[(split + 1)..].All(char.IsDigit))
            return entityName[..split];
        return entityName;
    }

    private void DrawPlayerBall()
    {
        Prefab? ball = Player.CurrentMaterial switch
        {
            BallMaterial.Stone => _ballStone,
            BallMaterial.Paper => _ballPaper,
            _ => _ballWood
        };

        Matrix4x4 ballMatrix = Matrix4x4.CreateFromQuaternion(Player.Rotation) *
                               Matrix4x4.CreateTranslation(Player.Position);

        if (ball != null && ball.Parts.Count > 0)
        {
            ball.Draw(ballMatrix);
        }
        else
        {
            // Procedural fallback sphere if mesh is not available
            Color c = Player.CurrentMaterial switch
            {
                BallMaterial.Stone => Color.DarkGray,
                BallMaterial.Paper => Color.RayWhite,
                _ => Color.Brown
            };
            Raylib.DrawSphere(Player.Position, Player.Properties.Radius, c);
            Raylib.DrawSphereWires(Player.Position, Player.Properties.Radius, 12, 12, Color.White);
        }
    }
}
