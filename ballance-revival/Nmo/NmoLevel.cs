using System.Numerics;

namespace BallanceRevival.Nmo;

public class NmoLevel
{
    public NmoFile RawFile { get; }

    public Dictionary<int, NmoMesh> Meshes { get; } = [];
    public Dictionary<int, NmoMaterial> Materials { get; } = [];
    public Dictionary<int, NmoTexture> Textures { get; } = [];
    public Dictionary<int, NmoEntity> Entities { get; } = [];

    public Dictionary<string, List<NmoEntity>> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<NmoEntity> AllRenderables { get; } = [];
    public List<NmoEntity> FloorColliders { get; } = [];
    public List<NmoEntity> RailColliders { get; } = [];
    public List<NmoEntity> CheckpointEntities { get; } = [];
    public List<NmoEntity> ResetPointEntities { get; } = [];
    public List<NmoEntity> ExtraPointEntities { get; } = [];
    public List<NmoEntity> ExtraLifeEntities { get; } = [];
    public List<NmoEntity> TrafoWoodEntities { get; } = [];
    public List<NmoEntity> TrafoStoneEntities { get; } = [];
    public List<NmoEntity> TrafoPaperEntities { get; } = [];
    public List<NmoEntity> BoxEntities { get; } = [];
    public List<NmoEntity> PuzzleBallEntities { get; } = [];

    /// <summary>Placeholder entities of PH groups (P_*, PS_*, PC_*, PE_*, PR_*) mapped to their group name.</summary>
    public Dictionary<NmoEntity, string> Placeholders { get; } = [];

    public NmoEntity? StartPoint { get; set; }
    public NmoEntity? EndDome { get; set; }
    public NmoEntity? EndBalloon { get; set; }

    public NmoLevel(NmoFile file)
    {
        RawFile = file;
        Build();
    }

    private void Build()
    {
        // 1. Textures (cid 31)
        foreach (var obj in RawFile.Objects)
        {
            if (obj.ClassId == 31)
            {
                var tex = NmoTexture.FromObject(obj);
                if (tex != null) Textures[obj.Index] = tex;
            }
        }

        // 2. Materials (cid 30)
        foreach (var obj in RawFile.Objects)
        {
            if (obj.ClassId == 30)
            {
                var mat = NmoMaterial.FromObject(obj);
                if (mat != null) Materials[obj.Index] = mat;
            }
        }

        // 3. Meshes (cid 32)
        foreach (var obj in RawFile.Objects)
        {
            if (obj.ClassId == 32)
            {
                var mesh = NmoMesh.FromObject(obj);
                if (mesh != null) Meshes[obj.Index] = mesh;
            }
        }

        // 4. Entities (cid 41)
        foreach (var obj in RawFile.Objects)
        {
            if (obj.ClassId == 41)
            {
                var entity = NmoEntity.FromObject(obj);
                if (entity != null)
                {
                    if (entity.MeshObjectIndex != uint.MaxValue && 
                        Meshes.TryGetValue((int)entity.MeshObjectIndex, out var mesh))
                    {
                        entity.Mesh = mesh;
                    }
                    Entities[obj.Index] = entity;
                }
            }
        }

        // 5. Groups (cid 23)
        foreach (var obj in RawFile.Objects)
        {
            if (obj.ClassId == 23 && obj.Chunk != null)
            {
                if (obj.Chunk.Seek(0xFFFFF))
                {
                    uint count = obj.Chunk.ReadUInt32();
                    var list = new List<NmoEntity>();
                    for (int i = 0; i < count; i++)
                    {
                        uint memberIdx = obj.Chunk.ReadUInt32();
                        if (Entities.TryGetValue((int)memberIdx, out var memberEntity))
                        {
                            list.Add(memberEntity);
                        }
                    }
                    Groups[obj.Name] = list;
                }
            }
        }

        foreach (var (groupName, members) in Groups)
        {
            if (!IsPrefabGroup(groupName)) continue;
            foreach (var m in members)
                Placeholders[m] = groupName;
        }

        // 6. Categorize gameplay entities
        CategorizeEntities();
    }

    private void CategorizeEntities()
    {
        // Floors
        if (Groups.TryGetValue("Phys_Floors", out var floors))
            FloorColliders.AddRange(floors);
        if (Groups.TryGetValue("Phys_FloorStopper", out var stoppers))
            FloorColliders.AddRange(stoppers);

        // Rails
        if (Groups.TryGetValue("Phys_FloorRails", out var rails))
            RailColliders.AddRange(rails);

        // Checkpoints & Reset points
        if (Groups.TryGetValue("PC_Checkpoints", out var checkpoints))
            CheckpointEntities.AddRange(checkpoints);
        if (Groups.TryGetValue("PR_Resetpoints", out var resetPoints))
            ResetPointEntities.AddRange(resetPoints);

        // Extra Pickups
        if (Groups.TryGetValue("P_Extra_Point", out var extraPoints))
            ExtraPointEntities.AddRange(extraPoints);
        if (Groups.TryGetValue("P_Extra_Life", out var extraLives))
            ExtraLifeEntities.AddRange(extraLives);

        // Transformers
        if (Groups.TryGetValue("P_Trafo_Wood", out var trafosWood))
            TrafoWoodEntities.AddRange(trafosWood);
        if (Groups.TryGetValue("P_Trafo_Stone", out var trafosStone))
            TrafoStoneEntities.AddRange(trafosStone);
        if (Groups.TryGetValue("P_Trafo_Paper", out var trafosPaper))
            TrafoPaperEntities.AddRange(trafosPaper);

        // Boxes
        if (Groups.TryGetValue("P_Box", out var boxes))
            BoxEntities.AddRange(boxes);

        // Puzzle Balls
        if (Groups.TryGetValue("P_Ball_Wood", out var ballsWood))
            PuzzleBallEntities.AddRange(ballsWood);
        if (Groups.TryGetValue("P_Ball_Stone", out var ballsStone))
            PuzzleBallEntities.AddRange(ballsStone);
        if (Groups.TryGetValue("P_Ball_Paper", out var ballsPaper))
            PuzzleBallEntities.AddRange(ballsPaper);

        // Start & End
        if (Groups.TryGetValue("PS_Levelstart", out var starts) && starts.Count > 0)
            StartPoint = starts[0];
        if (Groups.TryGetValue("P_Dome", out var domes) && domes.Count > 0)
            EndDome = domes[0];
        if (Groups.TryGetValue("PE_Levelende", out var balloons) && balloons.Count > 0)
            EndBalloon = balloons[0];

        var hidden = new HashSet<NmoEntity>();
        foreach (var g in new[] { "DepthTestCubes", "invisible" })
            if (Groups.TryGetValue(g, out var members)) hidden.UnionWith(members);

        // Fallbacks by entity name if groups were incomplete
        foreach (var entity in Entities.Values)
        {
            string name = entity.Name;
            if (hidden.Contains(entity) || name.StartsWith("Quader") ||
                name.StartsWith("DepthTestCube") || name.StartsWith("SkyLayer"))
                continue;

            if (entity.Mesh != null)
            {
                AllRenderables.Add(entity);
            }

            if (FloorColliders.Count == 0 && (name.Contains("Floor") || name.Contains("Modul")))
            {
                FloorColliders.Add(entity);
            }
            if (RailColliders.Count == 0 && name.Contains("Rail"))
            {
                RailColliders.Add(entity);
            }
            if (StartPoint == null && name.StartsWith("PS_FourFlames"))
            {
                StartPoint = entity;
            }
            if (EndDome == null && name.StartsWith("P_Dome"))
            {
                EndDome = entity;
            }
            if (EndBalloon == null && name.StartsWith("PE_Balloon"))
            {
                EndBalloon = entity;
            }
            if (ResetPointEntities.Count == 0 && name.StartsWith("PR_Resetpoint"))
            {
                ResetPointEntities.Add(entity);
            }
            if (CheckpointEntities.Count == 0 && name.StartsWith("PC_TwoFlames"))
            {
                CheckpointEntities.Add(entity);
            }
        }

        // Sort reset points and checkpoints by name (PR_Resetpoint_01, 02, etc.)
        ResetPointEntities.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        CheckpointEntities.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrefabGroup(string name) =>
        name.StartsWith("P_") || name.StartsWith("PS_") || name.StartsWith("PC_") ||
        name.StartsWith("PE_") || name.StartsWith("PR_");

    public static NmoLevel Load(string path)
    {
        var file = NmoFile.Load(path);
        return new NmoLevel(file);
    }
}
