using System.Numerics;
using BallanceRevival.Nmo;

namespace BallanceRevival.Gameplay;

public class CheckpointTrigger
{
    public string Name { get; }
    public Vector3 TriggerPosition { get; }
    public Vector3 SpawnPosition { get; }
    public float Radius { get; } = 4.0f;
    public bool IsActivated { get; set; }
    public NmoEntity? Entity { get; }

    public CheckpointTrigger(string name, Vector3 triggerPos, Vector3 spawnPos, NmoEntity? entity = null)
    {
        Name = name;
        TriggerPosition = triggerPos;
        SpawnPosition = spawnPos;
        Entity = entity;
    }

    public bool Check(Vector3 ballPos)
    {
        return !IsActivated && Vector3.Distance(ballPos, TriggerPosition) <= Radius;
    }
}

public enum PickupType
{
    Point,
    Life
}

public class PickupItem
{
    public PickupType Type { get; }
    public Vector3 BasePosition { get; }
    public Vector3 CurrentPosition { get; private set; }
    public float SpinAngle { get; private set; }
    public float Radius { get; } = 2.8f;
    public bool IsCollected { get; set; }
    public NmoEntity? Entity { get; }

    public PickupItem(PickupType type, Vector3 position, NmoEntity? entity = null)
    {
        Type = type;
        BasePosition = position;
        CurrentPosition = position;
        Entity = entity;
    }

    public void Update(float dt, float totalTime)
    {
        if (IsCollected) return;

        SpinAngle = (SpinAngle + 120.0f * dt) % 360.0f;
        float bob = MathF.Sin(totalTime * 3.5f) * 0.45f;
        CurrentPosition = new Vector3(BasePosition.X, BasePosition.Y + bob, BasePosition.Z);
    }

    public bool Check(Vector3 ballPos)
    {
        return !IsCollected && Vector3.Distance(ballPos, CurrentPosition) <= Radius;
    }
}

public class TransformerTrigger
{
    public BallMaterial TargetMaterial { get; }
    public Vector3 Position { get; }
    public float Radius { get; } = 3.5f;
    public float Cooldown { get; set; }
    public NmoEntity? Entity { get; }

    public TransformerTrigger(BallMaterial targetMaterial, Vector3 position, NmoEntity? entity = null)
    {
        TargetMaterial = targetMaterial;
        Position = position;
        Entity = entity;
    }

    public void Update(float dt)
    {
        if (Cooldown > 0f) Cooldown -= dt;
    }

    public bool Check(Vector3 ballPos, BallMaterial currentMaterial)
    {
        if (Cooldown > 0f || TargetMaterial == currentMaterial) return false;
        return Vector3.Distance(ballPos, Position) <= Radius;
    }
}

public class MovableBox
{
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 HalfExtents { get; } = new(1.8f, 1.8f, 1.8f);
    public float Mass { get; } = 25.0f;
    public NmoEntity? Entity { get; }

    public MovableBox(Vector3 position, NmoEntity? entity = null)
    {
        Position = position;
        Entity = entity;
    }

    public void Update(float dt, BallPlayer ball)
    {
        // Simple sphere vs box push interaction
        Vector3 diff = ball.Position - Position;
        Vector3 clamped = Vector3.Clamp(diff, -HalfExtents, HalfExtents);
        Vector3 closest = Position + clamped;
        Vector3 toBall = ball.Position - closest;
        float distSq = toBall.LengthSquared();

        if (distSq < ball.Properties.Radius * ball.Properties.Radius && distSq > 1e-6f)
        {
            float dist = MathF.Sqrt(distSq);
            Vector3 pushDir = toBall / dist;
            float penetration = ball.Properties.Radius - dist;

            // Only push box if ball has enough mass (Stone ball or fast Wood ball)
            float pushForce = (ball.Properties.Mass / (ball.Properties.Mass + Mass));
            Position -= pushDir * (penetration * (1f - pushForce));
            ball.Position += pushDir * (penetration * pushForce);

            // Transfer velocity
            Velocity -= pushDir * Vector3.Dot(ball.Velocity, pushDir) * 0.5f;
        }

        // Apply friction and small gravity
        Velocity = new Vector3(Velocity.X * 0.90f, Velocity.Y, Velocity.Z * 0.90f);
        Position += Velocity * dt;
    }
}
