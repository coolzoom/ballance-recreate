using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>Single directional light + ambient, approximating Virtools' fixed-function vertex lighting.</summary>
public static class LitShader
{
    private const string VertexSource = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matModel;
        uniform mat4 matNormal;
        out vec2 fragTexCoord;
        out vec3 fragNormal;
        out vec3 fragWorld;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            fragWorld = vec3(matModel * vec4(vertexPosition, 1.0));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        in vec3 fragNormal;
        in vec3 fragWorld;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 viewPos;
        uniform vec3 fogColor;
        uniform float fogDensity;
        out vec4 finalColor;
        const vec3 lightDir = normalize(vec3(-0.35, 0.9, -0.25));
        void main()
        {
            vec4 texel = texture(texture0, fragTexCoord) * colDiffuse;
            if (texel.a < 0.04) discard;
            // Virtools lights the levels almost flat, with a soft overhead key
            float diffuse = max(dot(normalize(fragNormal), lightDir), 0.0);
            vec3 lit = texel.rgb * (0.72 + 0.38 * diffuse);
            float dist = length(fragWorld - viewPos);
            float fog = 1.0 - exp(-fogDensity * dist);
            finalColor = vec4(mix(lit, fogColor, clamp(fog, 0.0, 0.82)), texel.a);
        }
        """;

    public static Shader Shader { get; private set; }
    public static int ViewPosLoc { get; private set; }
    public static int FogColorLoc { get; private set; }
    public static int FogDensityLoc { get; private set; }

    public static void Load()
    {
        Shader = Raylib.LoadShaderFromMemory(VertexSource, FragmentSource);
        ViewPosLoc = Raylib.GetShaderLocation(Shader, "viewPos");
        FogColorLoc = Raylib.GetShaderLocation(Shader, "fogColor");
        FogDensityLoc = Raylib.GetShaderLocation(Shader, "fogDensity");
    }

    public static void SetFog(Vector3 viewPos, Vector3 fogColor, float density)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, ViewPosLoc, viewPos, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, FogColorLoc, fogColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, FogDensityLoc, density, ShaderUniformDataType.Float);
    }

    public static void Unload()
    {
        if (Shader.Id != 0) Raylib.UnloadShader(Shader);
        Shader = default;
    }
}
