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
    public uint SpecularColor { get; set; } = 0xFF000000; // ARGB
    public uint EmissiveColor { get; set; } = 0xFF000000; // ARGB
    public float Power { get; set; } = 0f; // Virtools specular shininess exponent
    public uint TextureObjectIndex { get; set; } = uint.MaxValue;
    public uint BlendFactors { get; set; } // w[7]: Virtools packed SrcBlend/DstBlend nibbles
    public uint Options { get; set; }

    // Bit 3 of the Virtools material option word selects alpha blending (shadows, fades, glass)
    public bool AlphaBlend => (Options & 0x8) != 0;

    // Virtools w[7] packed blend nibbles: bits 12..15 = SrcBlend, bits 16..19 = DstBlend.
    // Standard alpha blend is 0x13265224 (Src=5 SRCALPHA, Dst=6 INVSRCALPHA);
    // additive glow/halo is 0x13222222 (Src=2 ONE, Dst=2 ONE).
    private uint DstBlend => (BlendFactors >> 16) & 0xF;

    public bool Additive =>
        (AlphaBlend && (DstBlend == 2 || Name.Contains("Verlauf", StringComparison.OrdinalIgnoreCase)))
        || Name.Contains("Flash", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Glow", StringComparison.OrdinalIgnoreCase)
        || Name.Equals("P_Extra_Life_Sphere", StringComparison.OrdinalIgnoreCase);

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
            mat.SpecularColor = obj.Chunk.ReadUInt32(); // Specular ARGB
            mat.EmissiveColor = obj.Chunk.ReadUInt32(); // Emissive ARGB
            mat.Power = obj.Chunk.ReadFloat(); // Shininess exponent
            mat.TextureObjectIndex = obj.Chunk.ReadUInt32();
            obj.Chunk.ReadUInt32();
            mat.BlendFactors = obj.Chunk.ReadUInt32();
            mat.Options = obj.Chunk.ReadUInt32();
        }

        return mat;
    }
}
