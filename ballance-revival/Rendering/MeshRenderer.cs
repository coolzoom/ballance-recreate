using System.Numerics;
using BallanceRevival.Nmo;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public unsafe class RenderSubmesh : IDisposable
{
    public Mesh Mesh;
    public Material Material;

    public RenderSubmesh(Mesh mesh, Material material)
    {
        Mesh = mesh;
        Material = material;
    }

    public bool Alpha;
    public bool Additive;
    public Vector3 Surface = LitShader.DefaultSurface;

    /// <summary>Draws without touching blend or depth state; the caller owns that.</summary>
    public void DrawRaw(Matrix4x4 transform)
    {
        LitShader.SetSurface(Surface);
        // Raylib-cs expects translation in M14 (column-vector layout), System.Numerics stores it in M41
        Raylib.DrawMesh(Mesh, Material, Matrix4x4.Transpose(transform));
    }

    /// <summary>Immediate blended draw, used when no frame queue is active.</summary>
    public void DrawBlended(Matrix4x4 transform)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        if (Additive) Raylib.BeginBlendMode(BlendMode.Additive);
        DrawRaw(transform);
        if (Additive) Raylib.EndBlendMode();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    public void Dispose()
    {
        Raylib.UnloadMesh(Mesh);
        // UnloadMaterial would also unload the shared shader and textures; only free the map array
        Raylib.MemFree(Material.Maps);
    }
}

public class RenderMesh : IDisposable
{
    public List<RenderSubmesh> Submeshes { get; } = [];
    public Vector3 BoundsCenter;
    public float BoundsRadius;

    public void Draw(Matrix4x4 transform)
    {
        if (Submeshes.Count == 0) return;

        Vector3 center = Vector3.Transform(BoundsCenter, transform);
        float scale = MathF.Sqrt(MathF.Max(
            new Vector3(transform.M11, transform.M12, transform.M13).LengthSquared(),
            MathF.Max(new Vector3(transform.M21, transform.M22, transform.M23).LengthSquared(),
                      new Vector3(transform.M31, transform.M32, transform.M33).LengthSquared())));
        if (!RenderQueue.IsVisible(center, BoundsRadius * scale)) return;

        foreach (var submesh in Submeshes)
        {
            if (submesh.Alpha) RenderQueue.EnqueueTransparent(submesh, transform, center);
            else submesh.DrawRaw(transform);
        }
    }

    public void SetSurface(Vector3 surface)
    {
        foreach (var submesh in Submeshes)
            submesh.Surface = surface;
    }

    public void Dispose()
    {
        foreach (var submesh in Submeshes)
        {
            submesh.Dispose();
        }
        Submeshes.Clear();
    }
}

public unsafe class MeshRenderer : IDisposable
{
    private readonly TextureManager _textureManager;
    private readonly Dictionary<int, RenderMesh> _cache = [];
    private readonly NmoLevel _level;

    public MeshRenderer(TextureManager textureManager, NmoLevel level)
    {
        _textureManager = textureManager;
        _level = level;
    }

    public void DrawEntity(NmoEntity entity)
    {
        if (entity.Mesh == null) return;

        var renderMesh = GetOrCreateRenderMesh(entity.Mesh);
        renderMesh.Draw(entity.WorldMatrix);
    }

    public void DrawMesh(NmoMesh mesh, Matrix4x4 transform)
    {
        var renderMesh = GetOrCreateRenderMesh(mesh);
        renderMesh.Draw(transform);
    }

    public RenderMesh GetOrCreateRenderMesh(NmoMesh nmoMesh)
    {
        if (_cache.TryGetValue(nmoMesh.ObjectIndex, out var cached))
            return cached;

        var renderMesh = CreateRenderMesh(nmoMesh);
        _cache[nmoMesh.ObjectIndex] = renderMesh;
        return renderMesh;
    }

    private RenderMesh CreateRenderMesh(NmoMesh nmoMesh)
    {
        var renderMesh = new RenderMesh();

        int vCount = nmoMesh.Positions.Length;
        if (vCount == 0 || nmoMesh.Faces.Count == 0)
            return renderMesh;

        ComputeBounds(nmoMesh, renderMesh);

        // Each submesh only uploads the vertices its faces reference instead of the whole mesh
        var remap = new int[vCount];
        var used = new List<int>(vCount);

        foreach (var submesh in nmoMesh.Submeshes)
        {
            int triCount = submesh.Indices.Count / 3;
            if (triCount == 0) continue;

            Array.Fill(remap, -1);
            used.Clear();
            foreach (ushort idx in submesh.Indices)
            {
                if (idx >= vCount || remap[idx] >= 0) continue;
                remap[idx] = used.Count;
                used.Add(idx);
            }
            int localCount = used.Count;

            Mesh mesh = new Mesh
            {
                VertexCount = localCount,
                TriangleCount = triCount,
                Vertices = (float*)Raylib.MemAlloc((uint)(localCount * 3 * sizeof(float))),
                Normals = (float*)Raylib.MemAlloc((uint)(localCount * 3 * sizeof(float))),
                TexCoords = (float*)Raylib.MemAlloc((uint)(localCount * 2 * sizeof(float))),
                Indices = (ushort*)Raylib.MemAlloc((uint)(triCount * 3 * sizeof(ushort)))
            };

            for (int j = 0; j < localCount; j++)
            {
                int i = used[j];
                Vector3 pos = nmoMesh.Positions[i];
                Vector3 nrm = nmoMesh.Normals.Length > i ? nmoMesh.Normals[i] : Vector3.UnitY;
                Vector2 uv = nmoMesh.TexCoords.Length > i ? nmoMesh.TexCoords[i] : Vector2.Zero;

                mesh.Vertices[j * 3 + 0] = pos.X;
                mesh.Vertices[j * 3 + 1] = pos.Y;
                mesh.Vertices[j * 3 + 2] = pos.Z;
                mesh.Normals[j * 3 + 0] = nrm.X;
                mesh.Normals[j * 3 + 1] = nrm.Y;
                mesh.Normals[j * 3 + 2] = nrm.Z;
                mesh.TexCoords[j * 2 + 0] = uv.X;
                mesh.TexCoords[j * 2 + 1] = uv.Y;
            }

            for (int i = 0; i < submesh.Indices.Count; i++)
            {
                int idx = submesh.Indices[i];
                mesh.Indices[i] = (ushort)(idx < vCount ? remap[idx] : 0);
            }

            Raylib.UploadMesh(ref mesh, false);

            Material mat = Raylib.LoadMaterialDefault();
            if (LitShader.Shader.Id != 0) mat.Shader = LitShader.Shader;

            // Resolve material texture and tint
            Color tint = Color.White;
            Texture2D texture = _textureManager.GetTexture(string.Empty);

            bool alpha = false;
            bool additive = false;
            if (_level.Materials.TryGetValue((int)submesh.MaterialObjectIndex, out var nmoMat))
            {
                uint argb = nmoMat.DiffuseColor;
                byte a = (byte)((argb >> 24) & 0xFF);
                byte r = (byte)((argb >> 16) & 0xFF);
                byte g = (byte)((argb >> 8) & 0xFF);
                byte b = (byte)(argb & 0xFF);
                if (a == 0) a = 255;
                alpha = nmoMat.AlphaBlend || a < 250;
                additive = nmoMat.Additive;

                if (_level.Textures.TryGetValue((int)nmoMat.TextureObjectIndex, out var nmoTex))
                {
                    texture = _textureManager.GetTexture(nmoTex.FileName);
                    tint = new Color((byte)255, (byte)255, (byte)255, a);
                }
                else
                {
                    tint = new Color(r, g, b, a);
                }
            }

            Raylib.SetMaterialTexture(ref mat, MaterialMapIndex.Albedo, texture);
            mat.Maps[(int)MaterialMapIndex.Albedo].Color = tint;

            var render = new RenderSubmesh(mesh, mat) { Alpha = alpha, Additive = additive };
            // Blended decals (baked shadows, glows) should neither shine nor be darkened twice
            if (alpha) render.Surface = new Vector3(0f, 1f, additive ? 0f : 1f);
            renderMesh.Submeshes.Add(render);
        }

        return renderMesh;
    }

    private static void ComputeBounds(NmoMesh nmoMesh, RenderMesh renderMesh)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var p in nmoMesh.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Vector3 center = (min + max) * 0.5f;
        float r2 = 0f;
        foreach (var p in nmoMesh.Positions)
            r2 = MathF.Max(r2, Vector3.DistanceSquared(p, center));
        renderMesh.BoundsCenter = center;
        renderMesh.BoundsRadius = MathF.Sqrt(r2) + 0.01f;
    }

    public void Dispose()
    {
        foreach (var rm in _cache.Values)
        {
            rm.Dispose();
        }
        _cache.Clear();
    }
}
