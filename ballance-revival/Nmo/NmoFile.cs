using System.IO.Compression;
using System.Text;

namespace BallanceRevival.Nmo;

public class NmoFile
{
    public List<NmoObject> Objects { get; } = [];
    public Dictionary<int, NmoObject> ByIndex { get; } = [];
    public Dictionary<string, NmoObject> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static NmoFile Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    public static NmoFile Parse(byte[] buffer)
    {
        var nmo = new NmoFile();

        // Header: 64 bytes
        // 0..7: sig
        // 8..11: crc, 12..15: ckver, 16..19: fver, 20..23: fver2, 24..27: mode
        // 28..31: h1pack
        // 32..35: dpack, 36..39: dunpack, 40..43: mgrc, 44..47: objc, 48..51: maxid, 52..55: pver, 56..59: pbuild, 60..63: h1unpack

        uint h1Pack = BitConverter.ToUInt32(buffer, 28);
        uint dPack = BitConverter.ToUInt32(buffer, 32);
        uint dUnpack = BitConverter.ToUInt32(buffer, 36);
        uint mgrc = BitConverter.ToUInt32(buffer, 40);
        uint objc = BitConverter.ToUInt32(buffer, 44);
        uint h1Unpack = BitConverter.ToUInt32(buffer, 60);

        int p = 64;
        byte[] h1Raw = new byte[h1Pack];
        Buffer.BlockCopy(buffer, p, h1Raw, 0, (int)h1Pack);
        p += (int)h1Pack;

        byte[] h1 = h1Pack != h1Unpack ? DecompressZLib(h1Raw, (int)h1Unpack) : h1Raw;

        byte[] datRaw = new byte[dPack];
        Buffer.BlockCopy(buffer, p, datRaw, 0, (int)dPack);
        byte[] dat = dPack != dUnpack ? DecompressZLib(datRaw, (int)dUnpack) : datRaw;

        // Parse object descriptors from h1
        int q = 0;
        for (int i = 0; i < objc; i++)
        {
            uint oid = BitConverter.ToUInt32(h1, q);
            uint cid = BitConverter.ToUInt32(h1, q + 4);
            uint fidx = BitConverter.ToUInt32(h1, q + 8);
            uint nl = BitConverter.ToUInt32(h1, q + 12);
            q += 16;

            string name = Encoding.Latin1.GetString(h1, q, (int)nl);
            q += (int)nl;

            var obj = new NmoObject
            {
                Id = oid,
                ClassId = cid,
                FileIndex = fidx,
                Name = name,
                Index = i
            };

            nmo.Objects.Add(obj);
            nmo.ByIndex[i] = obj;
            nmo.ByName[name] = obj;
        }

        // Parse manager chunks from dat
        q = 0;
        for (int i = 0; i < mgrc; i++)
        {
            uint sz = BitConverter.ToUInt32(dat, q + 8);
            q += 12 + (int)sz;
        }

        // Parse object chunks from dat
        for (int i = 0; i < objc; i++)
        {
            if (q + 4 > dat.Length) break;
            uint sz = BitConverter.ToUInt32(dat, q);
            q += 4;

            if (sz > 0 && q + sz <= dat.Length)
            {
                byte[] chunkBuf = new byte[sz];
                Buffer.BlockCopy(dat, q, chunkBuf, 0, (int)sz);
                nmo.Objects[i].Chunk = new NmoChunk(chunkBuf);
                q += (int)sz;
            }
        }

        return nmo;
    }

    private static byte[] DecompressZLib(byte[] input, int expectedSize)
    {
        using var mem = new MemoryStream(input);
        using var zlib = new ZLibStream(mem, CompressionMode.Decompress);
        byte[] output = new byte[expectedSize];
        int totalRead = 0;
        while (totalRead < expectedSize)
        {
            int read = zlib.Read(output, totalRead, expectedSize - totalRead);
            if (read <= 0) break;
            totalRead += read;
        }
        return output;
    }
}
