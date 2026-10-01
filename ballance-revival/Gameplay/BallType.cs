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
    public float Mass { get; init; } = 10.0f;
    public float Acceleration { get; init; } = 32.0f;
    public float MaxSpeed { get; init; } = 35.0f;
    public float Friction { get; init; } = 0.88f;
    public float Restitution { get; init; } = 0.25f;
    public float AirDrag { get; init; } = 0.995f;
    public float SlopeClimbLimit { get; init; } = 0.7f;
    public bool CanBeLiftedByWind { get; init; } = false;
    public string TextureName { get; init; } = "Ball_Wood.bmp";
    public string MeshName { get; init; } = "Ball_Wood_Mesh";

    public static BallProperties Wood => new()
    {
        Material = BallMaterial.Wood,
        Name = "Wood Ball",
        Radius = 2.0f,
        Mass = 10.0f,
        Acceleration = 32.0f,
        MaxSpeed = 35.0f,
        Friction = 0.88f,
        Restitution = 0.25f,
        AirDrag = 0.995f,
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
        Mass = 35.0f,
        Acceleration = 20.0f,
        MaxSpeed = 45.0f,
        Friction = 0.92f,
        Restitution = 0.08f,
        AirDrag = 0.998f,
        SlopeClimbLimit = 0.45f,
        CanBeLiftedByWind = false,
        TextureName = "Ball_Stone.bmp",
        MeshName = "Ball_Stone_HighRes_Mesh"
    };

    public static BallProperties Paper => new()
    {
        Material = BallMaterial.Paper,
        Name = "Paper Ball",
        Radius = 2.0f,
        Mass = 1.2f,
        Acceleration = 48.0f,
        MaxSpeed = 30.0f,
        Friction = 0.75f,
        Restitution = 0.15f,
        AirDrag = 0.985f,
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
