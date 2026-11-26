using System.Numerics;
using BallanceRevival.Nmo;

namespace BallanceRevival.Physics;

public struct CollisionTriangle
{
    public Vector3 A;
    public Vector3 B;
    public Vector3 C;
    public Vector3 Normal;
    public Vector3 Min;
    public Vector3 Max;

    public CollisionTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        A = a;
        B = b;
        C = c;

        Vector3 cross = Vector3.Cross(b - a, c - a);
        Normal = cross.LengthSquared() > 1e-8f ? Vector3.Normalize(cross) : Vector3.UnitY;

        Min = Vector3.Min(a, Vector3.Min(b, c));
        Max = Vector3.Max(a, Vector3.Max(b, c));
    }

    public Vector3 ClosestPoint(Vector3 p)
    {
        Vector3 ab = B - A;
        Vector3 ac = C - A;
        Vector3 ap = p - A;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return A; // Vertex A

        Vector3 bp = p - B;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return B; // Vertex B

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return A + ab * v; // Edge AB
        }

        Vector3 cp = p - C;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return C; // Vertex C

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return A + ac * w; // Edge AC
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return B + (C - B) * w; // Edge BC
        }

        // Inside face region
        float denom = 1f / (va + vb + vc);
        float vFace = vb * denom;
        float wFace = vc * denom;
        return A + ab * vFace + ac * wFace;
    }
}

public class TriangleMeshCollider
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<int>> _grid = [];
    private readonly List<CollisionTriangle> _triangles = [];
    // Reusable set to avoid per-frame GC allocation (#7)
    private readonly HashSet<int> _checkedTriangles = [];

    public TriangleMeshCollider(float cellSize = 8.0f)
    {
        _cellSize = cellSize;
    }

    public int TriangleCount => _triangles.Count;
    public IReadOnlyList<CollisionTriangle> Triangles => _triangles;

    public void AddEntity(NmoEntity entity)
    {
        if (entity.Mesh == null) return;
        var mesh = entity.Mesh;
        var matrix = entity.WorldMatrix;

        // Transform vertices to world space
        var worldPositions = new Vector3[mesh.Positions.Length];
        for (int i = 0; i < mesh.Positions.Length; i++)
        {
            worldPositions[i] = Vector3.Transform(mesh.Positions[i], matrix);
        }

        foreach (var face in mesh.Faces)
        {
            if (face.V0 < worldPositions.Length &&
                face.V1 < worldPositions.Length &&
                face.V2 < worldPositions.Length)
            {
                AddTriangle(new CollisionTriangle(
                    worldPositions[face.V0],
                    worldPositions[face.V1],
                    worldPositions[face.V2]
                ));
            }
        }
    }

    public void AddTriangle(CollisionTriangle tri)
    {
        int triIndex = _triangles.Count;
        _triangles.Add(tri);

        int minX = (int)MathF.Floor(tri.Min.X / _cellSize);
        int maxX = (int)MathF.Floor(tri.Max.X / _cellSize);
        int minY = (int)MathF.Floor(tri.Min.Y / _cellSize);
        int maxY = (int)MathF.Floor(tri.Max.Y / _cellSize);
        int minZ = (int)MathF.Floor(tri.Min.Z / _cellSize);
        int maxZ = (int)MathF.Floor(tri.Max.Z / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = GetKey(x, y, z);
                    if (!_grid.TryGetValue(key, out var list))
                    {
                        list = [];
                        _grid[key] = list;
                    }
                    list.Add(triIndex);
                }
            }
        }
    }

    // Asymmetric spatial hash to reduce key collisions (#6)
    private static long GetKey(int x, int y, int z)
    {
        unchecked
        {
            long h = (long)x * 73856093;
            h = (h * 31) ^ ((long)y * 19349663);
            h = (h * 31) ^ ((long)z * 83492791);
            return h;
        }
    }

    public bool CollideSphere(Vector3 center, float radius, out Vector3 penetrationCorrection, out Vector3 contactNormal, out bool isGrounded)
    {
        penetrationCorrection = Vector3.Zero;
        contactNormal = Vector3.Zero;
        isGrounded = false;

        // Iterative depenetration: resolve the deepest penetration first, then re-check (#1)
        const int maxIterations = 4;
        int totalContacts = 0;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            Vector3 currentCenter = center + penetrationCorrection;

            float queryRadius = radius + 0.5f;
            int minX = (int)MathF.Floor((currentCenter.X - queryRadius) / _cellSize);
            int maxX = (int)MathF.Floor((currentCenter.X + queryRadius) / _cellSize);
            int minY = (int)MathF.Floor((currentCenter.Y - queryRadius) / _cellSize);
            int maxY = (int)MathF.Floor((currentCenter.Y + queryRadius) / _cellSize);
            int minZ = (int)MathF.Floor((currentCenter.Z - queryRadius) / _cellSize);
            int maxZ = (int)MathF.Floor((currentCenter.Z + queryRadius) / _cellSize);

            _checkedTriangles.Clear(); // Reuse instead of allocating (#7)

            // Find the deepest penetrating triangle this iteration
            float deepestPenetration = 0f;
            Vector3 deepestNormal = Vector3.Zero;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        long key = GetKey(x, y, z);
                        if (_grid.TryGetValue(key, out var list))
                        {
                            foreach (int triIndex in list)
                            {
                                if (!_checkedTriangles.Add(triIndex)) continue;

                                var tri = _triangles[triIndex];

                                // Fast AABB check
                                if (currentCenter.X + radius < tri.Min.X || currentCenter.X - radius > tri.Max.X ||
                                    currentCenter.Y + radius < tri.Min.Y || currentCenter.Y - radius > tri.Max.Y ||
                                    currentCenter.Z + radius < tri.Min.Z || currentCenter.Z - radius > tri.Max.Z)
                                {
                                    continue;
                                }

                                Vector3 closest = tri.ClosestPoint(currentCenter);
                                Vector3 diff = currentCenter - closest;
                                float distSq = diff.LengthSquared();

                                if (distSq < radius * radius)
                                {
                                    Vector3 normal;
                                    float depth;

                                    if (distSq < 1e-8f)
                                    {
                                        // Ball center is ON the triangle — deepest penetration case (#2)
                                        // Fall back to the triangle's geometric normal
                                        normal = tri.Normal;
                                        depth = radius;
                                    }
                                    else
                                    {
                                        float dist = MathF.Sqrt(distSq);
                                        depth = radius - dist;
                                        normal = diff / dist;

                                        // Ignore back-face collisions — tightened threshold (#11)
                                        if (Vector3.Dot(normal, tri.Normal) < -0.1f)
                                            continue;
                                    }

                                    if (depth > deepestPenetration)
                                    {
                                        deepestPenetration = depth;
                                        deepestNormal = normal;
                                    }

                                    // Accumulate contact normal for average direction
                                    contactNormal += normal;
                                    totalContacts++;

                                    if (normal.Y > 0.4f)
                                    {
                                        isGrounded = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (deepestPenetration < 1e-5f)
                break; // No more penetration to resolve

            // Resolve only the deepest penetration this iteration (#1)
            penetrationCorrection += deepestNormal * deepestPenetration;
        }

        if (totalContacts > 0)
        {
            contactNormal = Vector3.Normalize(contactNormal);
            return true;
        }

        return false;
    }
}
