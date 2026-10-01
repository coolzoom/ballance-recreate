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

    public Prefab(NmoLevel source, TextureManager textures, Func<NmoEntity, bool>? filter = null)
    {
        Source = source;
        Renderer = new MeshRenderer(textures, source);
        foreach (var e in source.Entities.Values)
        {
            if (e.Mesh != null && (filter == null || filter(e)))
                Parts.Add(e);
        }
    }

    public void Draw(Matrix4x4 world)
    {
        foreach (var part in Parts)
            Renderer.DrawMesh(part.Mesh!, part.WorldMatrix * world);
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
                if (prefab.Parts.Count > 0)
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
