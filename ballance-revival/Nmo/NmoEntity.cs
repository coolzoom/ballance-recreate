using System.Numerics;

namespace BallanceRevival.Nmo;

public class NmoEntity
{
    public string Name { get; set; } = string.Empty;
    public int ObjectIndex { get; set; }
    public uint MeshObjectIndex { get; set; } = uint.MaxValue;
    public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.Identity;
    public Vector3 Position { get; set; }
    public NmoMesh? Mesh { get; set; }

    public static NmoEntity? FromObject(NmoObject obj)
    {
        if (obj.ClassId != 41) return null;

        var entity = new NmoEntity
        {
            Name = obj.Name,
            ObjectIndex = obj.Index
        };

        var c = obj.Chunk;
        if (c != null)
        {
            if (c.Seek(0x4000))
            {
                entity.MeshObjectIndex = c.ReadUInt32();
            }

            if (c.Seek(0x100000))
            {
                c.ReadUInt32(); // flags
                c.ReadUInt32(); // mf

                float m00 = c.ReadFloat(), m01 = c.ReadFloat(), m02 = c.ReadFloat();
                float m10 = c.ReadFloat(), m11 = c.ReadFloat(), m12 = c.ReadFloat();
                float m20 = c.ReadFloat(), m21 = c.ReadFloat(), m22 = c.ReadFloat();
                float m30 = c.ReadFloat(), m31 = c.ReadFloat(), m32 = c.ReadFloat();

                // S * M * S with S = diag(1, 1, -1): same Z mirror as applied to mesh vertices
                entity.WorldMatrix = new Matrix4x4(
                    m00, m01, -m02, 0f,
                    m10, m11, -m12, 0f,
                    -m20, -m21, m22, 0f,
                    m30, m31, -m32, 1f
                );

                entity.Position = new Vector3(m30, m31, -m32);
            }
        }

        return entity;
    }
}
