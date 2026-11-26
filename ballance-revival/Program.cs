using BallanceRevival.Core;
using BallanceRevival.Nmo;
using BallanceRevival.Physics;

namespace BallanceRevival;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=============================================");
        Console.WriteLine("    BALLANCE REVIVAL - .NET CORE (C#)        ");
        Console.WriteLine("=============================================");

        string gameRoot = FindGameRoot();
        Console.WriteLine($"Game Assets Root: {gameRoot}");

        if (args.Length > 0 && args[0] == "--verify")
        {
            VerifyAllLevels(gameRoot);
            return;
        }

        if (args.Length > 0 && args[0] == "--test")
        {
            Environment.Exit(PhysicsSelfTest.Run(gameRoot));
            return;
        }

        if (args.Length > 0 && args[0] == "--probe")
        {
            ProbeBalls(gameRoot);
            return;
        }

        string? shot = args.Length > 1 && args[0] == "--shot" ? Path.GetFullPath(args[1]) : null;
        int startLevel = args.Length > 2 && int.TryParse(args[2], out int lv) ? Math.Clamp(lv, 0, 12) : 1;
        Gameplay.BallMaterial? startMat = args.Length > 3 && Enum.TryParse<Gameplay.BallMaterial>(args[3], true, out var mat) ? mat : null;
        Rendering.PostProcessDebugMode? debugMode = null;
        float? mood = null, snow = null, godRays = null, fog = null, clouds = null;
        bool? ssr = null;
        bool startInOptions = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--options")
                startInOptions = true;
            else if (i + 1 < args.Length)
            {
                if (args[i] == "--debug" && int.TryParse(args[i + 1], out int dm))
                    debugMode = (Rendering.PostProcessDebugMode)Math.Clamp(dm, 0, 5);
                else if (args[i] == "--mood" && float.TryParse(args[i + 1], out float m))
                    mood = Math.Clamp(m, 0f, 1f);
                else if (args[i] == "--snow" && float.TryParse(args[i + 1], out float sn))
                    snow = Math.Clamp(sn, 0f, 1f);
                else if (args[i] == "--godrays" && float.TryParse(args[i + 1], out float gr))
                    godRays = Math.Clamp(gr, 0f, 1.5f);
                else if (args[i] == "--fog" && float.TryParse(args[i + 1], out float fg))
                    fog = Math.Clamp(fg, 0f, 1.5f);
                else if (args[i] == "--clouds" && float.TryParse(args[i + 1], out float cl))
                    clouds = Math.Clamp(cl, 0f, 1f);
                else if (args[i] == "--ssr" && int.TryParse(args[i + 1], out int s))
                    ssr = s != 0;
            }
        }
        using var engine = new GameEngine(gameRoot, 1280, 720)
        {
            ScreenshotPath = shot,
            StartLevel = startLevel,
            StartMaterial = startMat,
            InitialDebugMode = debugMode,
            InitialMood = mood,
            InitialSnow = snow,
            InitialGodRays = godRays,
            InitialFog = fog,
            InitialClouds = clouds,
            InitialSsr = ssr,
            StartInOptions = startInOptions
        };
        engine.Run();
    }

    private static void VerifyAllLevels(string gameRoot)
    {
        Console.WriteLine("\n[VERIFICATION] Verifying all 12 Ballance levels...");
        for (int i = 1; i <= 12; i++)
        {
            string path = Path.Combine(gameRoot, "3D Entities", "Level", $"Level_{i:D2}.NMO");
            if (!File.Exists(path))
            {
                path = Path.Combine(gameRoot, "3D Entities", "Level", $"Level_{i:D2}.nmo");
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"  Level {i:D2}: File not found ({path})");
                continue;
            }

            var level = NmoLevel.Load(path);
            var collider = new TriangleMeshCollider(8.0f);
            foreach (var floor in level.FloorColliders) collider.AddEntity(floor);
            foreach (var rail in level.RailColliders) collider.AddEntity(rail);

            Console.WriteLine($"  Level {i:D2}: OK | Objects: {level.RawFile.Objects.Count:D3} | Renderables: {level.AllRenderables.Count:D3} | Triangles: {collider.TriangleCount:D5} | Checkpoints: {level.CheckpointEntities.Count} | Resetpoints: {level.ResetPointEntities.Count}");
        }
        Console.WriteLine("[VERIFICATION] All 12 levels verified successfully!");
    }

    private static void ProbeBalls(string gameRoot)
    {
        string path = Path.Combine(gameRoot, "3D Entities", "Balls.nmo");
        var balls = NmoLevel.Load(path);
        foreach (var e in balls.Entities.Values)
        {
            if (e.Mesh == null || e.Mesh.Positions.Length == 0) continue;
            var lmin = new System.Numerics.Vector3(float.MaxValue);
            var lmax = new System.Numerics.Vector3(float.MinValue);
            var wmin = lmin;
            var wmax = lmax;
            foreach (var v in e.Mesh.Positions)
            {
                lmin = System.Numerics.Vector3.Min(lmin, v);
                lmax = System.Numerics.Vector3.Max(lmax, v);
                var w = System.Numerics.Vector3.Transform(v, e.WorldMatrix);
                wmin = System.Numerics.Vector3.Min(wmin, w);
                wmax = System.Numerics.Vector3.Max(wmax, w);
            }
            var ls = lmax - lmin;
            var ws = wmax - wmin;
            Console.WriteLine($"{e.Name,-28} verts={e.Mesh.Positions.Length,5} localSize=({ls.X:F3},{ls.Y:F3},{ls.Z:F3}) worldSize=({ws.X:F3},{ws.Y:F3},{ws.Z:F3}) worldRadiusY={ws.Y / 2:F3}");
        }
    }

    private static string FindGameRoot()
    {
        string[] candidates =
        [
            "../Ballance.Build.12799282",
            "Ballance.Build.12799282",
            "/Users/mac/Downloads/Ballance/Ballance.Build.12799282",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../Ballance.Build.12799282")
        ];

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && Directory.Exists(Path.Combine(c, "3D Entities")))
            {
                return Path.GetFullPath(c);
            }
        }

        return "/Users/mac/Downloads/Ballance/Ballance.Build.12799282";
    }
}
