using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Decodes the uncompressed BMP/TGA files shipped with Ballance.
/// The bundled raylib native library is built without BMP/TGA support.
/// </summary>
public static unsafe class ImageDecoder
{
    public static bool TryLoad(string path, out Image image)
    {
        image = default;
        byte[] d = File.ReadAllBytes(path);
        byte[]? rgba;
        int w, h;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".bmp") rgba = DecodeBmp(d, out w, out h);
        else if (ext == ".tga") rgba = DecodeTga(d, out w, out h);
        else return false;

        if (rgba == null) return false;

        byte* mem = (byte*)Raylib.MemAlloc((uint)rgba.Length);
        fixed (byte* src = rgba)
        {
            Buffer.MemoryCopy(src, mem, rgba.Length, rgba.Length);
        }

        image = new Image
        {
            Data = mem,
            Width = w,
            Height = h,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR8G8B8A8
        };
        return true;
    }

    private static byte[]? DecodeBmp(byte[] d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 54 || d[0] != 'B' || d[1] != 'M') return null;

        int dataOffset = BitConverter.ToInt32(d, 10);
        w = BitConverter.ToInt32(d, 18);
        int rawH = BitConverter.ToInt32(d, 22);
        int bpp = BitConverter.ToUInt16(d, 28);
        int compression = BitConverter.ToInt32(d, 30);
        if (compression != 0 || (bpp != 24 && bpp != 32 && bpp != 8)) return null;

        bool bottomUp = rawH > 0;
        h = Math.Abs(rawH);
        int stride = ((w * bpp + 31) / 32) * 4;
        var output = new byte[w * h * 4];

        int paletteOffset = 14 + BitConverter.ToInt32(d, 14);

        for (int y = 0; y < h; y++)
        {
            int srcRow = dataOffset + (bottomUp ? h - 1 - y : y) * stride;
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                int s;
                switch (bpp)
                {
                    case 8:
                        s = paletteOffset + d[srcRow + x] * 4;
                        break;
                    case 24:
                        s = srcRow + x * 3;
                        break;
                    default:
                        s = srcRow + x * 4;
                        break;
                }
                output[o + 0] = d[s + 2];
                output[o + 1] = d[s + 1];
                output[o + 2] = d[s + 0];
                output[o + 3] = 255;
            }
        }
        return output;
    }

    private static byte[]? DecodeTga(byte[] d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 18) return null;

        int idLength = d[0];
        int imageType = d[2];
        w = BitConverter.ToUInt16(d, 12);
        h = BitConverter.ToUInt16(d, 14);
        int bpp = d[16];
        bool topDown = (d[17] & 0x20) != 0;
        if (imageType != 2 || (bpp != 24 && bpp != 32)) return null;

        int bytesPerPixel = bpp / 8;
        int dataOffset = 18 + idLength;
        var output = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            int srcRow = dataOffset + (topDown ? y : h - 1 - y) * w * bytesPerPixel;
            for (int x = 0; x < w; x++)
            {
                int s = srcRow + x * bytesPerPixel;
                int o = (y * w + x) * 4;
                output[o + 0] = d[s + 2];
                output[o + 1] = d[s + 1];
                output[o + 2] = d[s + 0];
                output[o + 3] = bytesPerPixel == 4 ? d[s + 3] : (byte)255;
            }
        }
        return output;
    }
}
