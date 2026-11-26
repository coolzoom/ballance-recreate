using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Camera-facing sprites. Ballance's flame and pickup textures are bright shapes on black,
/// so the black is turned into transparency and the shape is tinted.
/// </summary>
public static unsafe class Billboard
{
    public static Texture2D LoadMasked(string path, bool keepColor = false)
    {
        if (!ImageDecoder.TryLoad(path, out Image img))
            return default;

        byte* p = (byte*)img.Data;
        int n = img.Width * img.Height;
        for (int i = 0; i < n; i++)
        {
            byte r = p[i * 4 + 0], g = p[i * 4 + 1], b = p[i * 4 + 2];
            byte a = Math.Max(r, Math.Max(g, b));
            // The source is a bright shape on black. Black is the color key, not a dark flame.
            if (a < 8) a = 0;
            if (!keepColor)
            {
                p[i * 4 + 0] = 255;
                p[i * 4 + 1] = 255;
                p[i * 4 + 2] = 255;
            }
            p[i * 4 + 3] = a;
        }

        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return tex;
    }

    /// <summary>
    /// Loads a circular sprite on a black background (like ExtraBall.bmp), keeping interior
    /// dark metallic reflections fully opaque while smoothly cutting out the black corners.
    /// </summary>
    public static Texture2D LoadCircularSprite(string path)
    {
        if (!ImageDecoder.TryLoad(path, out Image img))
            return default;

        byte* p = (byte*)img.Data;
        int w = img.Width, h = img.Height;
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float rad = MathF.Min(w, h) * 0.485f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                float dx = x - cx, dy = y - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float edge = Math.Clamp((rad - d) * 0.7f, 0f, 1f);
                p[idx + 3] = (byte)(edge * 255f);
            }
        }

        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return tex;
    }

    /// <summary>Camera-facing quad centered at <paramref name="center"/> (for spheres and floating particles).</summary>
    public static void DrawCentered(Texture2D tex, Vector3 center, Camera3D camera, float size, Color tint)
    {
        if (tex.Id == 0) return;

        Vector3 forward = Vector3.Normalize(camera.Position - center);
        Vector3 right = Vector3.Normalize(Vector3.Cross(camera.Up, forward));
        Vector3 up = Vector3.Cross(forward, right);

        float half = size * 0.5f;
        Vector3 rx = right * half;
        Vector3 uy = up * half;

        Vector3 bl = center - rx - uy;
        Vector3 br = center + rx - uy;
        Vector3 tr = center + rx + uy;
        Vector3 tl = center - rx + uy;

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(bl.X, bl.Y, bl.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(br.X, br.Y, br.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(tr.X, tr.Y, tr.Z);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(tl.X, tl.Y, tl.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public static void Draw(Texture2D tex, Vector3 center, Vector3 cameraPos, float width, float height, Color tint)
    {
        if (tex.Id == 0) return;

        Vector3 toCam = cameraPos - center;
        toCam.Y = 0f;
        if (toCam.LengthSquared() < 1e-6f) toCam = Vector3.UnitZ;
        toCam = Vector3.Normalize(toCam);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toCam));

        Vector3 bl = center - right * (width * 0.5f);
        Vector3 br = center + right * (width * 0.5f);
        Vector3 tl = bl + Vector3.UnitY * height;
        Vector3 tr = br + Vector3.UnitY * height;

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(bl.X, bl.Y, bl.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(br.X, br.Y, br.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(tr.X, tr.Y, tr.Z);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(tl.X, tl.Y, tl.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }
}
