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

    public void Draw(Matrix4x4 transform)
    {
        if (Alpha)
        {
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthMask();
            Rlgl.DisableBackfaceCulling();
            if (Additive) Raylib.BeginBlendMode(BlendMode.Additive);
        }

        // Raylib-cs expects translation in M14 (column-vector layout), System.Numerics stores it in M41
        Raylib.DrawMesh(Mesh, Material, Matrix4x4.Transpose(transform));

        if (Alpha)
        {
            Rlgl.DrawRenderBatchActive();
            if (Additive) Raylib.EndBlendMode();
            Rlgl.EnableBackfaceCulling();
            Rlgl.EnableDepthMask();
        }
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

        public void Draw(Matrix4x4 transform)
    {
        foreach (var submesh in Submeshes)
        {
            if (!submesh.Alpha) submesh.Draw(transform);
        }
        foreach (var submesh in Submeshes)
        {
            if (submesh.Alpha) submesh.Draw(transform);
        }
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

        foreach (var submesh in nmoMesh.Submeshes)
        {
            int triCount = submesh.Indices.Count / 3;
            if (triCount == 0) continue;

            Mesh mesh = new Mesh
            {
                VertexCount = vCount,
                TriangleCount = triCount,
                Vertices = (float*)Raylib.MemAlloc((uint)(vCount * 3 * sizeof(float))),
                Normals = (float*)Raylib.MemAlloc((uint)(vCount * 3 * sizeof(float))),
                TexCoords = (float*)Raylib.MemAlloc((uint)(vCount * 2 * sizeof(float))),
                Indices = (ushort*)Raylib.MemAlloc((uint)(triCount * 3 * sizeof(ushort)))
            };

            for (int i = 0; i < vCount; i++)
            {
                mesh.Vertices[i * 3 + 0] = nmoMesh.Positions[i].X;
                mesh.Vertices[i * 3 + 1] = nmoMesh.Positions[i].Y;
                mesh.Vertices[i * 3 + 2] = nmoMesh.Positions[i].Z;

                if (nmoMesh.Normals.Length > i)
                {
                    mesh.Normals[i * 3 + 0] = nmoMesh.Normals[i].X;
                    mesh.Normals[i * 3 + 1] = nmoMesh.Normals[i].Y;
                    mesh.Normals[i * 3 + 2] = nmoMesh.Normals[i].Z;
                }
                else
                {
                    mesh.Normals[i * 3 + 0] = 0f;
                    mesh.Normals[i * 3 + 1] = 1f;
                    mesh.Normals[i * 3 + 2] = 0f;
                }

                if (nmoMesh.TexCoords.Length > i)
                {
                    mesh.TexCoords[i * 2 + 0] = nmoMesh.TexCoords[i].X;
                    mesh.TexCoords[i * 2 + 1] = nmoMesh.TexCoords[i].Y;
                }
                else
                {
                    mesh.TexCoords[i * 2 + 0] = 0f;
                    mesh.TexCoords[i * 2 + 1] = 0f;
                }
            }

            for (int i = 0; i < submesh.Indices.Count; i++)
            {
                mesh.Indices[i] = submesh.Indices[i];
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

            renderMesh.Submeshes.Add(new RenderSubmesh(mesh, mat) { Alpha = alpha, Additive = additive });
        }

        return renderMesh;
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
