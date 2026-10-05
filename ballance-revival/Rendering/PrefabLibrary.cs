using System.Numerics;
using BallanceRevival.Nmo;

namespace BallanceRevival.Rendering;

/// <summary>
/// A model loaded from its own NMO file (PH prefabs, Balls.nmo). Material and texture
/// indices are local to that file, so each prefab owns its renderer.
/// </summary>
public class Prefab : IDisposable
{
    public NmoLevel Source { get; }
    public MeshRenderer Renderer { get; }
    public List<NmoEntity> Parts { get; } = [];
    /// <summary>Grate centers of the prefab's flames, in prefab space.</summary>
    public List<Vector3> FlamePoints { get; } = [];
    public List<string> FlameNames { get; } = [];

    public Prefab(NmoLevel source, TextureManager textures, Func<NmoEntity, bool>? filter = null)
    {
        Source = source;
        Renderer = new MeshRenderer(textures, source);
        foreach (var e in source.Entities.Values)
        {
            if (e.Mesh != null && (filter == null || filter(e)))
                Parts.Add(e);
        }

        foreach (var obj in source.RawFile.Objects)
        {
            if (obj.ClassId != 33 || obj.Chunk == null) continue;
            // "_Flame_" frames are emitters; "PC_TwoFlames_MF" style frames are the prefab's own origin
            if (!obj.Name.Contains("_Flame_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!obj.Chunk.Seek(0x100000)) continue;
            obj.Chunk.ReadUInt32();
            obj.Chunk.ReadUInt32();
            for (int i = 0; i < 9; i++) obj.Chunk.ReadFloat();
            float x = obj.Chunk.ReadFloat();
            float y = obj.Chunk.ReadFloat();
            float z = -obj.Chunk.ReadFloat();
            // Emitter frames hover about 0.8 above the rim of the flame cup (rim at y 1.25 for small flames)
            FlamePoints.Add(new Vector3(x, y - 0.8f, z));
            FlameNames.Add(obj.Name);
        }
    }

    public void Draw(Matrix4x4 world)
    {
        foreach (var part in Parts)
            Renderer.DrawMesh(part.Mesh!, part.WorldMatrix * world);
    }

    public void SetSurface(Vector3 surface)
    {
        foreach (var part in Parts)
            Renderer.GetOrCreateRenderMesh(part.Mesh!).SetSurface(surface);
    }

    public void Dispose() => Renderer.Dispose();
}

public class PrefabLibrary : IDisposable
{
    private readonly Dictionary<string, Prefab> _prefabs = new(StringComparer.OrdinalIgnoreCase);

    public PrefabLibrary(string phDirectory, TextureManager textures)
    {
        if (!Directory.Exists(phDirectory)) return;

        foreach (var file in Directory.EnumerateFiles(phDirectory, "*.nmo"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var prefab = new Prefab(NmoLevel.Load(file), textures, e => !IsHelperPart(e.Name));
                if (prefab.Parts.Count > 0 || prefab.FlamePoints.Count > 0)
                    _prefabs[name] = prefab;
                else
                    prefab.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Prefab {name} failed to load: {ex.Message}");
            }
        }
    }

    // Collision proxies and the end-of-level UFO are not visible during normal play
    private static bool IsHelperPart(string name) =>
        name.Contains("Kollision", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("PE_UFO", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("PE_Box_slide", StringComparison.OrdinalIgnoreCase);

    public Prefab? Get(string groupName) => _prefabs.GetValueOrDefault(groupName);

    public void Dispose()
    {
        foreach (var p in _prefabs.Values) p.Dispose();
        _prefabs.Clear();
    }
}
