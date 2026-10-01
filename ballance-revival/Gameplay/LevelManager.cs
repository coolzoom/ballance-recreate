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

    public NmoMesh? BallWoodMesh { get; private set; }
    public NmoMesh? BallStoneMesh { get; private set; }
    public NmoMesh? BallPaperMesh { get; private set; }

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

        LoadBallMeshes();
        Player = new BallPlayer(Vector3.Zero, BallMaterial.Wood);
        LoadLevel(1);
    }

    private void LoadBallMeshes()
    {
        string ballsPath = Path.Combine(_gameRoot, "3D Entities", "Balls.nmo");
        if (File.Exists(ballsPath))
        {
            var ballsFile = NmoFile.Load(ballsPath);
            foreach (var obj in ballsFile.Objects)
            {
                if (obj.ClassId == 32)
                {
                    if (obj.Name == "Ball_Wood_Mesh")
                        BallWoodMesh = NmoMesh.FromObject(obj);
                    else if (obj.Name == "Ball_Stone_HighRes_Mesh" || obj.Name == "Ball_Stone_Mesh")
                        BallStoneMesh ??= NmoMesh.FromObject(obj);
                    else if (obj.Name == "Ball_Paper_Mesh")
                        BallPaperMesh = NmoMesh.FromObject(obj);
                }
            }
        }
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

        // Check Goal (End Dome or Balloon)
        if (!IsLevelComplete)
        {
            Vector3? goalPos = CurrentLevel?.EndDome?.Position ?? CurrentLevel?.EndBalloon?.Position;
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

    public void DrawLevel()
    {
        if (CurrentRenderer == null || CurrentLevel == null) return;

        // 1. Draw static level geometry
        foreach (var entity in CurrentLevel.AllRenderables)
        {
            // Skip dynamic elements drawn separately
            string name = entity.Name;
            if (name.StartsWith("P_Extra") || name.StartsWith("P_Box") || name.StartsWith("P_Ball_"))
                continue;

            CurrentRenderer.DrawEntity(entity);
        }

        // 2. Draw Pickups (spinning crystals)
        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity?.Mesh == null) continue;

            Matrix4x4 mat = Matrix4x4.CreateRotationY(pickup.SpinAngle * (MathF.PI / 180f)) *
                            Matrix4x4.CreateTranslation(pickup.CurrentPosition);
            CurrentRenderer.DrawMesh(pickup.Entity.Mesh, mat);
        }

        // 3. Draw Boxes
        foreach (var box in Boxes)
        {
            if (box.Entity?.Mesh == null) continue;
            Matrix4x4 mat = Matrix4x4.CreateTranslation(box.Position);
            CurrentRenderer.DrawMesh(box.Entity.Mesh, mat);
        }

        // 4. Draw Player Ball
        DrawPlayerBall();
    }

    private void DrawPlayerBall()
    {
        NmoMesh? ballMesh = Player.CurrentMaterial switch
        {
            BallMaterial.Stone => BallStoneMesh,
            BallMaterial.Paper => BallPaperMesh,
            _ => BallWoodMesh
        };

        Matrix4x4 ballMatrix = Matrix4x4.CreateFromQuaternion(Player.Rotation) *
                               Matrix4x4.CreateTranslation(Player.Position);

        if (ballMesh != null && CurrentRenderer != null)
        {
            CurrentRenderer.DrawMesh(ballMesh, ballMatrix);
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
