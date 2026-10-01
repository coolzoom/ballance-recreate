using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public class Camera3DController
{
    public Camera3D Camera;

    private float _targetYaw = 0f;
    private float _currentYaw = 0f;
    private float _currentPitch = 26f;
    private float _targetPitch = 26f;
    private float _distance = 50f;
    private Vector3 _currentLookTarget;

    public float CurrentYaw => _currentYaw;

    public Vector3 Forward
    {
        get
        {
            Vector3 diff = Camera.Target - Camera.Position;
            diff.Y = 0f;
            return diff.LengthSquared() > 1e-4f ? Vector3.Normalize(diff) : Vector3.UnitZ;
        }
    }

    public Vector3 Right
    {
        get
        {
            Vector3 fwd = Forward;
            return new Vector3(-fwd.Z, 0f, fwd.X);
        }
    }

    public Camera3DController(Vector3 initialBallPos)
    {
        _currentLookTarget = initialBallPos;
        Camera = new Camera3D
        {
            Up = Vector3.UnitY,
            FovY = 60.0f,
            Projection = CameraProjection.Perspective
        };
        UpdateCameraPosition();
    }

    public void LookAlong(Vector3 direction)
    {
        Vector3 flat = new(direction.X, 0f, direction.Z);
        if (flat.LengthSquared() < 1e-4f) return;
        flat = Vector3.Normalize(flat);
        _currentYaw = _targetYaw = MathF.Atan2(flat.X, flat.Z) * (180f / MathF.PI);
        UpdateCameraPosition();
    }

    public void RotateLeft90()
    {
        _targetYaw -= 90.0f;
    }

    public void RotateRight90()
    {
        _targetYaw += 90.0f;
    }

    public void Update(float dt, Vector3 ballPosition, bool isSpaceDown, float mouseDeltaX = 0f)
    {
        // Smoothly follow ball target
        _currentLookTarget = Vector3.Lerp(_currentLookTarget, ballPosition, 12.0f * dt);

        // Spacebar raises pitch for track overview
        _targetPitch = isSpaceDown ? 62.0f : 26.0f;

        // Mouse drag can also adjust yaw
        if (mouseDeltaX != 0f)
        {
            _targetYaw += mouseDeltaX * 0.25f;
        }

        // Smooth yaw and pitch interpolation
        _currentYaw = LerpAngleDegrees(_currentYaw, _targetYaw, 10.0f * dt);
        _currentPitch = MathF.Min(85f, MathF.Max(15f, Lerp(_currentPitch, _targetPitch, 8.0f * dt)));

        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        float yawRad = _currentYaw * (MathF.PI / 180.0f);
        float pitchRad = _currentPitch * (MathF.PI / 180.0f);

        float horizDist = _distance * MathF.Cos(pitchRad);
        float vertDist = _distance * MathF.Sin(pitchRad);

        float offsetX = horizDist * MathF.Sin(yawRad);
        float offsetZ = horizDist * MathF.Cos(yawRad);

        Camera.Position = new Vector3(
            _currentLookTarget.X - offsetX,
            _currentLookTarget.Y + vertDist,
            _currentLookTarget.Z - offsetZ
        );

        Camera.Target = _currentLookTarget + new Vector3(0f, 0.8f, 0f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static float LerpAngleDegrees(float current, float target, float t)
    {
        float diff = (target - current) % 360.0f;
        if (diff > 180.0f) diff -= 360.0f;
        if (diff < -180.0f) diff += 360.0f;
        return current + diff * Math.Clamp(t, 0f, 1f);
    }
}
