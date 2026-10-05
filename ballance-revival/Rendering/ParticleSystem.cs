using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Color Color;
    public float Size;
    public float Life;
    public float MaxLife;
}

public class ParticleSystem : IDisposable
{
    private readonly List<Particle> _particles = [];
    private readonly Random _rand = new();
    private Texture2D _glow;

    public void EmitBurst(Vector3 position, Color color, int count, float speed = 8.0f, float size = 0.35f, float life = 0.8f)
    {
        for (int i = 0; i < count; i++)
        {
            float theta = (float)(_rand.NextDouble() * Math.PI * 2.0);
            float phi = (float)(_rand.NextDouble() * Math.PI);
            float spd = speed * (0.4f + (float)_rand.NextDouble() * 0.8f);

            Vector3 vel = new Vector3(
                MathF.Sin(phi) * MathF.Cos(theta),
                MathF.Cos(phi) + 0.3f, // Slight upward bias
                MathF.Sin(phi) * MathF.Sin(theta)
            ) * spd;

            _particles.Add(new Particle
            {
                Position = position,
                Velocity = vel,
                Color = color,
                Size = size * (0.6f + (float)_rand.NextDouble() * 0.8f),
                Life = life,
                MaxLife = life
            });
        }
    }

    public void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                // Swap-remove: draw order does not matter for additive sprites
                int last = _particles.Count - 1;
                _particles[i] = _particles[last];
                _particles.RemoveAt(last);
                continue;
            }

            p.Velocity += new Vector3(0f, -15.0f * dt, 0f); // Gravity on particles
            p.Position += p.Velocity * dt;
            _particles[i] = p;
        }
    }

    public void Draw(Camera3D camera)
    {
        if (_particles.Count == 0) return;
        if (_glow.Id == 0)
        {
            Image img = Raylib.GenImageGradientRadial(64, 64, 0.15f, Color.White, Color.Blank);
            _glow = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(_glow, TextureFilter.Bilinear);
        }

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        Vector3 up = Vector3.Cross(right, forward);

        // Additive glow sprites in one batch; no depth writes so overlapping sparks blend instead of clipping
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.SetTexture(_glow.Id);
        Rlgl.Begin(DrawMode.Quads);
        foreach (var p in _particles)
        {
            float t = p.Life / p.MaxLife;
            // Glow quads read smaller than solid spheres, so scale up the visual footprint
            float half = p.Size * (0.6f + 0.6f * t) * 1.8f;
            Vector3 r = right * half, u = up * half;
            Rlgl.Color4ub(p.Color.R, p.Color.G, p.Color.B, (byte)(p.Color.A * t));
            Vector3 a = p.Position - r - u, b = p.Position + r - u, c = p.Position + r + u, d = p.Position - r + u;
            Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(a.X, a.Y, a.Z);
            Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(b.X, b.Y, b.Z);
            Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(c.X, c.Y, c.Z);
            Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(d.X, d.Y, d.Z);
        }
        Rlgl.End();
        Rlgl.SetTexture(0);
        Raylib.EndBlendMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    public void Dispose()
    {
        if (_glow.Id != 0) Raylib.UnloadTexture(_glow);
        _glow = default;
    }
}
