using System.Text;

namespace BallanceRevival.Nmo;

public class NmoChunk
{
    public byte DataVer { get; }
    public byte Cls { get; }
    public byte ChunkVer { get; }
    public byte Opt { get; }
    public uint[] Data { get; }
    public byte[] RawBytes { get; }
    public List<uint> Ids { get; } = [];
    public List<(uint Ident, int Start, int End)> Subchunks { get; } = [];
    public int Pos { get; set; }

    public NmoChunk(byte[] buffer)
    {
        DataVer = buffer[0];
        Cls = buffer[1];
        ChunkVer = buffer[2];
        Opt = buffer[3];

        uint dw = BitConverter.ToUInt32(buffer, 4);
        Data = new uint[dw];
        Buffer.BlockCopy(buffer, 8, Data, 0, (int)dw * 4);
        RawBytes = new byte[dw * 4];
        Buffer.BlockCopy(buffer, 8, RawBytes, 0, (int)dw * 4);

        int p = 8 + (int)dw * 4;
        if ((Opt & 1) != 0 && p + 4 <= buffer.Length)
        {
            uint n = BitConverter.ToUInt32(buffer, p);
            p += 4;
            for (int i = 0; i < n && p + 4 <= buffer.Length; i++, p += 4)
            {
                Ids.Add(BitConverter.ToUInt32(buffer, p));
            }
        }

        ParseIdents();
    }

    private void ParseIdents()
    {
        if (Data.Length < 2) return;
        int pos = 0;
        while (pos + 1 < Data.Length)
        {
            uint ident = Data[pos];
            uint nxt = Data[pos + 1];
            int end = nxt != 0 ? (int)nxt : Data.Length;
            if (end > Data.Length) end = Data.Length;

            Subchunks.Add((ident, pos + 2, end));
            if (nxt == 0 || nxt + 1 >= Data.Length) break;
            pos = (int)nxt;
        }
    }

    public bool Seek(uint ident)
    {
        foreach (var (id, start, _) in Subchunks)
        {
            if (id == ident)
            {
                Pos = start;
                return true;
            }
        }
        return false;
    }

    public uint ReadUInt32()
    {
        if (Pos >= Data.Length) return 0;
        return Data[Pos++];
    }

    public float ReadFloat()
    {
        if (Pos >= Data.Length) return 0f;
        uint val = Data[Pos++];
        return BitConverter.UInt32BitsToSingle(val);
    }

    public byte[] ReadBytes(int count)
    {
        int bytePos = Pos * 4;
        if (bytePos + count > RawBytes.Length)
        {
            count = Math.Max(0, RawBytes.Length - bytePos);
        }
        byte[] result = new byte[count];
        Buffer.BlockCopy(RawBytes, bytePos, result, 0, count);
        Pos += (count + 3) / 4;
        return result;
    }

    public string ReadString()
    {
        uint len = ReadUInt32();
        if (len == 0) return string.Empty;
        byte[] bytes = ReadBytes((int)len);
        int realLen = 0;
        while (realLen < bytes.Length && bytes[realLen] != 0) realLen++;
        return Encoding.Latin1.GetString(bytes, 0, realLen);
    }
}
