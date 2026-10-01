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

    private Texture2D _flameTex;
    private Texture2D _pointTex;

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

    public LevelManager(string gameRoot, TextureManager textureManager, AudioManager audioManager, ParticleSystem particles, GameHUD hud)
    {
        _gameRoot = gameRoot;
        _textureManager = textureManager;
        _audioManager = audioManager;
        _particles = particles;
        _hud = hud;

        Prefabs = new PrefabLibrary(Path.Combine(_gameRoot, "3D Entities", "PH"), textureManager);
        _flameTex = Billboard.LoadMasked(Path.Combine(_gameRoot, "Textures", "Particle_Flames.bmp"));
        _pointTex = Billboard.LoadMasked(Path.Combine(_gameRoot, "Textures", "ExtraParticle.bmp"));
        LoadBallMeshes();
        Player = new BallPlayer(Vector3.Zero, BallMaterial.Wood);
        LoadLevel(1);
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
    }

    public void LoadLevel(int levelNum)
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

        _audioManager.PlayLevelStart();
        _hud.ShowBanner($"LEVEL {CurrentLevelNumber:D2} - START", Color.Gold, 2.5f);
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

    public void RespawnAtCheckpoint()
    {
        Player.Respawn(ActiveRespawnPosition);
        _hud.ShowBanner("RESPAWNED", Color.White, 1.0f);
    }

    public void DrawLevel(Vector3 cameraPos, float time)
    {
        if (CurrentRenderer == null || CurrentLevel == null) return;

        foreach (var entity in CurrentLevel.AllRenderables)
        {
            if (!CurrentLevel.Placeholders.TryGetValue(entity, out string? group))
            {
                CurrentRenderer.DrawEntity(entity);
                continue;
            }

            bool dynamic = group is "P_Extra_Point" or "P_Extra_Life" or "P_Box";
            var prefab = Prefabs.Get(PrefabKey(entity.Name));
            if (prefab == null || prefab.Parts.Count == 0)
            {
                // Level copies of these are untextured editor markers (reset arrows, flame volumes)
                if (prefab != null)
                    DrawFlames(prefab, entity.WorldMatrix, cameraPos, time);
                continue;
            }

            if (!dynamic)
                prefab.Draw(entity.WorldMatrix);
            DrawFlames(prefab, entity.WorldMatrix, cameraPos, time);
        }

        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null) continue;

            Matrix4x4 world = pickup.Entity.WorldMatrix;
            world.Translation = pickup.CurrentPosition;
            world = Matrix4x4.CreateRotationY(pickup.SpinAngle * (MathF.PI / 180f)) * world;

            string key = pickup.Type == PickupType.Point ? "P_Extra_Point" : "P_Extra_Life";
            Prefabs.Get(key)?.Draw(world);

            if (pickup.Type == PickupType.Point)
            {
                float spin = pickup.SpinAngle * (MathF.PI / 180f);
                Raylib.BeginBlendMode(BlendMode.Additive);
                for (int i = 0; i < 3; i++)
                {
                    float t = spin + i * MathF.PI * 2f / 3f;
                    var p = pickup.CurrentPosition + new Vector3(MathF.Cos(t) * 0.7f, 1.6f, MathF.Sin(t) * 0.7f);
                    Billboard.Draw(_pointTex, p, cameraPos, 1.6f, 1.6f, new Color((byte)140, (byte)210, (byte)255, (byte)220));
                }
                Raylib.EndBlendMode();
            }
        }

        foreach (var box in Boxes)
        {
            if (box.Entity == null) continue;
            Matrix4x4 world = box.Entity.WorldMatrix;
            world.Translation = box.Position;
            Prefabs.Get("P_Box")?.Draw(world);
        }

        DrawPlayerBall();
    }

    private void DrawFlames(Prefab prefab, Matrix4x4 world, Vector3 cameraPos, float time)
    {
        if (prefab.FlamePoints.Count == 0 || _flameTex.Id == 0) return;

        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var local in prefab.FlamePoints)
        {
            Vector3 p = Vector3.Transform(local, world) + new Vector3(0f, 0.6f, 0f);
            float flick = 0.82f + 0.18f * MathF.Sin(time * 11f + p.X * 0.7f);
            Billboard.Draw(_flameTex, p, cameraPos, 2.4f * flick, 3.6f, new Color((byte)255, (byte)236, (byte)190, (byte)230));
            Billboard.Draw(_flameTex, p + new Vector3(0f, 1.5f, 0f), cameraPos, 1.5f * flick, 2.8f, new Color((byte)255, (byte)250, (byte)230, (byte)180));
        }
        Raylib.EndBlendMode();
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
