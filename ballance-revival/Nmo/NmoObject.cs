namespace BallanceRevival.Nmo;

public class NmoObject
{
    public uint Id { get; init; }
    public uint ClassId { get; init; }
    public uint FileIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Index { get; init; }
    public NmoChunk? Chunk { get; set; }

    public override string ToString() => $"[{Index}] {Name} (ID=0x{Id:X}, CID={ClassId})";
}
