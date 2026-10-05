using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Ballance's grate flames: a hot magenta core sitting in the grate, embers streaming upward and
/// bending with the wind, and a faint pink smoke trail. Particles are a pure function of time and
/// flame position, so no per-flame state has to survive level reloads.
/// </summary>
public class FlameRenderer : IDisposable
{
    private const int EmberCount = 24;
    private const int SmokeCount = 12;
    private const float Height = 5.6f;

    private static readonly Vector3 Wind = Vector3.Normalize(new Vector3(0.8f, 0f, 0.35f));

    private readonly Texture2D _ember;
    private readonly Texture2D _smoke;
    private readonly Texture2D _glow;

    private Vector3 _right, _up, _eye;

    public FlameRenderer(string texturesDir)
    {
        _ember = LoadSprite(Path.Combine(texturesDir, "Particle_Flames.bmp"), keepColor: true);
        _smoke = LoadSprite(Path.Combine(texturesDir, "Particle_Smoke.bmp"), keepColor: false);
        Image img = Raylib.GenImageGradientRadial(64, 64, 0.0f, Color.White, Color.Blank);
        _glow = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref _glow);
        Raylib.SetTextureFilter(_glow, TextureFilter.Trilinear);
    }

    public bool Ready => _ember.Id != 0;

    private static Texture2D LoadSprite(string path, bool keepColor)
    {
        Texture2D tex = Billboard.LoadMasked(path, keepColor);
        if (tex.Id == 0) return tex;
        // Sprites shrink to a few pixels at distance; mipmaps keep them from sparkling
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        return tex;
    }

    /// <summary>
    /// Draws every flame (xyz grate center, w size scale) inside BeginMode3D. The body is alpha blended so it stays
    /// saturated magenta over Ballance's pale pink skies, where additive blending washes out to white;
    /// only the hot highlights are additive.
    /// </summary>
    public void DrawAll(Camera3D camera, List<Vector4> bases, float time)
    {
        if (bases.Count == 0 || !Ready) return;

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        _right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        _up = Vector3.Cross(_right, forward);
        _eye = camera.Position;

        // Back to front so the alpha-blended bodies overlap correctly
        Vector4 eye = new(camera.Position, 0f);
        bases.Sort((a, b) => DistSq(b, eye).CompareTo(DistSq(a, eye)));

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var p in bases) DrawBody(new Vector3(p.X, p.Y, p.Z), p.W, time);
        Raylib.EndBlendMode();

        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var p in bases) DrawHighlights(new Vector3(p.X, p.Y, p.Z), p.W, time);
        Raylib.EndBlendMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    private static float DistSq(Vector4 a, Vector4 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    private static uint Seed(Vector3 p) =>
        Hash((uint)(int)(p.X * 73.0f) ^ ((uint)(int)(p.Z * 151.0f) << 8) ^ ((uint)(int)(p.Y * 29.0f) << 16));

    private static float Flicker(uint seed, float time) =>
        0.88f + 0.08f * MathF.Sin(time * 13f + seed % 97) + 0.04f * MathF.Sin(time * 29f + seed % 61);

    private void DrawBody(Vector3 basePos, float s, float time)
    {
        uint seed = Seed(basePos);
        float flicker = Flicker(seed, time);

        // Smoke first so the embers' hot colors read on top
        Rlgl.SetTexture(_smoke.Id);
        Rlgl.Begin(DrawMode.Quads);
        for (int i = 0; i < SmokeCount; i++)
        {
            float life = 1.7f + 0.6f * Rand(seed, i, 1);
            float phase = time / life + Rand(seed, i, 2);
            float t = phase - MathF.Floor(phase);
            uint cycle = (uint)(int)MathF.Floor(phase);

            Vector3 p = basePos + s * (
                Vector3.UnitY * (1.2f + Height * 1.15f * t)
                + Wind * (2.6f * t * t)
                + Jitter(seed, i, cycle, 0.5f) * t);
            float size = (1.6f + 2.2f * t) * s;
            float alpha = Smooth(0f, 0.15f, t) * (1f - t) * 0.6f;
            Quad(p, size, Rand(seed, i, 3) * 6.28f + time * 0.6f, new Color((byte)245, (byte)110, (byte)205, (byte)(255 * alpha)));
        }
        Rlgl.End();

        Rlgl.SetTexture(_ember.Id);
        Rlgl.Begin(DrawMode.Quads);
        for (int i = 0; i < EmberCount; i++)
        {
            float life = 0.75f + 0.5f * Rand(seed, i, 4);
            float phase = time / life + Rand(seed, i, 5);
            float t = phase - MathF.Floor(phase);
            uint cycle = (uint)(int)MathF.Floor(phase);

            float sway = MathF.Sin(time * 3.1f + i * 1.7f + seed % 13) * 0.35f * t;
            Vector3 p = basePos + s * (
                Jitter(seed, i, cycle, 0.45f)
                + Vector3.UnitY * (0.3f + Height * (t + 0.2f * t * t))
                + Wind * (1.9f * t * t + sway)
                + _right * (sway * 0.5f));

            float size = s * (2.0f - 1.3f * t) * (0.8f + 0.4f * Rand(seed, i, 6)) * Smooth(0f, 0.12f, t + 0.04f) * flicker;
            float alpha = Smooth(0f, 0.06f, t) * MathF.Pow(1f - t, 1.2f);

            // Hot pink near the grate cooling to deep magenta up the trail
            byte g = (byte)(110 - 90 * t);
            byte b = (byte)(225 - 35 * t);
            float spin = Rand(seed, i, 7) * 6.28f + time * (Rand(seed, i, 8) - 0.5f) * 4f;
            Quad(p, size, spin, new Color((byte)255, g, b, (byte)(190 * alpha)));
        }
        Rlgl.End();

        // Solid magenta ball sitting in the grate, over the embers so it reads as one smooth glow
        Rlgl.SetTexture(_glow.Id);
        Rlgl.Begin(DrawMode.Quads);
        Vector3 core = basePos + new Vector3(0f, 0.6f * s, 0f);
        Quad(core, 3.4f * s * flicker, 0f, new Color((byte)235, (byte)25, (byte)155, (byte)130));
        Quad(core, 2.3f * s * flicker, 0f, new Color((byte)250, (byte)45, (byte)175, (byte)250));
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    private void DrawHighlights(Vector3 basePos, float s, float time)
    {
        uint seed = Seed(basePos);
        float flicker = Flicker(seed, time);

        Rlgl.SetTexture(_glow.Id);
        Rlgl.Begin(DrawMode.Quads);
        Vector3 core = basePos + new Vector3(0f, 0.6f * s, 0f);
        Quad(core, 1.9f * s * flicker, 0f, new Color((byte)255, (byte)120, (byte)220, (byte)190));
        Quad(core + new Vector3(0f, 0.15f * s, 0f), 0.9f * s * flicker, 0f, new Color((byte)255, (byte)230, (byte)250, (byte)200));
        Rlgl.End();

        // A few bright sparks in the lower trail
        Rlgl.SetTexture(_ember.Id);
        Rlgl.Begin(DrawMode.Quads);
        for (int i = 0; i < 6; i++)
        {
            float life = 0.6f + 0.4f * Rand(seed, i, 9);
            float phase = time / life + Rand(seed, i, 10);
            float t = phase - MathF.Floor(phase);
            uint cycle = (uint)(int)MathF.Floor(phase);
            Vector3 p = basePos + s * (Jitter(seed, i + 100, cycle, 0.35f)
                + Vector3.UnitY * (0.6f + Height * 0.6f * t) + Wind * (1.2f * t * t));
            float alpha = Smooth(0f, 0.1f, t) * (1f - t);
            Quad(p, 0.9f * s * (1f - 0.5f * t), Rand(seed, i, 11) * 6.28f, new Color((byte)255, (byte)170, (byte)235, (byte)(170 * alpha)));
        }
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    private void Quad(Vector3 c, float size, float angle, Color color)
    {
        float h = size * 0.5f;
        // Slide along the view ray toward the eye: same screen position, but the lower half of the
        // sprite no longer sinks into the floor around the grate and gets depth-clipped
        Vector3 toEye = _eye - c;
        float dist = toEye.Length();
        if (dist > 1e-3f) c += toEye * (MathF.Min(h * 1.2f, dist * 0.5f) / dist);
        float cs = MathF.Cos(angle) * h, sn = MathF.Sin(angle) * h;
        Vector3 ax = _right * cs + _up * sn;
        Vector3 ay = _up * cs - _right * sn;
        Vector3 a = c - ax - ay, b = c + ax - ay, d = c - ax + ay, e = c + ax + ay;

        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(a.X, a.Y, a.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(b.X, b.Y, b.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(e.X, e.Y, e.Z);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(d.X, d.Y, d.Z);
    }

    private static Vector3 Jitter(uint seed, int i, uint cycle, float radius)
    {
        uint h = Hash(seed ^ ((uint)i * 0x9E3779B9u) ^ (cycle * 0x85EBCA6Bu));
        float a = (h & 0xFFFF) / 65535f * MathF.PI * 2f;
        float r = (h >> 16) / 65535f * radius;
        return new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
    }

    private static float Rand(uint seed, int i, int channel) =>
        (Hash(seed ^ ((uint)i * 0x9E3779B9u) ^ ((uint)channel * 0xC2B2AE35u)) & 0xFFFFFF) / 16777215f;

    private static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7FEB352Du;
        x ^= x >> 15; x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public void Dispose()
    {
        if (_ember.Id != 0) Raylib.UnloadTexture(_ember);
        if (_smoke.Id != 0) Raylib.UnloadTexture(_smoke);
        if (_glow.Id != 0) Raylib.UnloadTexture(_glow);
    }
}
