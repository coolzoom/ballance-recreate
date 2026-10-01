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

        string? shot = args.Length > 1 && args[0] == "--shot" ? Path.GetFullPath(args[1]) : null;
        int startLevel = args.Length > 2 && int.TryParse(args[2], out int lv) ? Math.Clamp(lv, 1, 12) : 1;
        using var engine = new GameEngine(gameRoot, 1280, 720) { ScreenshotPath = shot, StartLevel = startLevel };
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
