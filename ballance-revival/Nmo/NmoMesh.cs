using System.Numerics;

namespace BallanceRevival.Nmo;

public struct NmoFace
{
    public int V0;
    public int V1;
    public int V2;
    public int MaterialSlot;

    public NmoFace(int v0, int v1, int v2, int mat)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        MaterialSlot = mat;
    }
}

public class NmoSubmesh
{
    public int MaterialSlot { get; set; }
    public uint MaterialObjectIndex { get; set; }
    public List<ushort> Indices { get; } = [];
}

public class NmoMesh
{
    public string Name { get; set; } = string.Empty;
    public int ObjectIndex { get; set; }
    public Vector3[] Positions { get; set; } = [];
    public Vector3[] Normals { get; set; } = [];
    public Vector2[] TexCoords { get; set; } = [];
    public List<NmoFace> Faces { get; } = [];
    public List<uint> MaterialIndices { get; } = [];
    public List<NmoSubmesh> Submeshes { get; } = [];

    public static NmoMesh? FromObject(NmoObject obj)
    {
        if (obj.ClassId != 32 || obj.Chunk == null) return null;

        var mesh = new NmoMesh
        {
            Name = obj.Name,
            ObjectIndex = obj.Index
        };

        var c = obj.Chunk;

        // 1. Materials (0x100000)
        if (c.Seek(0x100000))
        {
            uint matCount = c.ReadUInt32();
            for (int i = 0; i < matCount; i++)
            {
                uint matObjIdx = c.ReadUInt32();
                c.ReadUInt32(); // flags or unused
                mesh.MaterialIndices.Add(matObjIdx);
            }
        }

        // 2. Faces (0x10000)
        if (c.Seek(0x10000))
        {
            uint faceCount = c.ReadUInt32();
            for (int i = 0; i < faceCount; i++)
            {
                uint w1 = c.ReadUInt32();
                uint w2 = c.ReadUInt32();
                int v0 = (int)(w1 & 0xFFFF);
                int v1 = (int)(w1 >> 16);
                int v2 = (int)(w2 & 0xFFFF);
                int mat = (int)(w2 >> 16);
                // Swap V1/V2 so faces are CCW-front in the Z-mirrored (right-handed) space
                mesh.Faces.Add(new NmoFace(v0, v2, v1, mat));
            }
        }

        // 3. Vertices (0x20000)
        if (c.Seek(0x20000))
        {
            uint vertCount = c.ReadUInt32();
            uint flags = c.ReadUInt32();
            uint sz = c.ReadUInt32();

            // Virtools 2.1 vertex buffer (chunk data version >= 9).
            // Layout matches NeMo2 ILoadVertices and libcmo21 CKMesh::Load:
            //   positions, unless bit 0x10
            //   diffuse color: one dword if bit 0x01, otherwise one per vertex
            //   specular color: one dword if bit 0x02, otherwise one per vertex
            //   normals, unless bit 0x04
            //   UVs: one pair if bit 0x08, otherwise one pair per vertex
            // The two color dwords sit in front of the normals. Treating them as
            // texture coordinates smears the stone medallion into chevrons.
            bool skipPositions = (flags & 0x10) != 0;
            bool singleColor = (flags & 0x01) != 0;
            bool singleSpecular = (flags & 0x02) != 0;
            bool hasNormals = (flags & 0x04) == 0;
            bool singleUv = (flags & 0x08) != 0;

            mesh.Positions = new Vector3[vertCount];
            if (!skipPositions)
            {
                for (int i = 0; i < vertCount; i++)
                {
                    // Virtools is left-handed; mirror Z to get right-handed OpenGL space
                    mesh.Positions[i] = new Vector3(c.ReadFloat(), c.ReadFloat(), -c.ReadFloat());
                }
            }

            int colorCount = singleColor ? 1 : (int)vertCount;
            for (int i = 0; i < colorCount; i++)
                c.ReadUInt32();

            int specularCount = singleSpecular ? 1 : (int)vertCount;
            for (int i = 0; i < specularCount; i++)
                c.ReadUInt32();

            if (hasNormals)
            {
                mesh.Normals = new Vector3[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    mesh.Normals[i] = new Vector3(c.ReadFloat(), c.ReadFloat(), -c.ReadFloat());
                }
            }

            mesh.TexCoords = new Vector2[vertCount];
            if (singleUv)
            {
                float u = c.ReadFloat();
                float v = c.ReadFloat();
                if (float.IsNaN(u)) u = 0f;
                if (float.IsNaN(v)) v = 0f;
                var uv = new Vector2(u, v);
                for (int i = 0; i < vertCount; i++)
                    mesh.TexCoords[i] = uv;
            }
            else
            {
                for (int i = 0; i < vertCount; i++)
                {
                    float u = c.ReadFloat();
                    float v = c.ReadFloat();
                    if (float.IsNaN(u)) u = 0f;
                    if (float.IsNaN(v)) v = 0f;
                    mesh.TexCoords[i] = new Vector2(u, v);
                }
            }

            // If no normals, generate smooth normals from face geometry
            if (!hasNormals && mesh.Faces.Count > 0)
            {
                mesh.Normals = new Vector3[vertCount];
                foreach (var face in mesh.Faces)
                {
                    if (face.V0 < vertCount && face.V1 < vertCount && face.V2 < vertCount)
                    {
                        Vector3 p0 = mesh.Positions[face.V0];
                        Vector3 p1 = mesh.Positions[face.V1];
                        Vector3 p2 = mesh.Positions[face.V2];
                        Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                        if (fn.LengthSquared() > 1e-6f)
                        {
                            fn = Vector3.Normalize(fn);
                            mesh.Normals[face.V0] += fn;
                            mesh.Normals[face.V1] += fn;
                            mesh.Normals[face.V2] += fn;
                        }
                    }
                }
                for (int i = 0; i < vertCount; i++)
                {
                    if (mesh.Normals[i].LengthSquared() > 1e-6f)
                        mesh.Normals[i] = Vector3.Normalize(mesh.Normals[i]);
                    else
                        mesh.Normals[i] = Vector3.UnitY;
                }
            }
        }

        // 4. Build submeshes per material slot
        var submeshDict = new Dictionary<int, NmoSubmesh>();
        foreach (var face in mesh.Faces)
        {
            if (!submeshDict.TryGetValue(face.MaterialSlot, out var submesh))
            {
                uint matObjIdx = face.MaterialSlot < mesh.MaterialIndices.Count 
                    ? mesh.MaterialIndices[face.MaterialSlot] 
                    : uint.MaxValue;

                submesh = new NmoSubmesh
                {
                    MaterialSlot = face.MaterialSlot,
                    MaterialObjectIndex = matObjIdx
                };
                submeshDict[face.MaterialSlot] = submesh;
                mesh.Submeshes.Add(submesh);
            }

            submesh.Indices.Add((ushort)face.V0);
            submesh.Indices.Add((ushort)face.V1);
            submesh.Indices.Add((ushort)face.V2);
        }

        return mesh;
    }
}
