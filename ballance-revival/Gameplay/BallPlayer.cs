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
        if (collider.CollideSphere(Position, Properties.Radius, out Vector3 correction, out Vector3 normal, out bool grounded))
        {
            Position += correction;
            IsGrounded = grounded;
            LastContactNormal = normal;

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
        ResolveBoxCollisions(boxes);

        // Frame-rate-independent rotational damping (#10 fix)
        float angDamp = MathF.Exp(-0.8f * dt);
        AngularVelocity *= angDamp;
    }

    // Ball-vs-box contact tuning. A box is movable, so momentum is shared with the
    // box's own mass (unlike the immovable floor). Restitution is gated so a ball
    // gently pressed against a box settles instead of bouncing away.
    internal const float BallBoxBounceSpeed = 3.0f;
    private const float BallBoxRestitution = 0.10f;
    private const float BallBoxFriction = 0.35f;

    /// <summary>
    /// Resolve the ball against every movable box as a proper two-body contact.
    /// Momentum is exchanged by the two masses so the ball is pushed back / deflected
    /// and cannot pass through, and a contact whose normal points mostly upward also
    /// grounds the ball so it can roll on top of the box like a surface.
    /// </summary>
    private void ResolveBoxCollisions(IReadOnlyList<MovableBox>? boxes)
    {
        if (boxes is null) return;

        float radius = Properties.Radius;
        float invMassBall = 1f / Properties.Mass;

        foreach (var box in boxes)
        {
            Vector3 diff = Position - box.Position;

            // Closest point of the box to the ball center (sphere vs AABB) + the
            // geometric contact normal, used as a fallback when no face is being driven into.
            Vector3 clamped = Vector3.Clamp(diff, -box.HalfExtents, box.HalfExtents);
            Vector3 toBall = diff - clamped;                 // == Position - (box.Position + clamped)
            float distSq = toBall.LengthSquared();
            Vector3 geoNormal;
            float geoPen;
            if (distSq > 1e-6f)
            {
                float dist = MathF.Sqrt(distSq);
                geoNormal = toBall / dist;
                geoPen = radius - dist;
            }
            else
            {
                // Ball centre inside the box: push out along the shallowest axis.
                Vector3 pen = box.HalfExtents - Vector3.Abs(diff);
                if (pen.X >= pen.Y && pen.X >= pen.Z)      geoNormal = new Vector3(diff.X > 0f ? 1f : -1f, 0f, 0f);
                else if (pen.Y >= pen.Z)                   geoNormal = new Vector3(0f, diff.Y > 0f ? 1f : -1f, 0f);
                else                                       geoNormal = new Vector3(0f, 0f, diff.Z > 0f ? 1f : -1f);
                geoPen = radius;
            }

            if (geoPen <= 1e-5f) continue;                // touching or apart

            // Mass-weighted separation shares (constant for this contact).
            float ballShare = box.Mass / (Properties.Mass + box.Mass);
            float boxShare = Properties.Mass / (Properties.Mass + box.Mass);

            Vector3 vel = Velocity - box.Velocity;        // relative velocity (ball w.r.t. box)
            float speed = vel.Length();

            // Velocity-directed contact: use the face the ball is actually driving into.
            // Without this, a ball rolling at the base of a box (its centre below the
            // box's) snaps onto the bottom face, gets a vertical normal and therefore zero
            // horizontal impulse, so it slides over the box instead of pushing it.
            Vector3 normal;
            float penetration;
            bool driving;
            if (speed > 1e-4f && PickApproachFace(radius, diff, vel, box, out int axis, out int sign, out float facePen, out Vector3 faceNormal))
            {
                normal = faceNormal;
                penetration = facePen;
                driving = true;
            }
            else
            {
                // Being pressed into but not driven at any face: separate only, no impulse,
                // so the ball can rest against / under the box without jitter.
                normal = geoNormal;
                penetration = geoPen;
                driving = false;
            }

            if (!driving)
            {
                Position += normal * (penetration * ballShare);
                box.Position -= normal * (penetration * boxShare);
                if (normal.Y > 0.4f)
                {
                    IsGrounded = true;
                    LastContactNormal = normal;
                }
                continue;
            }

            float vn = Vector3.Dot(vel, normal);          // < 0 means approaching
            float invMassSum = invMassBall + 1f / box.Mass;

            // Normal impulse (separating contacts never attract). Restitution is gated
            // by BallBoxBounceSpeed for a gentle settle on light contacts.
            float jn = 0f;
            if (vn < 0f)
            {
                float restitution = (-vn > BallBoxBounceSpeed) ? BallBoxRestitution : 0f;
                jn = -(1f + restitution) * vn / invMassSum;
                Velocity += normal * (jn * invMassBall);
                box.Velocity -= normal * (jn / box.Mass);
            }

            // Tangential Coulomb friction on the relative tangential velocity. It both
            // lets the ball "grab" and roll off a face and drags the box as it grinds.
            Vector3 relTangent = vel - normal * vn;
            float relTanSpeed = relTangent.Length();
            if (relTanSpeed > 1e-5f && jn > 1e-6f)
            {
                float jt = MathF.Min(BallBoxFriction * jn, relTanSpeed);
                Vector3 tDir = relTangent / relTanSpeed;
                float tScale = jt / invMassSum;
                Velocity += (-tDir) * (tScale * invMassBall);
                box.Velocity += tDir * (tScale / box.Mass);
            }

            // Mass-weighted separation so neither body sinks into the other.
            Position += normal * (penetration * ballShare);
            box.Position -= normal * (penetration * boxShare);

            // A contact whose normal points mostly upward carries the ball, letting
            // it roll on top of the box just like a ground surface.
            if (normal.Y > 0.4f)
            {
                IsGrounded = true;
                LastContactNormal = normal;
            }
        }
    }

    /// <summary>
    /// Pick the box face the ball is driving into, using the relative velocity. This
    /// makes a fast ball hit a vertical side face (transferring horizontal motion) even
    /// when its centre is below the box, where the geometric closest point would be the
    /// bottom face. Scoring = approach speed * plane penetration.
    /// </summary>
    private static bool PickApproachFace(float radius, Vector3 diff, Vector3 vel, MovableBox box,
        out int axis, out int sign, out float pen, out Vector3 normal)
    {
        float hx = box.HalfExtents.X, hy = box.HalfExtents.Y, hz = box.HalfExtents.Z;
        float magX = MathF.Abs(diff.X), magY = MathF.Abs(diff.Y), magZ = MathF.Abs(diff.Z);

        int bestAxis = -1;
        float bestSign = 0f;
        float bestPen = 0f;
        Vector3 bestNormal = Vector3.Zero;
        float bestScore = -1f;
        Eval(0, +1f, diff.X, magX, hx);
        Eval(0, -1f, -diff.X, magX, hx);
        Eval(1, +1f, diff.Y, magY, hy);
        Eval(1, -1f, -diff.Y, magY, hy);
        Eval(2, +1f, diff.Z, magZ, hz);
        Eval(2, -1f, -diff.Z, magZ, hz);

        void Eval(int a, float s, float along, float mag, float half)
        {
            if (mag <= half) return;                              // not beyond this face plane
            float p = radius - (mag - half);                       // sphere penetration vs this face
            if (p <= 0f) return;
            float approach = -(s * (a == 0 ? vel.X : a == 1 ? vel.Y : vel.Z));
            if (approach <= 0f) return;                            // moving away from / parallel to face
            float score = approach * p;
            if (score > bestScore)
            {
                bestScore = score;
                bestAxis = a; bestSign = s; bestPen = p;
                bestNormal = a == 0 ? new Vector3(s, 0f, 0f) : a == 1 ? new Vector3(0f, s, 0f) : new Vector3(0f, 0f, s);
            }
        }

        axis = bestAxis; sign = (int)bestSign; pen = bestPen; normal = bestNormal;
        return bestAxis >= 0;
    }

    public void ApplyExternalForce(Vector3 force, float dt)
    {
        Velocity += (force / Properties.Mass) * dt;
    }
}
