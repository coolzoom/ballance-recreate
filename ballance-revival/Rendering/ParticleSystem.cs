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

public class ParticleSystem
{
    private readonly List<Particle> _particles = [];
    private readonly Random _rand = new();

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
                _particles.RemoveAt(i);
                continue;
            }

            p.Velocity += new Vector3(0f, -15.0f * dt, 0f); // Gravity on particles
            p.Position += p.Velocity * dt;
            _particles[i] = p;
        }
    }

    public void Draw()
    {
        foreach (var p in _particles)
        {
            float alphaFrac = p.Life / p.MaxLife;
            Color c = new Color(p.Color.R, p.Color.G, p.Color.B, (byte)(p.Color.A * alphaFrac));
            Raylib.DrawSphere(p.Position, p.Size * alphaFrac, c);
        }
    }
}
