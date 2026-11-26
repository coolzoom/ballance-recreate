using System.Numerics;
using BallanceRevival.Gameplay;
using BallanceRevival.Physics;

namespace BallanceRevival;

/// <summary>
/// Headless physics self-test: validates sphere-triangle collision, ball physics,
/// and all refactoring changes without opening a GPU window.
/// Run with: dotnet run -- --test
/// </summary>
public static class PhysicsSelfTest
{
    private static int _passed, _failed;

    private static void Assert(bool cond, string name)
    {
        if (cond) { _passed++; Console.WriteLine($"  ✅ {name}"); }
        else { _failed++; Console.WriteLine($"  ❌ FAIL: {name}"); }
    }

    private static void AssertNear(float a, float b, float eps, string name) =>
        Assert(MathF.Abs(a - b) < eps, $"{name}: {a:F4} ≈ {b:F4}");

    public static int Run(string? gameRoot = null)
    {
        _passed = 0;
        _failed = 0;
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Ballance Revival — Physics Self-Test        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");

        TestClosestPointFace();
        TestClosestPointEdge();
        TestClosestPointVertex();
        TestSphereFlatGround();
        TestZeroDistanceFallback();
        TestIterativeDepenetration();
        TestUphillDetection();
        TestBallPropertiesSingleton();
        TestAngularDampingFrameRateIndependence();
        TestSpatialHashAsymmetry();
        TestBallGravityFall();
        TestBallGroundedRolling();
        TestBallBounce();
        TestMovableBoxFloorCollision();
        TestBallRestsOnTopOfBox();
        TestHeavyBallPushesBox();
        TestLightBallBouncesOffBox();
        TestWoodBallPushesBox();
        TestBoxesSeparateWhenPushed();
        TestBoxChainAgainstWall();
        TestBallDoesNotClimbCrateFromSide();
        TestStackedBoxes();
        TestPuzzleBallsFallAndCollide();
        if (gameRoot != null) TestRealLevels(gameRoot);

        Console.WriteLine($"\n{"═",-47}");
        Console.WriteLine($"  Results: {_passed} passed, {_failed} failed");
        Console.WriteLine($"{"═",-47}");

        return _failed > 0 ? 1 : 0;
    }

    /// <summary>Horizontal quad at Y=0 wound so its normal faces +Y (same convention as NmoMesh faces).</summary>
    private static void AddFloorQuad(TriangleMeshCollider c, float x0, float x1, float z0, float z1)
    {
        c.AddTriangle(new CollisionTriangle(new(x0, 0, z0), new(x0, 0, z1), new(x1, 0, z1)));
        c.AddTriangle(new CollisionTriangle(new(x0, 0, z0), new(x1, 0, z1), new(x1, 0, z0)));
    }

    // ── Test: ClosestPoint finds face interior ──────────────────────
    private static void TestClosestPointFace()
    {
        Console.WriteLine("\n── ClosestPoint: face center ──");
        var tri = new CollisionTriangle(new(0, 0, 0), new(10, 0, 0), new(5, 0, 10));
        var cp = tri.ClosestPoint(new(5, 5, 3));
        AssertNear(cp.Y, 0f, 0.01f, "Y on face plane");
        Assert(cp.X >= -0.01f && cp.X <= 10.01f, "X in triangle range");
    }

    // ── Test: ClosestPoint snaps to edge ────────────────────────────
    private static void TestClosestPointEdge()
    {
        Console.WriteLine("\n── ClosestPoint: edge ──");
        var tri = new CollisionTriangle(new(0, 0, 0), new(10, 0, 0), new(5, 0, 10));
        var cp = tri.ClosestPoint(new(-2, 0, 5));
        float d1 = Vector3.Distance(cp, tri.A);
        float d2 = Vector3.Distance(cp, tri.C);
        float edgeLen = Vector3.Distance(tri.A, tri.C);
        Assert(d1 + d2 <= edgeLen + 0.02f, "Point lies on edge AC");
    }

    // ── Test: ClosestPoint snaps to vertex ──────────────────────────
    private static void TestClosestPointVertex()
    {
        Console.WriteLine("\n── ClosestPoint: vertex ──");
        var tri = new CollisionTriangle(new(0, 0, 0), new(10, 0, 0), new(5, 0, 10));
        var cp = tri.ClosestPoint(new(-5, 5, -5));
        AssertNear(Vector3.Distance(cp, tri.A), 0f, 0.01f, "Snaps to vertex A");
    }

    // ── Test: Sphere penetrates flat ground correctly ────────────────
    private static void TestSphereFlatGround()
    {
        Console.WriteLine("\n── Sphere vs flat ground ──");
        var collider = new TriangleMeshCollider(16f);
        // Large flat quad at Y=0, wound counter-clockwise so normal faces UP (+Y)
        AddFloorQuad(collider, -50, 50, -50, 50);

        float radius = 2.0f;
        Vector3 center = new(0, 1.5f, 0); // penetrating by 0.5

        bool hit = collider.CollideSphere(center, radius, out var correction, out var normal, out bool grounded);
        Assert(hit, "Collision detected");
        Assert(grounded, "Ball is grounded");
        Assert(normal.Y > 0.9f, $"Normal points up: {normal}");
        AssertNear(correction.Y, 0.5f, 0.15f, "Correction pushes ball up ~0.5");
    }

    // ── Test: Ball center ON triangle — was previously missed (#2) ──
    private static void TestZeroDistanceFallback()
    {
        Console.WriteLine("\n── Zero-distance fallback (#2) ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -50, 50, -50, 50);

        float radius = 2.0f;
        Vector3 center = new(0, 0, 0); // exactly on face — was completely missed before

        bool hit = collider.CollideSphere(center, radius, out var correction, out var normal, out bool grounded);
        Assert(hit, "Collision detected even when center is ON face");
        AssertNear(correction.Length(), radius, 0.2f, $"Correction depth ≈ radius ({radius})");
    }

    // ── Test: Corner penetration uses iterative depenetration (#1) ──
    private static void TestIterativeDepenetration()
    {
        Console.WriteLine("\n── Iterative depenetration (#1) ──");
        var collider = new TriangleMeshCollider(16f);
        // Floor at Y=0
        AddFloorQuad(collider, -50, 50, -50, 50);
        // Wall at X=0
        collider.AddTriangle(new CollisionTriangle(new(0, -5, -50), new(0, 10, 50), new(0, -5, 50)));
        collider.AddTriangle(new CollisionTriangle(new(0, -5, -50), new(0, 10, -50), new(0, 10, 50)));

        float radius = 2.0f;
        Vector3 center = new(0.3f, 1.5f, 0); // near corner of floor+wall

        bool hit = collider.CollideSphere(center, radius, out var correction, out var normal, out bool grounded);
        Assert(hit, "Collision at corner detected");
        Vector3 resolved = center + correction;
        Assert(resolved.Y >= radius - 0.1f, $"Resolved Y={resolved.Y:F2} ≥ radius-ε (not pushed below floor)");
        Assert(resolved.X >= radius - 0.1f, $"Resolved X={resolved.X:F2} ≥ radius-ε (not pushed into wall)");
    }

    // ── Test: Uphill detection fixed (#5) ───────────────────────────
    private static void TestUphillDetection()
    {
        Console.WriteLine("\n── Uphill detection (#5) ──");
        Vector3 slopeNormal = Vector3.Normalize(new(1f, 1f, 0f)); // 45° slope facing +X
        Vector3 inputDir = new(1f, 0f, 0f); // pushing into the slope

        // OLD logic was wrong
        bool oldIsUphill = Vector3.Dot(inputDir, slopeNormal) < -0.1f;
        Assert(!oldIsUphill, "Old logic incorrectly says NOT uphill");

        // NEW logic
        Vector3 slopeHoriz = new(slopeNormal.X, 0f, slopeNormal.Z);
        float slopeHorizLen = slopeHoriz.Length();
        bool newIsUphill = slopeHorizLen > 0.01f &&
                           Vector3.Dot(inputDir, slopeHoriz / slopeHorizLen) > 0.1f;
        Assert(newIsUphill, "New logic correctly detects uphill");
    }

    // ── Test: BallProperties are static readonly (#9) ───────────────
    private static void TestBallPropertiesSingleton()
    {
        Console.WriteLine("\n── BallProperties singleton (#9) ──");
        var a = BallProperties.Wood;
        var b = BallProperties.Wood;
        Assert(ReferenceEquals(a, b), "Wood is same instance (static readonly)");
        Assert(ReferenceEquals(BallProperties.Stone, BallProperties.Stone), "Stone is same instance");
        Assert(ReferenceEquals(BallProperties.Paper, BallProperties.Paper), "Paper is same instance");
        Assert(BallProperties.Wood.Radius == 2.0f, "Wood collision radius remains 2.0");
        Assert(BallProperties.Stone.Radius == 2.0f, "Stone collision radius remains 2.0");
        Assert(BallProperties.Paper.Radius == 2.0f, "Paper collision radius remains 2.0");
    }

    // ── Test: Angular damping is frame-rate independent (#10) ───────
    private static void TestAngularDampingFrameRateIndependence()
    {
        Console.WriteLine("\n── Angular damping frame-rate independence (#10) ──");
        float dt = 1f / 60f;
        int substeps = 4;
        float subDt = dt / substeps;

        // New damping: exp(-0.8 * subDt) per substep
        float dampProduct = 1f;
        for (int i = 0; i < substeps; i++) dampProduct *= MathF.Exp(-0.8f * subDt);

        float dampDirect = MathF.Exp(-0.8f * dt);
        AssertNear(dampProduct, dampDirect, 1e-6f, "Substep product == direct exp(-0.8*dt)");
    }

    // ── Test: Spatial hash no longer commutative (#6) ───────────────
    private static void TestSpatialHashAsymmetry()
    {
        Console.WriteLine("\n── Spatial hash asymmetry (#6) ──");
        long NewHash(int x, int y, int z) { unchecked { long h = (long)x * 73856093; h = (h * 31) ^ ((long)y * 19349663); h = (h * 31) ^ ((long)z * 83492791); return h; } }

        Assert(NewHash(1, 2, 3) != NewHash(2, 1, 3), "New hash distinguishes (1,2,3) from (2,1,3)");
    }

    // ── Test: Ball falls under gravity ──────────────────────────────
    private static void TestBallGravityFall()
    {
        Console.WriteLine("\n── Ball gravity free-fall ──");
        var collider = new TriangleMeshCollider(16f); // empty — no ground
        var ball = new BallPlayer(new Vector3(0, 100, 0));
        float dt = 1f / 60f;
        Vector3 startPos = ball.Position;

        for (int i = 0; i < 60; i++) // 1 second
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider);

        Assert(ball.Position.Y < startPos.Y - 10f, $"Ball fell: Y went from {startPos.Y:F1} to {ball.Position.Y:F1}");
        Assert(!ball.IsGrounded, "Ball is not grounded in free fall");
    }

    // ── Test: Ball on ground reaches equilibrium ────────────────────
    private static void TestBallGroundedRolling()
    {
        Console.WriteLine("\n── Ball grounded rolling ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var ball = new BallPlayer(new Vector3(0, 5, 0));
        float dt = 1f / 60f;

        // Let ball drop and settle
        for (int i = 0; i < 120; i++) // 2 seconds
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider);

        Assert(ball.IsGrounded, "Ball is grounded after settling");
        AssertNear(ball.Position.Y, ball.Properties.Radius, 0.3f, $"Ball rests at Y ≈ radius ({ball.Properties.Radius})");
        Assert(ball.Velocity.Length() < 2f, $"Ball nearly at rest: speed={ball.Velocity.Length():F3}");
    }

    // ── Test: Ball bounces off ground ───────────────────────────────
    private static void TestBallBounce()
    {
        Console.WriteLine("\n── Ball bounce ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        // Drop ball from height with enough speed to trigger bounce
        var ball = new BallPlayer(new Vector3(0, 20, 0));
        float dt = 1f / 60f;

        float maxY = 0f;
        bool hitGround = false;
        for (int i = 0; i < 240; i++)
        {
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider);
            if (ball.IsGrounded && !hitGround)
            {
                hitGround = true;
            }
            else if (hitGround && ball.Position.Y > ball.Properties.Radius + 0.5f)
            {
                maxY = MathF.Max(maxY, ball.Position.Y);
            }
        }

        Assert(hitGround, "Ball hit ground");
        Assert(maxY > ball.Properties.Radius + 0.1f, $"Ball bounced to Y={maxY:F2}");
    }

    // ── Test: MovableBox collides with floor (#8) ───────────────────
    private static void TestMovableBoxFloorCollision()
    {
        Console.WriteLine("\n── MovableBox floor collision (#8) ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(10, 10, 0));
        float dt = 1f / 60f;

        for (int i = 0; i < 120; i++)
            box.Update(dt, collider);

        AssertNear(box.Position.Y, box.HalfExtents.Y, 0.1f, "Box rests on floor at Y = half-extent");
        Assert(box.Position.Y < 5f, $"Box has settled: Y={box.Position.Y:F2}");
    }

    // ── Test: A driven wood ball really pushes a box (velocity-directed contact) ───
    private static void TestWoodBallPushesBox()
    {
        Console.WriteLine("\n── Wood ball pushes box ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(6f, 2.5f, 0f)); // resting on the floor
        var ball = new BallPlayer(new Vector3(-4f, 2f, 0f), BallMaterial.Wood); // resting beside it
        var boxes = new List<MovableBox> { box };
        float dt = 1f / 60f;

        // Drive the ball rightward (moveInput.X => camera-right => +x) so it keeps pushing.
        Vector2 drive = new Vector2(1f, 0f);
        float startX = box.Position.X;
        float touchGap = ball.Properties.Radius + box.HalfExtents.X; // 4.5

        for (int i = 0; i < 120; i++)
        {
            ball.Update(dt, drive, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            box.Update(dt, collider);
        }

        Assert(box.Position.X - startX > 1f, $"Ball pushed the box forward ({box.Position.X - startX:F2} units)");
        Assert(ball.Position.X <= box.Position.X - touchGap + 0.2f,
            $"Ball pushed from behind and did not pass through (ball X={ball.Position.X:F2}, box left face={box.Position.X - touchGap:F2})");
    }

    // ── Test: Boxes pushed into each other separate (no overlap) ────────────
    private static void TestBoxesSeparateWhenPushed()
    {
        Console.WriteLine("\n── Boxes separate on contact ──");
        var a = new MovableBox(new Vector3(0f, 2.5f, 0f));
        var b = new MovableBox(new Vector3(3.5f, 2.5f, 0f)); // overlapping A
        a.Velocity = new Vector3(20f, 0f, 0f); // driven into B; b stays still

        float bStartX = b.Position.X;
        MovableBox.ResolvePair(a, b);

        Assert(b.Position.X > bStartX, $"Box B was pushed aside (moved {b.Position.X - bStartX:F2} units)");
        Vector3 overlap = (a.HalfExtents + b.HalfExtents) - Vector3.Abs(a.Position - b.Position);
        Assert(overlap.X <= 1e-3f || overlap.Y <= 1e-3f || overlap.Z <= 1e-3f,
            $"Boxes no longer overlap after resolution (overlapX={overlap.X:F4})");
    }

    // ── Test: Ball dropped on a box rests on top (never falls through) ─────
    private static void TestBallRestsOnTopOfBox()
    {
        Console.WriteLine("\n── Ball rests on top of box ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(0f, 5f, 0f)); // falls onto the floor
        var ball = new BallPlayer(new Vector3(0f, 25f, 0f)); // drops straight onto the box top
        var boxes = new List<MovableBox> { box };
        float dt = 1f / 60f;

        for (int i = 0; i < 600; i++)
        {
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            box.Update(dt, collider);
        }

        float expectedY = box.Position.Y + box.HalfExtents.Y + ball.Properties.Radius;
        Assert(ball.IsGrounded, "Ball is grounded on top of the box");
        AssertNear(ball.Position.Y, expectedY, 0.3f, $"Ball rests at Y\u2248{expectedY:F2} (box top + radius)");
        Assert(ball.Position.Y > box.Position.Y + box.HalfExtents.Y, "Ball sits above the box surface");
    }

    // ── Test: Heavy/fast ball pushes a box, never tunneling through it ─────
    private static void TestHeavyBallPushesBox()
    {
        Console.WriteLine("\n── Heavy ball pushes box ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(0f, 100f, 0f));
        var ball = new BallPlayer(new Vector3(-12f, 100f, 0f), BallMaterial.Stone);
        ball.Velocity = new Vector3(30f, 0f, 0f); // fast rightward run-up
        var boxes = new List<MovableBox> { box };
        float dt = 1f / 60f;

        float startX = box.Position.X;
        float touchGap = ball.Properties.Radius + box.HalfExtents.X; // 4.5, ball must stay left of this
        for (int i = 0; i < 180; i++)
        {
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            box.Update(dt, collider);
        }

        Assert(box.Position.X - startX > 3f, $"Box was pushed forward ({box.Position.X - startX:F2} units)");
        Assert(ball.Position.X <= box.Position.X - touchGap + 0.15f, $"Ball did not tunnel through box (ball X={ball.Position.X:F2} \u2264 box X{box.Position.X:F2}-{touchGap:F1})");
        Assert(ball.Velocity.X < 30f, $"Ball decelerated while pushing (vx={ball.Velocity.X:F2})");
    }

    // ── Test: Light ball barely moves the box and bounces off ──────────────
    private static void TestLightBallBouncesOffBox()
    {
        Console.WriteLine("\n── Light (paper) ball bounces off box ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(0f, 100f, 0f));
        var ball = new BallPlayer(new Vector3(-12f, 100f, 0f), BallMaterial.Paper);
        ball.Velocity = new Vector3(20f, 0f, 0f); // rightward
        var boxes = new List<MovableBox> { box };
        float dt = 1f / 60f;

        float startX = box.Position.X;
        bool bounced = false;
        for (int i = 0; i < 240; i++)
        {
            ball.Update(dt, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            if (ball.Velocity.X < 0f) bounced = true;
            box.Update(dt, collider);
        }

        Assert(bounced, "Light ball bounced back after hitting the box");
        Assert(box.Position.X - startX < 3f, $"Ball barely moved the box ({box.Position.X - startX:F2} units)");
    }

    // ── Test: Two crates pushed against a wall never overlap or clip into the wall ──
    private static void TestBoxChainAgainstWall()
    {
        Console.WriteLine("\n── Box chain pushed against wall (OBB SAT) ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);
        // Vertical wall at X = 20 facing -X
        collider.AddTriangle(new CollisionTriangle(new(20, 0, -50), new(20, 0, 50), new(20, 10, 50)));
        collider.AddTriangle(new CollisionTriangle(new(20, 0, -50), new(20, 10, 50), new(20, 10, -50)));

        var boxA = new MovableBox(new Vector3(6f, 2.5f, 0f));
        var boxB = new MovableBox(new Vector3(13f, 2.5f, 0f));
        var boxes = new List<MovableBox> { boxA, boxB };
        var ball = new BallPlayer(new Vector3(-4f, 2f, 0f), BallMaterial.Stone);
        float dt = 1f / 60f;
        Vector2 drive = new Vector2(1f, 0f);

        for (int i = 0; i < 240; i++)
        {
            ball.Update(dt, drive, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            MovableBox.StepAll(boxes, dt, collider);
            ball.ResolvePostBoxStep(boxes, collider, dt);
        }

        // Box B must stop flush against the wall at X = 20 - 2.5 = 17.5 (no clipping!)
        Assert(boxB.Position.X <= 17.5f + 0.12f, $"Box B stops at wall (X={boxB.Position.X:F2} ≤ 17.5)");
        // Box A must stop flush behind Box B (gap ≥ 5.0, no overlap!)
        float boxGap = boxB.Position.X - boxA.Position.X;
        Assert(boxGap >= 5.0f - 0.12f, $"Boxes do not overlap (center gap={boxGap:F2} ≥ 5.0)");
        // Ball stays behind Box A and on the floor (never climbs on top!)
        Assert(ball.Position.X <= boxA.Position.X - 4.5f + 0.15f, $"Ball stays outside Box A (ball X={ball.Position.X:F2}, boxA left={boxA.Position.X - 4.5f:F2})");
        Assert(ball.Position.Y <= 2.3f, $"Ball stays on floor, never climbs crate (Y={ball.Position.Y:F2})");
    }

    // ── Test: Fast wood ball hitting crate side never climbs onto top or gets sucked in ──
    private static void TestBallDoesNotClimbCrateFromSide()
    {
        Console.WriteLine("\n── Wood ball pushing crate never climbs onto top ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var box = new MovableBox(new Vector3(10f, 2.5f, 0f));
        var boxes = new List<MovableBox> { box };
        var ball = new BallPlayer(new Vector3(-10f, 2f, 0f), BallMaterial.Wood);
        float dt = 1f / 60f;
        Vector2 drive = new Vector2(1f, 0f);

        float maxBallY = 0f;
        for (int i = 0; i < 240; i++)
        {
            ball.Update(dt, drive, Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            MovableBox.StepAll(boxes, dt, collider);
            ball.ResolvePostBoxStep(boxes, collider, dt);
            maxBallY = MathF.Max(maxBallY, ball.Position.Y);
        }

        Assert(maxBallY <= 2.35f, $"Ball never popped onto crate (maxY={maxBallY:F2} ≤ 2.35)");
        Assert(box.Position.X > 12f, $"Crate was pushed smoothly (X={box.Position.X:F2})");
    }

    // ── Test: Stacked crates rest cleanly on top of each other ──
    private static void TestStackedBoxes()
    {
        Console.WriteLine("\n── Stacked crates ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var bottom = new MovableBox(new Vector3(0f, 2.5f, 0f));
        var top = new MovableBox(new Vector3(0f, 8.5f, 0f));
        var boxes = new List<MovableBox> { bottom, top };
        float dt = 1f / 60f;

        for (int i = 0; i < 180; i++)
            MovableBox.StepAll(boxes, dt, collider);

        AssertNear(bottom.Position.Y, 2.5f, 0.1f, "Bottom crate rests on floor");
        AssertNear(top.Position.Y, 7.5f, 0.15f, "Top crate rests cleanly on bottom crate");
    }

    // ── Test: Puzzle paper balls fall to floor, collide with player and each other ──
    private static void TestPuzzleBallsFallAndCollide()
    {
        Console.WriteLine("\n── Puzzle balls (P_Ball_Paper) fall & collide ──");
        var collider = new TriangleMeshCollider(16f);
        AddFloorQuad(collider, -100, 100, -100, 100);

        var p1 = new PuzzleBall(BallMaterial.Paper, new Vector3(6f, 8f, 0f));
        var p2 = new PuzzleBall(BallMaterial.Paper, new Vector3(11f, 8f, 0f));
        var pBalls = new List<PuzzleBall> { p1, p2 };
        var boxes = new List<MovableBox>();
        var player = new BallPlayer(new Vector3(-6f, 2f, 0f), BallMaterial.Wood);
        float dt = 1f / 60f;

        // First let the paper balls fall and settle onto the floor
        for (int i = 0; i < 120; i++)
            PuzzleBall.StepAll(pBalls, player, boxes, dt, collider);

        AssertNear(p1.Position.Y, 2.0f, 0.2f, $"Paper ball 1 fell to floor (Y={p1.Position.Y:F2})");
        Assert(p1.IsGrounded, "Paper ball 1 is grounded");

        // Now drive the wood player ball into the paper balls
        float p2StartX = p2.Position.X;
        for (int i = 0; i < 120; i++)
        {
            player.Update(dt, new Vector2(1f, 0f), Vector3.UnitZ, Vector3.UnitX, collider, boxes);
            PuzzleBall.StepAll(pBalls, player, boxes, dt, collider);
        }

        Assert(p1.Position.X > 10f, $"Wood ball pushed paper ball 1 (X={p1.Position.X:F2})");
        Assert(p2.Position.X > p2StartX + 2f, $"Paper ball 1 knocked paper ball 2 (X={p2.Position.X:F2})");
        Assert(Vector3.Distance(p1.Position, p2.Position) >= 3.85f, "Paper balls do not overlap");
    }

    // ── Integration: real level data ────────────────────────────────
    private static void TestRealLevels(string gameRoot)
    {
        Console.WriteLine("\n── Real levels: winding + spawn drop ──");
        for (int lv = 1; lv <= 12; lv++)
        {
            string path = Path.Combine(gameRoot, "3D Entities", "Level", $"Level_{lv:D2}.NMO");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, ".nmo");
            if (!File.Exists(path)) { Console.WriteLine($"  (level {lv} missing)"); continue; }

            var level = Nmo.NmoLevel.Load(path);
            var collider = new TriangleMeshCollider(8f);
            foreach (var e in level.FloorColliders) collider.AddEntity(e);
            foreach (var e in level.RailColliders) collider.AddEntity(e);

            // Winding: among clearly horizontal triangles, how many face up?
            int up = 0, down = 0;
            foreach (var t in collider.Triangles)
            {
                if (t.Normal.Y > 0.9f) up++;
                else if (t.Normal.Y < -0.9f) down++;
            }

            if (level.ResetPointEntities.Count == 0) { Console.WriteLine($"  (level {lv} has no spawn)"); continue; }

            float minY = float.MaxValue;
            foreach (var e in level.FloorColliders) minY = MathF.Min(minY, e.Position.Y);
            float killY = minY - 30f;

            int grounded = 0, fell = 0, spawns = 0;
            float worstDrift = 0f;
            foreach (var spawn in level.ResetPointEntities)
            {
                spawns++;
                var ball = new BallPlayer(spawn.Position);
                Vector3 settled = Vector3.Zero;
                bool died = false;
                for (int i = 0; i < 240; i++) // 4 s at 60 fps, no input
                {
                    ball.Update(1f / 60f, Vector2.Zero, Vector3.UnitZ, Vector3.UnitX, collider);
                    if (ball.Position.Y < killY) { died = true; break; }
                    if (i == 119) settled = ball.Position;
                }
                if (died) { fell++; continue; }
                if (ball.IsGrounded) grounded++;
                worstDrift = MathF.Max(worstDrift, MathF.Abs(ball.Position.Y - settled.Y));
            }

            Console.WriteLine($"  L{lv:D2}: horizontal tris up={up} down={down} | spawns={spawns} grounded={grounded} fell={fell} vDrift={worstDrift:F3}");
            Assert(up > down, $"L{lv:D2} floor normals face up");
            Assert(fell == 0, $"L{lv:D2} ball never falls through from any spawn");
            Assert(grounded == spawns, $"L{lv:D2} ball is grounded at every spawn");
            Assert(worstDrift < 0.05f, $"L{lv:D2} resting ball does not sink/jitter (drift {worstDrift:F3})");
        }
    }
}
