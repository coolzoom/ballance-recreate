using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Per-frame view state shared by every <see cref="MeshRenderer"/>: frustum culling and a single
/// back-to-front transparent pass, so blended surfaces sort across the level and all prefabs.
/// </summary>
public static class RenderQueue
{
    public const float NearPlane = 0.5f;
    public const float FarPlane = 2000f;

    private static readonly Vector4[] _planes = new Vector4[6];
    private static bool _cullEnabled;
    private static Vector3 _viewPos;
    private static Vector3 _shadowCenter;
    private static float _shadowRadius;
    public static bool IsShadowPass { get; private set; }

    private struct TransparentItem
    {
        public RenderSubmesh Submesh;
        public Matrix4x4 Transform;
        public float Depth;
    }

    private static readonly List<TransparentItem> _transparent = [];
    private static readonly Comparison<TransparentItem> _backToFront = (a, b) => b.Depth.CompareTo(a.Depth);

    public static int DrawnMeshes { get; private set; }
    public static int CulledMeshes { get; private set; }
    public static Matrix4x4 ViewMatrix { get; private set; } = Matrix4x4.Identity;
    public static Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;
    public static Matrix4x4 ViewProjectionMatrix { get; private set; } = Matrix4x4.Identity;
    public static Matrix4x4 InvViewProjectionMatrix { get; private set; } = Matrix4x4.Identity;
    public static Vector3 ViewPosition => _viewPos;

    public static void BeginFrame(Camera3D camera, float aspect)
    {
        _viewPos = camera.Position;
        _transparent.Clear();
        DrawnMeshes = 0;
        CulledMeshes = 0;

        Matrix4x4 view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(camera.FovY * (MathF.PI / 180f), aspect, NearPlane, FarPlane);
        Matrix4x4 m = view * proj;

        ViewMatrix = view;
        ProjectionMatrix = proj;
        ViewProjectionMatrix = m;
        InvViewProjectionMatrix = Matrix4x4.Invert(m, out Matrix4x4 inv) ? inv : Matrix4x4.Identity;

        // Gribb-Hartmann plane extraction for row-vector matrices (clip = v * M)
        _planes[0] = new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41);
        _planes[1] = new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41);
        _planes[2] = new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42);
        _planes[3] = new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42);
        _planes[4] = new Vector4(m.M13, m.M23, m.M33, m.M43);
        _planes[5] = new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43);
        for (int i = 0; i < 6; i++)
        {
            float len = new Vector3(_planes[i].X, _planes[i].Y, _planes[i].Z).Length();
            if (len > 0f) _planes[i] /= len;
        }
        _cullEnabled = true;
    }

    /// <summary>Drawing outside a frame (menus, previews) must not be culled against a stale camera.</summary>
    public static void EndFrame()
    {
        FlushTransparent();
        _cullEnabled = false;
    }

    public static void BeginShadowPass(Vector3 center, float radius)
    {
        IsShadowPass = true;
        _shadowCenter = center;
        _shadowRadius = radius;
    }

    public static void EndShadowPass()
    {
        IsShadowPass = false;
    }

    public static bool IsVisible(Vector3 center, float radius)
    {
        if (IsShadowPass)
        {
            float maxR = _shadowRadius + radius;
            return Vector3.DistanceSquared(center, _shadowCenter) <= maxR * maxR;
        }
        if (!_cullEnabled) return true;
        for (int i = 0; i < 6; i++)
        {
            Vector4 p = _planes[i];
            if (p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W < -radius)
            {
                CulledMeshes++;
                return false;
            }
        }
        DrawnMeshes++;
        return true;
    }

    public static void EnqueueTransparent(RenderSubmesh submesh, Matrix4x4 transform, Vector3 worldCenter)
    {
        if (!_cullEnabled)
        {
            submesh.DrawBlended(transform);
            return;
        }
        _transparent.Add(new TransparentItem
        {
            Submesh = submesh,
            Transform = transform,
            Depth = Vector3.DistanceSquared(worldCenter, _viewPos)
        });
    }

    public static void FlushTransparent()
    {
        if (_transparent.Count == 0) return;
        _transparent.Sort(_backToFront);

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        bool additive = false;
        foreach (var item in _transparent)
        {
            if (item.Submesh.Additive != additive)
            {
                additive = item.Submesh.Additive;
                if (additive) Raylib.BeginBlendMode(BlendMode.Additive);
                else Raylib.EndBlendMode();
            }
            item.Submesh.DrawRaw(item.Transform);
        }
        if (additive) Raylib.EndBlendMode();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
        _transparent.Clear();
    }
}
