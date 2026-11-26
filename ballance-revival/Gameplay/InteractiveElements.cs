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
    // P_Box_Mesh spans -2.5..2.5 on every axis
    public Vector3 HalfExtents { get; } = new(2.5f, 2.5f, 2.5f);
    public float Mass { get; } = 25.0f;
    public NmoEntity? Entity { get; }

    public MovableBox(Vector3 position, NmoEntity? entity = null)
    {
        Position = position;
        Entity = entity;
    }

    // The ball-vs-box contact is resolved inside BallPlayer (its own substeps) so
    // the ball reacts in the same frame and can never tunnel through a box. This
    // method only simulates the box's own dynamics: gravity, settling friction,
    // integration, and the box's collision with the level floor.
    public void Update(float dt, Physics.TriangleMeshCollider? collider = null)
    {
        // Apply gravity (#8 fix)
        Velocity = new Vector3(Velocity.X, Velocity.Y + BallPlayer.Gravity * dt, Velocity.Z);

        // Kinetic friction with the floor. Boxes slide when pushed and gradually settle;
        // a gentle resting drag (not a hard brake) so a driven ball can actually push it.
        float drag = MathF.Exp(-BoxKineticFriction * dt);
        Velocity = new Vector3(Velocity.X * drag, Velocity.Y, Velocity.Z * drag);

        // Integrate position
        Position += Velocity * dt;

        // Floor collision for the box (#8 fix)
        // Approximate as a sphere of radius equal to half-extent Y for ground checking
        if (collider != null)
        {
            float boxCheckRadius = HalfExtents.Y;
            if (collider.CollideSphere(Position, boxCheckRadius, out Vector3 correction, out Vector3 normal, out bool _))
            {
                Position += correction;
                float normalVel = Vector3.Dot(Velocity, normal);
                if (normalVel < 0f)
                {
                    Velocity -= normal * normalVel;
                }
            }
        }
    }

    // Kinetic friction coefficient applied (exponentially, frame-rate independent) to a
    // box sliding on the floor. Small enough that a driven ball can slide the box, large
    // enough that it eventually settles after being pushed.
    public const float BoxKineticFriction = 2.5f;

    // Restitution used when two boxes collide / are pushed into each other (kept low so
    // chained pushes move together instead of bouncing apart).
    public const float BoxRestitution = 0.05f;

    /// <summary>
    /// Resolve two overlapping boxes against each other (AABB vs AABB). Momentum is
    /// exchanged along the minimum-overlap axis so a box driven into another pushes it
    /// instead of sinking in, and any residual penetration is split by inverse mass.
    /// </summary>
    public static void ResolvePair(MovableBox a, MovableBox b)
    {
        Vector3 d = a.Position - b.Position;          // points from b to a
        Vector3 overlap = (a.HalfExtents + b.HalfExtents) - Vector3.Abs(d);

        if (overlap.X <= 0f || overlap.Y <= 0f || overlap.Z <= 0f) return; // no overlap

        // Minimum-overlap axis is the contact axis; n points from b to a (sign of d),
        // not from overlap (which is always positive).
        Vector3 n;
        float pen;
        if (overlap.X <= overlap.Y && overlap.X <= overlap.Z)
        {
            n = new Vector3(d.X >= 0f ? 1f : -1f, 0f, 0f); pen = overlap.X;
        }
        else if (overlap.Y <= overlap.Z)
        {
            n = new Vector3(0f, d.Y >= 0f ? 1f : -1f, 0f); pen = overlap.Y;
        }
        else
        {
            n = new Vector3(0f, 0f, d.Z >= 0f ? 1f : -1f); pen = overlap.Z;
        }

        // Split separation by inverse mass (lighter box moves more).
        float invMassSum = 1f / a.Mass + 1f / b.Mass;
        float aShare = (1f / a.Mass) / invMassSum;

        float vn = Vector3.Dot(a.Velocity - b.Velocity, n);   // < 0 means approaching
        if (vn < 0f)
        {
            float restitution = (-vn > BallPlayer.BallBoxBounceSpeed) ? BoxRestitution : 0f;
            float jn = -(1f + restitution) * vn / invMassSum;
            a.Velocity += n * (jn / a.Mass);
            b.Velocity -= n * (jn / b.Mass);
        }
        float bShare = (1f / b.Mass) / invMassSum;
        a.Position += n * (pen * aShare);
        b.Position -= n * (pen * bShare);
    }
}
