using System.Numerics;

namespace BallanceRevival.Gameplay;

public enum BallMaterial
{
    Wood,
    Stone,
    Paper
}

public class BallProperties
{
    public BallMaterial Material { get; init; }
    public string Name { get; init; } = "Wood";
    public float Radius { get; init; } = 2.0f;
    public float Mass { get; init; } = 1.9f;
    /// <summary>Player push, in units per second squared. Heavier balls get less of this.</summary>
    public float Acceleration { get; init; } = 36.0f;
    public float MaxSpeed { get; init; } = 36.0f;
    /// <summary>How fast a free roll slows down, per second. Paper sheds speed; stone keeps it.</summary>
    public float LinearDamp { get; init; } = 0.2f;
    /// <summary>Rolling resistance on the ground, in units per second squared.</summary>
    public float RollingResistance { get; init; } = 6.0f;
    public float Restitution { get; init; } = 0.25f;
    public float SlopeClimbLimit { get; init; } = 0.7f;
    public bool CanBeLiftedByWind { get; init; } = false;
    public string TextureName { get; init; } = "Ball_Wood.bmp";
    public string MeshName { get; init; } = "Ball_Wood_Mesh";

    public static BallProperties Wood => new()
    {
        Material = BallMaterial.Wood,
        Name = "Wood Ball",
        Radius = 2.0f,
        Mass = 1.9f,
        Acceleration = 36.0f,
        MaxSpeed = 36.0f,
        LinearDamp = 0.22f,
        RollingResistance = 5.5f,
        Restitution = 0.28f,
        SlopeClimbLimit = 0.75f,
        CanBeLiftedByWind = false,
        TextureName = "Ball_Wood.bmp",
        MeshName = "Ball_Wood_Mesh"
    };

    public static BallProperties Stone => new()
    {
        Material = BallMaterial.Stone,
        Name = "Stone Ball",
        Radius = 2.0f,
        Mass = 10.0f,
        Acceleration = 14.0f,
        MaxSpeed = 52.0f,
        LinearDamp = 0.06f,
        RollingResistance = 2.0f,
        Restitution = 0.05f,
        SlopeClimbLimit = 0.42f,
        CanBeLiftedByWind = false,
        TextureName = "Ball_Stone.bmp",
        MeshName = "Ball_Stone_HighRes_Mesh"
    };

    public static BallProperties Paper => new()
    {
        Material = BallMaterial.Paper,
        Name = "Paper Ball",
        Radius = 2.0f,
        Mass = 0.2f,
        Acceleration = 55.0f,
        MaxSpeed = 26.0f,
        LinearDamp = 0.9f,
        RollingResistance = 9.0f,
        Restitution = 0.12f,
        SlopeClimbLimit = 0.95f,
        CanBeLiftedByWind = true,
        TextureName = "Ball_Paper.bmp",
        MeshName = "Ball_Paper_Mesh"
    };

    public static BallProperties ForMaterial(BallMaterial mat) => mat switch
    {
        BallMaterial.Stone => Stone,
        BallMaterial.Paper => Paper,
        _ => Wood
    };
}
