namespace BallanceRevival.Nmo;

public class NmoTexture
{
    public string Name { get; set; } = string.Empty;
    public int ObjectIndex { get; set; }
    public string FileName { get; set; } = string.Empty;

    public static NmoTexture? FromObject(NmoObject obj)
    {
        if (obj.ClassId != 31) return null;

        var tex = new NmoTexture
        {
            Name = obj.Name,
            ObjectIndex = obj.Index,
            FileName = obj.Name + ".bmp"
        };

        if (obj.Chunk != null && obj.Chunk.Seek(0x10000))
        {
            uint n = obj.Chunk.ReadUInt32();
            if (n > 0)
            {
                string fn = obj.Chunk.ReadString();
                if (!string.IsNullOrEmpty(fn))
                {
                    tex.FileName = fn;
                }
            }
        }

        return tex;
    }
}

public class NmoMaterial
{
    public string Name { get; set; } = string.Empty;
    public int ObjectIndex { get; set; }
    public uint DiffuseColor { get; set; } = 0xFFFFFFFF; // ARGB
    public uint TextureObjectIndex { get; set; } = uint.MaxValue;
    public uint Options { get; set; }

    // Bit 3 of the Virtools material option word selects alpha blending (shadows, fades, glass)
    public bool AlphaBlend => (Options & 0x8) != 0;

    public bool Additive =>
        AlphaBlend && Name.Contains("Verlauf", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Flash", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Glow", StringComparison.OrdinalIgnoreCase);

    public static NmoMaterial? FromObject(NmoObject obj)
    {
        if (obj.ClassId != 30) return null;

        var mat = new NmoMaterial
        {
            Name = obj.Name,
            ObjectIndex = obj.Index
        };

        if (obj.Chunk != null && obj.Chunk.Seek(0x1000))
        {
            mat.DiffuseColor = obj.Chunk.ReadUInt32(); // Diffuse ARGB
            obj.Chunk.ReadUInt32(); // Ambient
            obj.Chunk.ReadUInt32(); // Specular
            obj.Chunk.ReadUInt32(); // Emissive
            obj.Chunk.ReadUInt32(); // Shininess float
            mat.TextureObjectIndex = obj.Chunk.ReadUInt32();
            obj.Chunk.ReadUInt32();
            obj.Chunk.ReadUInt32();
            mat.Options = obj.Chunk.ReadUInt32();
        }

        return mat;
    }
}
