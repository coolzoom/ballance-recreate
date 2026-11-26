using System.Text.Json;
using System.Text.Json.Serialization;
using BallanceRevival.Rendering;

namespace BallanceRevival.Core;

/// <summary>
/// Persistent graphics & post-processing settings shared between the in-game Options menu,
/// live debug hotkeys (F1-F6), and the rendering pipeline.
/// </summary>
public sealed class GraphicsSettings
{
    private static readonly float[] BloomSteps = [0f, 0.45f, 0.80f, 1.15f];
    private static readonly float[] SnowSteps = [0f, 0.50f, 0.90f];
    private static readonly float[] GodRaySteps = [0f, 0.45f, 0.95f];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public float AtmosphereMood { get; set; } = 0f;
    public float BloomIntensity { get; set; } = 0.45f;
    public float SnowIntensity { get; set; } = 0f;
    public float GodRaysIntensity { get; set; } = 0.45f;
    public bool EnableSsr { get; set; } = true;
    public PostProcessDebugMode DebugMode { get; set; } = PostProcessDebugMode.None;

    public static string SettingsFilePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = AppContext.BaseDirectory;
            return Path.Combine(appData, "BallanceRevival", "settings.json");
        }
    }

    public static GraphicsSettings Load()
    {
        try
        {
            string path = SettingsFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<GraphicsSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.Clamp();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Failed to load settings: {ex.Message}");
        }

        return new GraphicsSettings();
    }

    public void Save()
    {
        try
        {
            Clamp();
            string path = SettingsFilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Failed to save settings: {ex.Message}");
        }
    }

    public void Clamp()
    {
        AtmosphereMood = Math.Clamp(AtmosphereMood, 0f, 1f);
        BloomIntensity = Math.Clamp(BloomIntensity, 0f, 1.5f);
        SnowIntensity = Math.Clamp(SnowIntensity, 0f, 1f);
        GodRaysIntensity = Math.Clamp(GodRaysIntensity, 0f, 1.5f);
        if (!Enum.IsDefined(DebugMode))
            DebugMode = PostProcessDebugMode.None;
    }

    public void ResetDefaults()
    {
        AtmosphereMood = 0f;
        BloomIntensity = 0.45f;
        SnowIntensity = 0f;
        GodRaysIntensity = 0.45f;
        EnableSsr = true;
        DebugMode = PostProcessDebugMode.None;
    }

    public void ToggleAtmosphere() => AtmosphereMood = AtmosphereMood > 0.5f ? 0f : 1f;
    public void CycleBloom(int dir = 1) => BloomIntensity = StepValue(BloomIntensity, BloomSteps, dir);
    public void CycleSnow(int dir = 1) => SnowIntensity = StepValue(SnowIntensity, SnowSteps, dir);
    public void CycleGodRays(int dir = 1) => GodRaysIntensity = StepValue(GodRaysIntensity, GodRaySteps, dir);
    public void ToggleSsr() => EnableSsr = !EnableSsr;

    public void CycleDebugMode(int dir = 1)
    {
        int count = 5;
        int next = ((int)DebugMode + dir + count) % count;
        DebugMode = (PostProcessDebugMode)next;
    }

    public string AtmosphereLabel => AtmosphereMood > 0.5f ? "Night" : "Classic";
    public string BloomLabel => BloomIntensity < 0.05f ? "Off" : $"{BloomIntensity * 100f:0}%";
    public string SnowLabel => SnowIntensity < 0.05f ? "Off" : $"{SnowIntensity * 100f:0}%";
    public string GodRaysLabel => GodRaysIntensity < 0.05f ? "Off" : $"{GodRaysIntensity * 100f:0}%";
    public string SsrLabel => EnableSsr ? "On" : "Off";

    public string DebugModeLabel => DebugMode switch
    {
        PostProcessDebugMode.None => "Off",
        PostProcessDebugMode.LinearDepth => "Depth",
        PostProcessDebugMode.WorldNormal => "Normal",
        PostProcessDebugMode.WorldPosition => "WorldPos",
        PostProcessDebugMode.BloomOnly => "Bloom",
        _ => "Off"
    };

    private static float StepValue(float current, float[] steps, int dir)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < steps.Length; i++)
        {
            float d = MathF.Abs(current - steps[i]);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        int next = (best + dir + steps.Length) % steps.Length;
        return steps[next];
    }
}
