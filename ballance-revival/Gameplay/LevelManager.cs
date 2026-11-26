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
    private Prefab? _lightningSphere;
    private Prefab? _animTrafo;
    private readonly Texture2D[] _lightningFrames = new Texture2D[3];

    // Active transformer animation state (AnimTrafo.nmo + Ball_LightningSphere)
    public bool IsTransforming { get; private set; }
    private float _trafoTimer;
    private const float TrafoDuration = 1.65f;
    private TransformerTrigger? _activeTrafo;
    private Vector3 _trafoStartPos;
    private bool _trafoSwapped;
    private float _trafoSparkAccum;

    private readonly FlameRenderer _flames;
    private Texture2D _pointTex;
    private Texture2D _extraBallTex;
    private Texture2D _lifeParticleTex;
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
    public List<PuzzleBall> PuzzleBalls { get; } = [];
    public List<WindFan> WindFans { get; } = [];
    private float _scoreDecayAccum;
    private float _windParticleAccum;

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
        _extraBallTex = Billboard.LoadCircularSprite(Path.Combine(_gameRoot, "Textures", "ExtraBall.bmp"));
        _lifeParticleTex = Billboard.LoadMasked(Path.Combine(_gameRoot, "Textures", "P_Extra_Life_Particle.bmp"), keepColor: true);
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
        _lightningSphere = Make("Ball_LightningSphere");

        // The ball never receives its own blob shadow (z=0); specular matches Balls.nmo finish:
        // wood = soft satin, stone = matte rough rock, paper = flat matte.
        _ballWood.SetSurface(new Vector4(0.16f, 18f, 0f, 0f));
        _ballStone.SetSurface(new Vector4(0.08f, 10f, 0f, 0f));
        _ballPaper.SetSurface(new Vector4(0.04f, 6f, 0f, 0f));

        // Load the 3 animated lightning textures for Ball_LightningSphere
        _lightningFrames[0] = _textureManager.GetTexture("Ball_LightningSphere1.bmp");
        _lightningFrames[1] = _textureManager.GetTexture("Ball_LightningSphere2.bmp");
        _lightningFrames[2] = _textureManager.GetTexture("Ball_LightningSphere3.bmp");

        // Load AnimTrafo.nmo (rising chrome pillars, 4 ring quadrants, electric flashfield)
        string trafoPath = Path.Combine(_gameRoot, "3D Entities", "AnimTrafo.nmo");
        if (File.Exists(trafoPath))
        {
            _animTrafo = new Prefab(NmoLevel.Load(trafoPath), _textureManager);
        }
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

        // Add walkable/solid prefab modules (bridges, seesaws, ramps, gates, dome, goal platform)
        // to the static triangle collider so the ball never falls through P_Modul_* structures.
        foreach (var (entity, group) in CurrentLevel.Placeholders)
        {
            bool isWalkableModule = group.StartsWith("P_Modul_", StringComparison.OrdinalIgnoreCase)
                || group.Equals("P_Dome", StringComparison.OrdinalIgnoreCase)
                || group.Equals("PE_Levelende", StringComparison.OrdinalIgnoreCase);
            if (!isWalkableModule || group.Equals("P_Modul_18", StringComparison.OrdinalIgnoreCase)) continue;

            var prefab = Prefabs.Get(PrefabKey(entity.Name));
            if (prefab == null) continue;
            foreach (var part in prefab.Parts)
            {
                if (part.Mesh == null) continue;
                string pn = part.Name;
                if (pn.Contains("Shadow", StringComparison.OrdinalIgnoreCase) ||
                    pn.Contains("Flash", StringComparison.OrdinalIgnoreCase) ||
                    pn.Contains("Ballon", StringComparison.OrdinalIgnoreCase) ||
                    pn.Contains("Seil", StringComparison.OrdinalIgnoreCase) ||
                    pn.Contains("Rope", StringComparison.OrdinalIgnoreCase))
                    continue;
                Collider.AddMesh(part.Mesh, part.WorldMatrix * entity.WorldMatrix);
            }
            if (entity.Position.Y < minY) minY = entity.Position.Y;
        }

        _scoreDecayAccum = 0f;
        _windParticleAccum = 0f;

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

        // Reset player and any in-progress transformer animation
        IsTransforming = false;
        _activeTrafo = null;
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
        PuzzleBalls.Clear();
        WindFans.Clear();

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

        // 6. Puzzle Balls (P_Ball_Paper, P_Ball_Wood, P_Ball_Stone)
        foreach (var pb in CurrentLevel.PuzzleBallEntities)
        {
            BallMaterial mat = pb.Name.Contains("Paper", StringComparison.OrdinalIgnoreCase)
                ? BallMaterial.Paper
                : pb.Name.Contains("Stone", StringComparison.OrdinalIgnoreCase)
                    ? BallMaterial.Stone
                    : BallMaterial.Wood;
            PuzzleBalls.Add(new PuzzleBall(mat, pb.Position, pb));
        }

        // 7. Wind Fans (P_Modul_18 — 113 instances across Levels 2-12)
        if (CurrentLevel.Groups.TryGetValue("P_Modul_18", out var fans))
        {
            foreach (var fan in fans)
                WindFans.Add(new WindFan(fan.Position, fan));
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
        // Score decay: 1 point per second using a float accumulator (fixes 60fps integer truncation to 0)
        if (Score > 0 && !IsLevelComplete && !IsTransforming)
        {
            _scoreDecayAccum += dt;
            while (_scoreDecayAccum >= 1.0f && Score > 0)
            {
                _scoreDecayAccum -= 1.0f;
                Score--;
            }
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

        // Update active transformer animation sequence
        if (IsTransforming && _activeTrafo != null)
        {
            UpdateTransformAnimation(dt);
            return;
        }

        // Update Transformers
        foreach (var t in Transformers)
        {
            t.Update(dt);
            if (t.Check(Player.Position, Player.CurrentMaterial))
            {
                StartTransformAnimation(t);
                break;
            }
        }

        // Step all boxes with substepped OBB-vs-world and box-vs-box resolution,
        // then step puzzle balls (P_Ball_Paper/Wood/Stone) and re-separate the player ball.
        MovableBox.StepAll(Boxes, dt, Collider);
        PuzzleBall.StepAll(PuzzleBalls, Player, Boxes, dt, Collider);
        Player.ResolvePostBoxStep(Boxes, Collider, dt);

        // Update Wind Fans (P_Modul_18): spin rotors & lift Paper Ball in the updraft
        if (WindFans.Count > 0)
        {
            _windParticleAccum += dt;
            bool emitWind = _windParticleAccum >= 0.14f;
            if (emitWind) _windParticleAccum = 0f;

            foreach (var fan in WindFans)
            {
                fan.Update(dt, Player, PuzzleBalls);
                if (emitWind && Vector3.DistanceSquared(fan.Position, Player.Position) < 90f * 90f)
                {
                    _particles.EmitBurst(
                        fan.Position + fan.UpDirection * 1.2f,
                        new Color((byte)215, (byte)235, (byte)255, (byte)130),
                        1, 2.0f, 0.22f, 0.9f);
                }
            }
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

    public void DisposeEffects()
    {
        _flames.Dispose();
        _lightningSphere?.Dispose();
        _animTrafo?.Dispose();
        if (_pointTex.Id != 0) Raylib.UnloadTexture(_pointTex);
        if (_extraBallTex.Id != 0) Raylib.UnloadTexture(_extraBallTex);
        if (_lifeParticleTex.Id != 0) Raylib.UnloadTexture(_lifeParticleTex);
    }

    private void StartTransformAnimation(TransformerTrigger trafo)
    {
        IsTransforming = true;
        _activeTrafo = trafo;
        _trafoTimer = 0f;
        _trafoStartPos = Player.Position;
        _trafoSwapped = false;
        _trafoSparkAccum = 0f;
        trafo.Cooldown = 3.5f;

        Player.Velocity = Vector3.Zero;
        Player.AngularVelocity = Vector3.Zero;
        _audioManager.PlayTransformer();

        Color glow = trafo.TargetMaterial switch
        {
            BallMaterial.Stone => new Color((byte)110, (byte)235, (byte)170, (byte)255),
            BallMaterial.Paper => new Color((byte)110, (byte)185, (byte)255, (byte)255),
            _ => new Color((byte)255, (byte)175, (byte)70, (byte)255)
        };
        _particles.EmitBurst(trafo.Position + new Vector3(0f, 1.5f, 0f), glow, 24, 9.0f, 0.4f, 0.8f);
    }

    private void UpdateTransformAnimation(float dt)
    {
        if (_activeTrafo == null)
        {
            IsTransforming = false;
            return;
        }

        _trafoTimer += dt;
        float u = Math.Clamp(_trafoTimer / TrafoDuration, 0f, 1f);
        Vector3 anchor = _activeTrafo.Position;

        // Smooth lift profile: rise (0..0.33), hover & morph (0.33..0.72), settle back (0.72..1.0)
        float lift;
        if (u < 0.33f)
        {
            float t = u / 0.33f;
            lift = t * t * (3f - 2f * t);
        }
        else if (u < 0.72f)
        {
            lift = 1f + MathF.Sin((u - 0.33f) * MathF.PI * 6f) * 0.05f;
        }
        else
        {
            float t = (u - 0.72f) / 0.28f;
            lift = 1f - t * t * (3f - 2f * t);
        }

        // Pull ball smoothly to pad center while levitating
        float centerT = Math.Clamp(u / 0.28f, 0f, 1f);
        float targetX = _trafoStartPos.X + (anchor.X - _trafoStartPos.X) * centerT;
        float targetZ = _trafoStartPos.Z + (anchor.Z - _trafoStartPos.Z) * centerT;
        float targetY = anchor.Y + Player.Properties.Radius + lift * 3.0f;
        Player.Position = new Vector3(targetX, targetY, targetZ);

        // Energized spin during transformation
        float spinRate = MathF.Sin(u * MathF.PI) * 9.0f;
        if (spinRate > 0.01f)
        {
            var delta = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f)), spinRate * dt);
            Player.Rotation = Quaternion.Normalize(delta * Player.Rotation);
        }

        // Emit crackling electric sparks during the peak energy window
        if (u > 0.20f && u < 0.78f)
        {
            _trafoSparkAccum += dt;
            if (_trafoSparkAccum >= 0.055f)
            {
                _trafoSparkAccum = 0f;
                _particles.EmitBurst(Player.Position, new Color((byte)150, (byte)205, (byte)255, (byte)255), 5, 7.5f, 0.28f, 0.55f);
            }
        }

        // Swap ball material at peak lightning flash (u = 0.52)
        if (!_trafoSwapped && u >= 0.52f)
        {
            _trafoSwapped = true;
            Player.SetMaterial(_activeTrafo.TargetMaterial);
            Color glow = _activeTrafo.TargetMaterial switch
            {
                BallMaterial.Stone => new Color((byte)110, (byte)235, (byte)170, (byte)255),
                BallMaterial.Paper => new Color((byte)110, (byte)185, (byte)255, (byte)255),
                _ => new Color((byte)255, (byte)175, (byte)70, (byte)255)
            };
            _particles.EmitBurst(Player.Position, glow, 42, 13.0f, 0.45f, 1.0f);
            _hud.ShowBanner($"TRANSFORMED: {_activeTrafo.TargetMaterial.ToString().ToUpper()} BALL", glow, 2.0f);
        }

        if (_trafoTimer >= TrafoDuration)
        {
            IsTransforming = false;
            _activeTrafo = null;
            Player.Velocity = Vector3.Zero;
            Player.AngularVelocity = Vector3.Zero;
        }
    }

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

            bool dynamic = group is "P_Extra_Point" or "P_Extra_Life" or "P_Box"
                or "P_Ball_Wood" or "P_Ball_Stone" or "P_Ball_Paper" or "P_Modul_18";
            var prefab = Prefabs.Get(PrefabKey(entity.Name));
            // Level copies of empty prefabs are untextured editor markers (reset arrows, flame volumes)
            if (prefab == null || prefab.Parts.Count == 0) continue;

            if (!dynamic)
            {
                // When this transformer is actively animating, AnimTrafo.nmo replaces its static ring
                if (IsTransforming && _activeTrafo?.Entity == entity)
                {
                    foreach (var part in prefab.Parts)
                        if (part.Name.Contains("Shadow", StringComparison.OrdinalIgnoreCase))
                            prefab.Renderer.DrawMesh(part.Mesh!, part.WorldMatrix * entity.WorldMatrix);
                }
                else
                {
                    prefab.Draw(entity.WorldMatrix);
                }
            }
        }

        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null) continue;

            string key = pickup.Type == PickupType.Point ? "P_Extra_Point" : "P_Extra_Life";
            var prefab = Prefabs.Get(key);
            if (prefab == null) continue;

            float spin = pickup.SpinAngle * (MathF.PI / 180f);
            foreach (var part in prefab.Parts)
            {
                bool isFloorDecal = part.Name.Contains("Shadow", StringComparison.OrdinalIgnoreCase)
                    || part.Name.Contains("Floor", StringComparison.OrdinalIgnoreCase);

                Matrix4x4 world = pickup.Entity.WorldMatrix;
                if (isFloorDecal)
                {
                    // Floor halo stays anchored on the ground while pulsing gently
                    world.Translation = pickup.BasePosition;
                }
                else
                {
                    // Outer iridescent oil bubble swirls smoothly around the inner silver ball
                    world = Matrix4x4.CreateFromYawPitchRoll(spin, spin * 0.65f, spin * 0.35f) * world;
                    world.Translation = pickup.CurrentPosition;
                }

                prefab.Renderer.DrawMesh(part.Mesh!, part.WorldMatrix * world);
            }
        }

        foreach (var box in Boxes)
        {
            if (box.Entity == null) continue;
            Matrix4x4 world = box.Entity.WorldMatrix;
            world.Translation = box.Position;
            Prefabs.Get("P_Box")?.Draw(world);
        }

        foreach (var pb in PuzzleBalls)
        {
            Matrix4x4 world = Matrix4x4.CreateFromQuaternion(pb.Rotation) *
                              Matrix4x4.CreateTranslation(pb.Position);
            Prefabs.Get(pb.PrefabKey)?.Draw(world);
        }

        // Spin P_Modul_18 fan rotors
        var fanPrefab = Prefabs.Get("P_Modul_18");
        if (fanPrefab != null)
        {
            foreach (var fan in WindFans)
            {
                if (fan.Entity == null) continue;
                float rad = fan.SpinAngle * (MathF.PI / 180f);
                Matrix4x4 world = Matrix4x4.CreateRotationY(rad) * fan.Entity.WorldMatrix;
                fanPrefab.Draw(world);
            }
        }

        DrawPlayerBall();
        DrawTransformAnimation(time);
        DrawPickupCores(camera);
        RenderQueue.FlushTransparent();
        DrawQueuedFlames(camera, time);
    }

    /// <summary>
    /// Renders the original AnimTrafo.nmo rising pillars, spinning ring quadrants,
    /// electric flashfield, and 3-frame animated Ball_LightningSphere during transformation.
    /// </summary>
    private unsafe void DrawTransformAnimation(float time)
    {
        if (!IsTransforming || _activeTrafo?.Entity == null) return;

        float u = Math.Clamp(_trafoTimer / TrafoDuration, 0f, 1f);
        Matrix4x4 baseWorld = _activeTrafo.Entity.WorldMatrix;

        // Rig lift height: rises out of the pad (0..4.6), holds, then retracts
        float rigLift;
        if (u < 0.30f)
        {
            float t = u / 0.30f;
            rigLift = t * t * (3f - 2f * t) * 4.6f;
        }
        else if (u < 0.72f)
        {
            rigLift = 4.6f;
        }
        else
        {
            float t = (u - 0.72f) / 0.28f;
            rigLift = (1f - t * t * (3f - 2f * t)) * 4.6f;
        }

        // 1. AnimTrafo.nmo parts (bars, 4 spinning ring quadrants, electric flashfield)
        if (_animTrafo != null)
        {
            float ringSpin = u * MathF.PI * 3.0f;
            float ringExpand = MathF.Sin(Math.Clamp((u - 0.15f) / 0.70f, 0f, 1f) * MathF.PI) * 0.45f;
            bool showFlashField = u > 0.18f && u < 0.78f;

            foreach (var part in _animTrafo.Parts)
            {
                if (part.Mesh == null) continue;
                string pname = part.Name;

                if (pname.Contains("Flashfield", StringComparison.OrdinalIgnoreCase))
                {
                    if (!showFlashField) continue;
                    // Pulsing electric plasma field sweeping across the ring
                    float flashY = rigLift * (0.45f + 0.55f * MathF.Sin(time * 24f) * 0.5f + 0.275f);
                    Matrix4x4 fm = Matrix4x4.CreateRotationY(time * 10f)
                        * Matrix4x4.CreateTranslation(0f, flashY, 0f)
                        * baseWorld;
                    _animTrafo.Renderer.DrawMesh(part.Mesh, part.WorldMatrix * fm);
                }
                else if (pname.Contains("Bars", StringComparison.OrdinalIgnoreCase))
                {
                    Matrix4x4 bm = Matrix4x4.CreateTranslation(0f, rigLift, 0f) * baseWorld;
                    _animTrafo.Renderer.DrawMesh(part.Mesh, part.WorldMatrix * bm);
                }
                else if (pname.Contains("Ringpart", StringComparison.OrdinalIgnoreCase))
                {
                    float scale = 1f + ringExpand * 0.18f;
                    Matrix4x4 rm = Matrix4x4.CreateScale(scale, 1f, scale)
                        * Matrix4x4.CreateRotationY(ringSpin)
                        * Matrix4x4.CreateTranslation(0f, rigLift, 0f)
                        * baseWorld;
                    _animTrafo.Renderer.DrawMesh(part.Mesh, part.WorldMatrix * rm);
                }
            }
        }

        // 2. Ball_LightningSphere cycling Ball_LightningSphere1/2/3.bmp around the levitating ball
        if (_lightningSphere != null && u > 0.24f && u < 0.80f)
        {
            float env = MathF.Sin(((u - 0.24f) / 0.56f) * MathF.PI);
            float scale = 0.96f + env * 0.22f;
            int frameIdx = ((int)(time * 18f)) % 3;
            Texture2D frameTex = _lightningFrames[frameIdx];

            foreach (var part in _lightningSphere.Parts)
            {
                if (part.Mesh == null) continue;
                var rm = _lightningSphere.Renderer.GetOrCreateRenderMesh(part.Mesh);
                foreach (var sub in rm.Submeshes)
                {
                    sub.Alpha = true;
                    sub.Additive = true;
                    sub.Surface = new Vector4(0f, 1f, 0f, -1f);
                    if (frameTex.Id != 0)
                        Raylib.SetMaterialTexture(ref sub.Material, MaterialMapIndex.Albedo, frameTex);
                    byte a = (byte)(Math.Clamp(env * 1.15f, 0f, 1f) * 255f);
                    sub.Material.Maps[(int)MaterialMapIndex.Albedo].Color = new Color((byte)180, (byte)215, (byte)255, a);
                }

                Matrix4x4 lm = Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromYawPitchRoll(time * 8.5f, time * 5.2f, time * 3.7f)
                    * Matrix4x4.CreateTranslation(Player.Position);
                rm.Draw(lm);
            }
        }
    }

    /// <summary>
    /// Draws the chrome silver ball core (CKSprite3D ExtraBall.bmp) inside Extra Life and Extra Point
    /// pickups BEFORE the transparent pass so the additive iridescent oil bubble swirls over it.
    /// </summary>
    private void DrawPickupCores(Camera3D camera)
    {
        if (_extraBallTex.Id == 0) return;
        Rlgl.DrawRenderBatchActive();
        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null) continue;
            float ballSize = pickup.Type == PickupType.Life ? 3.2f : 2.3f;
            Vector3 center = pickup.Type == PickupType.Life
                ? pickup.CurrentPosition
                : pickup.CurrentPosition + new Vector3(0f, 1.3f, 0f);
            Billboard.DrawCentered(_extraBallTex, center, camera, ballSize, Color.White);
        }
        Rlgl.DrawRenderBatchActive();
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

        // Additive energy & oil-shimmer particles around Extra Point and Extra Life pickups
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);

        foreach (var pickup in Pickups)
        {
            if (pickup.IsCollected || pickup.Entity == null) continue;
            float spin = pickup.SpinAngle * (MathF.PI / 180f);

            if (pickup.Type == PickupType.Point)
            {
                for (int i = 0; i < 3; i++)
                {
                    float t = spin + i * MathF.PI * 2f / 3f;
                    var p = pickup.CurrentPosition + new Vector3(MathF.Cos(t) * 1.1f, 1.4f + MathF.Sin(time * 3f + i) * 0.25f, MathF.Sin(t) * 1.1f);
                    Billboard.DrawCentered(_pointTex, p, camera, 1.7f, new Color((byte)140, (byte)215, (byte)255, (byte)225));
                }
            }
            else
            {
                // Iridescent shimmering particles orbiting & rising around the Extra Life bubble
                for (int i = 0; i < 4; i++)
                {
                    float phase = (time * 0.65f + i * 0.25f) % 1.0f;
                    float angle = spin * 0.8f + i * MathF.PI * 0.5f;
                    float orbit = 1.6f + MathF.Sin(phase * MathF.PI) * 0.8f;
                    float py = -0.6f + phase * 2.6f;
                    byte alpha = (byte)(MathF.Sin(phase * MathF.PI) * 210f);
                    var p = pickup.CurrentPosition + new Vector3(MathF.Cos(angle) * orbit, py, MathF.Sin(angle) * orbit);
                    Billboard.DrawCentered(_lifeParticleTex, p, camera, 1.5f + phase * 0.6f, new Color((byte)230, (byte)210, (byte)255, alpha));
                }
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
