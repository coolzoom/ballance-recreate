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

    public const float Gravity = -38.0f;

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

    public void Update(float dt, Vector2 moveInput, Vector3 cameraForward, Vector3 cameraRight, TriangleMeshCollider collider)
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
            SimulateSubstep(subDt, inputDir, collider);
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

    private void SimulateSubstep(float dt, Vector3 inputDir, TriangleMeshCollider collider)
    {
        // Apply gravity
        Velocity = new Vector3(Velocity.X, Velocity.Y + Gravity * dt, Velocity.Z);

        // Apply player drive input
        if (inputDir.LengthSquared() > 1e-4f)
        {
            float accel = Properties.Acceleration;

            if (IsGrounded)
            {
                // Slope handling: check if slope is too steep for this ball type
                float slopeCos = Vector3.Dot(LastContactNormal, Vector3.UnitY);
                bool isUphill = Vector3.Dot(inputDir, LastContactNormal) < -0.1f;

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

        // Apply air drag
        Velocity = new Vector3(
            Velocity.X * Properties.AirDrag,
            Velocity.Y,
            Velocity.Z * Properties.AirDrag
        );

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

                // Friction along tangential velocity
                Vector3 tangentVel = Velocity - normal * Vector3.Dot(Velocity, normal);
                float frictionAmount = MathF.Min(1.0f, Properties.Friction * 12.0f * dt);
                Velocity -= tangentVel * frictionAmount;

                // Drive angular velocity to match rolling without slip
                Vector3 targetAngVel = Vector3.Cross(normal, Velocity) / Properties.Radius;
                AngularVelocity = Vector3.Lerp(AngularVelocity, targetAngVel, 18.0f * dt);
            }
        }
        else
        {
            IsGrounded = false;
        }

        // Natural rotational damping
        AngularVelocity *= 0.998f;
    }

    public void ApplyExternalForce(Vector3 force, float dt)
    {
        Velocity += (force / Properties.Mass) * dt;
    }
}
