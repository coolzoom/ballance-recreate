using System.Numerics;
using BallanceRevival.Physics;

namespace BallanceRevival.Gameplay;

public class BallPlayer
{
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 AngularVelocity { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public BallProperties Properties { get; private set; } = BallProperties.Wood;
    public BallMaterial CurrentMaterial => Properties.Material;

    public bool IsGrounded { get; private set; }
    public Vector3 LastContactNormal { get; private set; } = Vector3.UnitY;
    public SurfaceKind LastSurface { get; private set; } = SurfaceKind.Stone;
    public float LastImpactSpeed { get; private set; }

    // Gameplay.nmo "Set Physics Globals": gravity (0, -20, 0), physics time factor 2.
    // The factor runs the solver at double rate, so one real second falls like -40.
    public const float Gravity = -20.0f * 2.0f;

    public BallPlayer(Vector3 spawnPosition, BallMaterial material = BallMaterial.Wood)
    {
        Position = spawnPosition;
        SetMaterial(material);
    }

    public void SetMaterial(BallMaterial material)
    {
        Properties = BallProperties.ForMaterial(material);
    }

    public void Respawn(Vector3 position)
    {
        Position = position;
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Rotation = Quaternion.Identity;
        IsGrounded = false;
        LastContactNormal = Vector3.UnitY;
    }

    public void Update(float dt, Vector2 moveInput, Vector3 cameraForward, Vector3 cameraRight, TriangleMeshCollider collider, IReadOnlyList<MovableBox>? boxes = null)
    {
        // 4 physics substeps for ultra-smooth rail and slope stability
        const int subSteps = 4;
        float subDt = dt / subSteps;

        Vector3 planarForward = Vector3.Normalize(new Vector3(cameraForward.X, 0f, cameraForward.Z));
        Vector3 planarRight = Vector3.Normalize(new Vector3(cameraRight.X, 0f, cameraRight.Z));
        Vector3 inputDir = planarForward * moveInput.Y + planarRight * moveInput.X;
        if (inputDir.LengthSquared() > 1f)
            inputDir = Vector3.Normalize(inputDir);

        LastImpactSpeed = 0f;

        for (int step = 0; step < subSteps; step++)
        {
            SimulateSubstep(subDt, inputDir, collider, boxes);
        }

        // Integrate 3D visual rotation from angular velocity
        float angSpeed = AngularVelocity.Length();
        if (angSpeed > 1e-4f)
        {
            Vector3 axis = AngularVelocity / angSpeed;
            float angle = angSpeed * dt;
            Quaternion deltaRot = Quaternion.CreateFromAxisAngle(axis, angle);
            Rotation = Quaternion.Normalize(deltaRot * Rotation);
        }
    }

    private void SimulateSubstep(float dt, Vector3 inputDir, TriangleMeshCollider collider, IReadOnlyList<MovableBox>? boxes = null)
    {
        // Apply gravity
        Velocity = new Vector3(Velocity.X, Velocity.Y + Gravity * dt, Velocity.Z);

        // Apply player drive input
        if (inputDir.LengthSquared() > 1e-4f)
        {
            float accel = Properties.Acceleration;

            if (IsGrounded)
            {
                // Slope handling: check if slope is too steep for this ball type (#5 fix)
                float slopeCos = Vector3.Dot(LastContactNormal, Vector3.UnitY);

                // Project input onto the ground plane and check if it goes "uphill":
                // the uphill direction on a slope is the component of the surface normal
                // projected onto the horizontal plane.
                Vector3 slopeHoriz = new Vector3(LastContactNormal.X, 0f, LastContactNormal.Z);
                float slopeHorizLen = slopeHoriz.Length();
                bool isUphill = slopeHorizLen > 0.01f &&
                                Vector3.Dot(inputDir, slopeHoriz / slopeHorizLen) > 0.1f;

                if (isUphill && slopeCos < Properties.SlopeClimbLimit)
                {
                    // Too steep! Limit forward push
                    accel *= MathF.Max(0f, (slopeCos - 0.3f) / (Properties.SlopeClimbLimit - 0.3f));
                }

                // Project input direction onto contact plane
                Vector3 driveDir = inputDir - LastContactNormal * Vector3.Dot(inputDir, LastContactNormal);
                if (driveDir.LengthSquared() > 1e-4f)
                    driveDir = Vector3.Normalize(driveDir);

                Velocity += driveDir * (accel * dt);
            }
            else
            {
                // Small air control
                Velocity += inputDir * (accel * 0.25f * dt);
            }
        }

        // Light speed dampening from the original physicalize block (0.1), stronger on paper.
        float damp = MathF.Exp(-Properties.LinearDamp * dt);
        Velocity = new Vector3(Velocity.X * damp, Velocity.Y, Velocity.Z * damp);

        // Limit maximum horizontal speed
        Vector2 horizVel = new Vector2(Velocity.X, Velocity.Z);
        float hSpeed = horizVel.Length();
        if (hSpeed > Properties.MaxSpeed)
        {
            horizVel = horizVel / hSpeed * Properties.MaxSpeed;
            Velocity = new Vector3(horizVel.X, Velocity.Y, horizVel.Y);
        }

        // Integrate position
        Position += Velocity * dt;

        // Collision detection and resolution
        if (collider.CollideSphere(Position, Properties.Radius, out Vector3 correction, out Vector3 normal, out bool grounded, out SurfaceKind surface))
        {
            Position += correction;
            IsGrounded = grounded;
            LastContactNormal = normal;
            LastSurface = surface;

            // Velocity resolution against contact normal
            float normalVel = Vector3.Dot(Velocity, normal);
            if (normalVel < 0f)
            {
                float impact = -normalVel;
                if (impact > LastImpactSpeed)
                {
                    LastImpactSpeed = impact;
                }

                // Normal impulse with restitution
                float bounce = (impact > 3.0f) ? Properties.Restitution : 0f;
                Vector3 normalImpulse = -normal * normalVel * (1.0f + bounce);
                Velocity += normalImpulse;
            }

            // Rolling resistance and angular sync always run while grounded (#4 fix)
            // Previously these were inside the `normalVel < 0` block, so they would
            // never execute when the ball was sliding along the surface with a small
            // positive normal velocity component.
            Vector3 tangentVel = Velocity - normal * Vector3.Dot(Velocity, normal);
            float tangentSpeed = tangentVel.Length();
            if (tangentSpeed > 1e-4f)
            {
                float drop = MathF.Min(tangentSpeed, Properties.RollingResistance * dt);
                Velocity -= tangentVel * (drop / tangentSpeed);
            }

            // Drive angular velocity to match rolling without slip
            Vector3 targetAngVel = Vector3.Cross(normal, Velocity) / Properties.Radius;
            AngularVelocity = Vector3.Lerp(AngularVelocity, targetAngVel, 18.0f * dt);
        }
        else
        {
            IsGrounded = false;
        }

        // Ball-vs-box collision: each box is a movable solid, so resolve it here in
        // the same substep. This keeps the ball reacting instantly and prevents it
        // from tunneling through a box that the collider (floors/rails only) cannot see.
        ResolveBoxCollisions(boxes, dt);

        // Frame-rate-independent rotational damping (#10 fix)
        float angDamp = MathF.Exp(-0.8f * dt);
        AngularVelocity *= angDamp;
    }

    // Ball-vs-box contact tuning. A box is movable, so momentum is shared with the
    // box's own mass (unlike the immovable floor). Restitution is gated so a ball
    // gently pressed against a box settles instead of bouncing away.
    internal const float BallBoxBounceSpeed = 3.0f;
    private const float BallBoxRestitution = 0.12f;
    private const float BallBoxFriction = 0.25f;

    /// <summary>
    /// Post-step re-separation after MovableBox.StepAll resolves box-vs-box and box-vs-wall
    /// contacts, so a crate stopped by a wall never leaves the ball overlapping it.
    /// </summary>
    public void ResolvePostBoxStep(IReadOnlyList<MovableBox>? boxes, TriangleMeshCollider collider, float dt)
    {
        if (boxes is null || boxes.Count == 0) return;
        // Crates are already settled against walls and each other; move only the ball here
        // so a heavy ball pressing a wall-jammed crate cannot push it into a wall or neighbor crate.
        ResolveBoxCollisions(boxes, dt, moveBoxes: false);
        if (collider.CollideSphere(Position, Properties.Radius, out Vector3 corr, out Vector3 n, out bool g))
        {
            Position += corr;
            if (g)
            {
                IsGrounded = true;
                LastContactNormal = n;
            }
            float nv = Vector3.Dot(Velocity, n);
            if (nv < 0f) Velocity -= n * nv;
        }
    }

    /// <summary>
    /// Resolve the ball against every movable box as an exact Sphere-vs-OBB two-body contact.
    /// Momentum is exchanged by inverse mass so the ball pushes crates naturally without
    /// tunneling, getting sucked in, or launching onto the top face.
    /// </summary>
    private void ResolveBoxCollisions(IReadOnlyList<MovableBox>? boxes, float dt, bool moveBoxes = true)
    {
        if (boxes is null) return;

        float radius = Properties.Radius;
        float invMassBall = 1f / Properties.Mass;

        foreach (var box in boxes)
        {
            Vector3 rel = Position - box.Position;

            // Project ball center into the box's local OBB coordinates (handles rotated crates)
            Vector3 localBall = new(
                Vector3.Dot(rel, box.AxisX),
                Vector3.Dot(rel, box.AxisY),
                Vector3.Dot(rel, box.AxisZ)
            );

            Vector3 clamped = Vector3.Clamp(localBall, -box.HalfExtents, box.HalfExtents);
            Vector3 localDiff = localBall - clamped;
            float distSq = localDiff.LengthSquared();

            if (distSq >= radius * radius)
                continue;

            Vector3 localNormal;
            float penetration;
            if (distSq > 1e-8f)
            {
                float dist = MathF.Sqrt(distSq);
                localNormal = localDiff / dist;
                penetration = radius - dist;
            }
            else
            {
                // Ball center inside the box: eject along the SHALLOWEST axis (minimum distance to face)
                Vector3 toFace = box.HalfExtents - Vector3.Abs(localBall);
                if (toFace.X <= toFace.Y && toFace.X <= toFace.Z)
                {
                    localNormal = new Vector3(localBall.X >= 0f ? 1f : -1f, 0f, 0f);
                    penetration = toFace.X + radius;
                }
                else if (toFace.Z <= toFace.Y)
                {
                    localNormal = new Vector3(0f, 0f, localBall.Z >= 0f ? 1f : -1f);
                    penetration = toFace.Z + radius;
                }
                else
                {
                    localNormal = new Vector3(0f, localBall.Y >= 0f ? 1f : -1f, 0f);
                    penetration = toFace.Y + radius;
                }
            }

            if (penetration <= 1e-5f) continue;

            // Transform contact normal back to world space (pointing from box -> ball)
            Vector3 normal = Vector3.Normalize(
                box.AxisX * localNormal.X +
                box.AxisY * localNormal.Y +
                box.AxisZ * localNormal.Z
            );

            // Vertically-constrained slider blocks (e.g. P_Modul_34_Schiebestein) do not move horizontally
            bool boxImmovable = !moveBoxes || (box.LockHorizontal && MathF.Abs(normal.Y) < 0.5f);
            float invMassBox = boxImmovable ? 0f : (1f / box.Mass);
            float invMassSum = invMassBall + invMassBox;
            float ballShare = invMassBall / invMassSum;
            float boxShare = invMassBox / invMassSum;

            Vector3 relVel = Velocity - box.Velocity;
            float vn = Vector3.Dot(relVel, normal); // < 0 means approaching

            // Normal impulse (separating contacts never attract)
            float jn = 0f;
            if (vn < 0f)
            {
                float impact = -vn;
                if (impact > LastImpactSpeed) LastImpactSpeed = impact;

                float restitution = (impact > BallBoxBounceSpeed) ? BallBoxRestitution : 0f;
                jn = -(1f + restitution) * vn / invMassSum;
                Velocity += normal * (jn * invMassBall);

                Vector3 boxImp = -normal * (jn * invMassBox);
                // Side hits push crates horizontally; never slam a grounded crate into the floor
                if (MathF.Abs(normal.Y) < 0.5f || (box.IsGrounded && boxImp.Y < 0f))
                    boxImp.Y = 0f;
                box.Velocity += boxImp;
            }

            // Correct Coulomb tangential friction (skip on overhead ceiling contacts normal.Y < -0.5
            // so a descending P_Modul_34 stone block never pins the player ball to the floor)
            Vector3 relTangent = relVel - normal * vn;
            float relTanSpeed = relTangent.Length();
            if (relTanSpeed > 1e-5f && jn > 1e-6f && normal.Y > -0.5f)
            {
                float jt = MathF.Min(BallBoxFriction * jn, relTanSpeed / invMassSum);
                Vector3 tDir = relTangent / relTanSpeed;
                Velocity -= tDir * (jt * invMassBall);

                Vector3 boxTanImp = tDir * (jt * invMassBox);
                if (MathF.Abs(normal.Y) < 0.5f || (box.IsGrounded && boxTanImp.Y < 0f))
                    boxTanImp.Y = 0f;
                box.Velocity += boxTanImp;
            }

            // Mass-weighted positional separation
            Vector3 ballCorr = normal * (penetration * ballShare);
            Vector3 boxCorr = -normal * (penetration * boxShare);
            if (MathF.Abs(normal.Y) < 0.5f)
            {
                boxCorr.Y = 0f;
            }
            else if (box.IsGrounded && boxCorr.Y < 0f)
            {
                ballCorr.Y -= boxCorr.Y;
                boxCorr.Y = 0f;
            }

            Position += ballCorr;
            box.Position += boxCorr;

            // A contact whose normal points mostly upward carries the ball like a floor surface
            if (normal.Y > 0.4f)
            {
                IsGrounded = true;
                LastContactNormal = normal;
                Vector3 tVel = (Velocity - box.Velocity);
                tVel -= normal * Vector3.Dot(tVel, normal);
                float tSpeed = tVel.Length();
                if (tSpeed > 1e-4f)
                {
                    float drop = MathF.Min(tSpeed, Properties.RollingResistance * dt);
                    Velocity -= tVel * (drop / tSpeed);
                }
                Vector3 targetAngVel = Vector3.Cross(normal, Velocity - box.Velocity) / radius;
                AngularVelocity = Vector3.Lerp(AngularVelocity, targetAngVel, 18.0f * dt);
            }
        }
    }

    public void ApplyExternalForce(Vector3 force, float dt)
    {
        Velocity += (force / Properties.Mass) * dt;
    }

    public void ApplyDynamicSurfaceContact(Vector3 normal, float dt, SurfaceKind surface = SurfaceKind.Wood)
    {
        if (normal.Y > 0.4f)
        {
            IsGrounded = true;
            LastContactNormal = normal;
            LastSurface = surface;

            Vector3 tVel = Velocity - normal * Vector3.Dot(Velocity, normal);
            float tSpeed = tVel.Length();
            if (tSpeed > 1e-4f)
            {
                float drop = MathF.Min(tSpeed, Properties.RollingResistance * dt);
                Velocity -= tVel * (drop / tSpeed);
            }
            Vector3 targetAngVel = Vector3.Cross(normal, Velocity) / Properties.Radius;
            AngularVelocity = Vector3.Lerp(AngularVelocity, targetAngVel, 18.0f * dt);
        }
    }
}
