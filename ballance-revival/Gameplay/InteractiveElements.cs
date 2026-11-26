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
    public float Mass { get; } = 15.0f;
    public bool IsGrounded { get; private set; }
    public Vector3 AxisX { get; } = Vector3.UnitX;
    public Vector3 AxisY { get; } = Vector3.UnitY;
    public Vector3 AxisZ { get; } = Vector3.UnitZ;
    public NmoEntity? Entity { get; }

    public MovableBox(Vector3 position, NmoEntity? entity = null)
    {
        Position = position;
        Entity = entity;

        // Extract horizontal orientation from entity WorldMatrix (e.g., rotated crates in Level 3)
        if (entity != null)
        {
            var m = entity.WorldMatrix;
            var rx = new Vector3(m.M11, 0f, m.M13);
            var rz = new Vector3(m.M31, 0f, m.M33);
            if (rx.LengthSquared() > 1e-4f && rz.LengthSquared() > 1e-4f)
            {
                AxisX = Vector3.Normalize(rx);
                AxisY = Vector3.UnitY;
                AxisZ = Vector3.Normalize(Vector3.Cross(AxisX, AxisY));
                if (Vector3.Dot(AxisZ, rz) < 0f) AxisZ = -AxisZ;
            }
        }
    }

    // The ball-vs-box contact is resolved inside BallPlayer (its own substeps) so
    // the ball reacts in the same frame and can never tunnel through a box. This
    // method simulates the box's own dynamics: gravity, sliding friction, integration,
    // and exact OBB-vs-TriangleMesh collision against floors, curbs, and walls.
    public void Update(float dt, Physics.TriangleMeshCollider? collider = null)
    {
        // Apply gravity
        Velocity = new Vector3(Velocity.X, Velocity.Y + BallPlayer.Gravity * dt, Velocity.Z);

        // Kinetic friction with the floor so crates slide smoothly then settle
        float drag = MathF.Exp(-BoxKineticFriction * dt);
        Velocity = new Vector3(Velocity.X * drag, Velocity.Y, Velocity.Z * drag);

        // Integrate position
        Position += Velocity * dt;

        // Exact OBB-vs-TriangleMesh collision (prevents corners clipping into walls/curbs)
        if (collider != null)
        {
            if (collider.CollideBox(Position, HalfExtents, AxisX, AxisY, AxisZ, out Vector3 correction, out Vector3 normal, out bool grounded))
            {
                Position += correction;
                IsGrounded = grounded;
                float normalVel = Vector3.Dot(Velocity, normal);
                if (normalVel < 0f)
                {
                    Velocity -= normal * normalVel;
                }
            }
            else
            {
                IsGrounded = false;
            }
        }
    }

    /// <summary>
    /// Substepped simulation for all boxes in a level: integrates motion, resolves
    /// box-vs-box contacts (before and after wall collisions) so crates never overlap
    /// each other or sink into walls/curbs.
    /// </summary>
    public static void StepAll(IReadOnlyList<MovableBox> boxes, float dt, Physics.TriangleMeshCollider collider)
    {
        if (boxes.Count == 0) return;

        const int subSteps = 4;
        float subDt = dt / subSteps;

        for (int step = 0; step < subSteps; step++)
        {
            for (int i = 0; i < boxes.Count; i++)
                boxes[i].Update(subDt, collider);

            // Resolve box-vs-box contacts (2 passes for chains / stacked crates)
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                        ResolvePair(boxes[i], boxes[j]);
                }

                // Re-enforce wall/floor constraints after box-vs-box push
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    if (collider.CollideBox(b.Position, b.HalfExtents, b.AxisX, b.AxisY, b.AxisZ, out Vector3 corr, out Vector3 n, out bool g))
                    {
                        b.Position += corr;
                        if (g) b.IsGrounded = true;
                        float nv = Vector3.Dot(b.Velocity, n);
                        if (nv < 0f) b.Velocity -= n * nv;
                    }
                }
            }
        }
    }

    // Kinetic friction coefficient applied (exponentially, frame-rate independent) to a
    // box sliding on the floor.
    public const float BoxKineticFriction = 2.5f;

    // Restitution used when two boxes collide / are pushed into each other.
    public const float BoxRestitution = 0.05f;

    /// <summary>
    /// Exact 3D OBB-vs-OBB collision for Y-rotated crates (5 SAT axes: UnitY, A.X, A.Z, B.X, B.Z).
    /// Handles both side-by-side pushing and stacked crates without overlap.
    /// </summary>
    public static void ResolvePair(MovableBox a, MovableBox b)
    {
        Vector3 delta = a.Position - b.Position; // points from b to a

        float maxDist = a.HalfExtents.Length() + b.HalfExtents.Length();
        if (delta.LengthSquared() >= maxDist * maxDist) return;

        float minOverlap = float.MaxValue;
        Vector3 n = Vector3.Zero;

        bool CheckAxis(Vector3 axis)
        {
            float ra =
                a.HalfExtents.X * MathF.Abs(Vector3.Dot(a.AxisX, axis)) +
                a.HalfExtents.Y * MathF.Abs(Vector3.Dot(a.AxisY, axis)) +
                a.HalfExtents.Z * MathF.Abs(Vector3.Dot(a.AxisZ, axis));
            float rb =
                b.HalfExtents.X * MathF.Abs(Vector3.Dot(b.AxisX, axis)) +
                b.HalfExtents.Y * MathF.Abs(Vector3.Dot(b.AxisY, axis)) +
                b.HalfExtents.Z * MathF.Abs(Vector3.Dot(b.AxisZ, axis));

            float proj = Vector3.Dot(delta, axis);
            float overlap = (ra + rb) - MathF.Abs(proj);
            if (overlap <= 0f) return false;

            if (overlap < minOverlap)
            {
                minOverlap = overlap;
                n = proj >= 0f ? axis : -axis; // points from b to a
            }
            return true;
        }

        if (!CheckAxis(Vector3.UnitY)) return;
        if (!CheckAxis(a.AxisX)) return;
        if (!CheckAxis(a.AxisZ)) return;
        if (!CheckAxis(b.AxisX)) return;
        if (!CheckAxis(b.AxisZ)) return;

        if (minOverlap <= 1e-5f) return;

        float invMassSum = 1f / a.Mass + 1f / b.Mass;
        float vn = Vector3.Dot(a.Velocity - b.Velocity, n); // < 0 means approaching
        if (vn < 0f)
        {
            float restitution = (-vn > BallPlayer.BallBoxBounceSpeed) ? BoxRestitution : 0f;
            float jn = -(1f + restitution) * vn / invMassSum;
            Vector3 impA = n * (jn / a.Mass);
            Vector3 impB = -n * (jn / b.Mass);
            if (a.IsGrounded && impA.Y < 0f) impA.Y = 0f;
            if (b.IsGrounded && impB.Y < 0f) impB.Y = 0f;
            a.Velocity += impA;
            b.Velocity += impB;
        }

        // Positional separation: handle stacked crates cleanly
        if (MathF.Abs(n.Y) > 0.5f)
        {
            if (n.Y > 0f && b.IsGrounded)
            {
                a.Position += n * minOverlap;
                a.IsGrounded = true;
            }
            else if (n.Y < 0f && a.IsGrounded)
            {
                b.Position -= n * minOverlap;
                b.IsGrounded = true;
            }
            else
            {
                a.Position += n * (minOverlap * 0.5f);
                b.Position -= n * (minOverlap * 0.5f);
            }
        }
        else
        {
            float aShare = (1f / a.Mass) / invMassSum;
            float bShare = (1f / b.Mass) / invMassSum;
            a.Position += n * (minOverlap * aShare);
            b.Position -= n * (minOverlap * bShare);
        }
    }
}
