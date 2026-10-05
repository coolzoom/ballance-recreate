using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Forward lighting for level geometry: sky-tinted hemisphere ambient, a wrapped sun key,
/// flame point lights, a blob shadow under the ball, and distance plus height fog.
/// </summary>
public static class LitShader
{
    public const int MaxLights = 8;

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
            fragNormal = vec3(matNormal * vec4(vertexNormal, 0.0));
            fragWorld = vec3(matModel * vec4(vertexPosition, 1.0));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330
        #define MAX_LIGHTS 8
        in vec2 fragTexCoord;
        in vec3 fragNormal;
        in vec3 fragWorld;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 viewPos;
        uniform vec3 fogColor;
        uniform float fogDensity;
        uniform vec2 fogHeight;      // y where height fog starts, depth over which it saturates
        uniform vec3 skyTint;
        uniform vec3 groundTint;
        uniform vec3 sunTint;
        uniform vec3 sunDir;
        uniform vec4 ballPos;        // xyz, radius
        uniform vec3 surface;        // specular strength, specular power, receives ball shadow
        uniform int lightCount;
        uniform vec4 lightPos[MAX_LIGHTS];   // xyz, radius
        uniform vec3 lightColor[MAX_LIGHTS];
        out vec4 finalColor;

        float ballShadow()
        {
            if (surface.z < 0.5 || ballPos.w <= 0.0) return 0.0;
            float h = ballPos.y - fragWorld.y;
            if (h < -0.2 || h > 40.0) return 0.0;
            float d = length(fragWorld.xz - ballPos.xz);
            float r = ballPos.w * (0.85 + h * 0.025);
            float soft = ballPos.w * (0.35 + h * 0.06);
            float s = 1.0 - smoothstep(r - soft, r + soft, d);
            return s * (1.0 - smoothstep(4.0, 40.0, h)) * 0.75;
        }

        vec3 softClip(vec3 c)
        {
            const float k = 0.82;
            vec3 over = max(c - k, 0.0);
            return min(c, vec3(k)) + (1.0 - k) * (1.0 - exp(-over / (1.0 - k)));
        }

        void main()
        {
            vec4 texel = texture(texture0, fragTexCoord) * colDiffuse;
            if (texel.a < 0.04) discard;

            vec3 n = normalize(fragNormal);
            if (!gl_FrontFacing) n = -n;
            vec3 toView = viewPos - fragWorld;
            float dist = length(toView);
            vec3 v = toView / max(dist, 1e-4);

            float shadow = ballShadow();

            vec3 ambient = mix(groundTint, skyTint, n.y * 0.5 + 0.5) * 0.64;
            float ndl = dot(n, sunDir);
            float wrapped = clamp((ndl + 0.3) / 1.3, 0.0, 1.0);
            vec3 light = ambient * (1.0 - 0.45 * shadow) + sunTint * (0.48 * wrapped) * (1.0 - shadow);

            vec3 spec = vec3(0.0);
            if (surface.x > 0.0 && ndl > 0.0)
            {
                vec3 h = normalize(sunDir + v);
                spec = sunTint * surface.x * pow(max(dot(n, h), 0.0), surface.y) * (1.0 - shadow);
            }

            for (int i = 0; i < MAX_LIGHTS; i++)
            {
                if (i >= lightCount) break;
                vec3 l = lightPos[i].xyz - fragWorld;
                float ld = length(l);
                float att = clamp(1.0 - ld / lightPos[i].w, 0.0, 1.0);
                if (att <= 0.0) continue;
                att *= att;
                float lambert = max(dot(n, l / ld), 0.0) * 0.75 + 0.25;
                light += lightColor[i] * att * lambert;
            }

            float rim = pow(1.0 - max(dot(n, v), 0.0), 4.0);
            vec3 color = texel.rgb * light + spec + skyTint * rim * 0.12;
            color = softClip(color);

            float fog = 1.0 - exp(-fogDensity * dist);
            float hf = clamp((fogHeight.x - fragWorld.y) / fogHeight.y, 0.0, 1.0);
            fog = 1.0 - (1.0 - fog) * (1.0 - hf * hf * 0.9);
            finalColor = vec4(mix(color, fogColor, clamp(fog, 0.0, 0.9)), texel.a);
        }
        """;

    public static Shader Shader { get; private set; }

    private static int _viewPosLoc, _fogColorLoc, _fogDensityLoc, _fogHeightLoc;
    private static int _skyTintLoc, _groundTintLoc, _sunTintLoc, _sunDirLoc;
    private static int _ballPosLoc, _surfaceLoc, _lightCountLoc, _lightPosLoc, _lightColorLoc;
    private static Vector3 _surface = new(-1f);

    public static readonly Vector3 DefaultSurface = new(0.10f, 24f, 1f);

    public static void Load()
    {
        Shader = Raylib.LoadShaderFromMemory(VertexSource, FragmentSource);
        if (Shader.Id == 0) return;
        _viewPosLoc = Raylib.GetShaderLocation(Shader, "viewPos");
        _fogColorLoc = Raylib.GetShaderLocation(Shader, "fogColor");
        _fogDensityLoc = Raylib.GetShaderLocation(Shader, "fogDensity");
        _fogHeightLoc = Raylib.GetShaderLocation(Shader, "fogHeight");
        _skyTintLoc = Raylib.GetShaderLocation(Shader, "skyTint");
        _groundTintLoc = Raylib.GetShaderLocation(Shader, "groundTint");
        _sunTintLoc = Raylib.GetShaderLocation(Shader, "sunTint");
        _sunDirLoc = Raylib.GetShaderLocation(Shader, "sunDir");
        _ballPosLoc = Raylib.GetShaderLocation(Shader, "ballPos");
        _surfaceLoc = Raylib.GetShaderLocation(Shader, "surface");
        _lightCountLoc = Raylib.GetShaderLocation(Shader, "lightCount");
        _lightPosLoc = Raylib.GetShaderLocation(Shader, "lightPos");
        _lightColorLoc = Raylib.GetShaderLocation(Shader, "lightColor");

        Raylib.SetShaderValue(Shader, _sunDirLoc, Vector3.Normalize(new Vector3(-0.35f, 0.9f, -0.25f)), ShaderUniformDataType.Vec3);
        SetEnvironment(Vector3.One, Vector3.One, Vector3.One);
        SetFogHeight(-1e6f, 1f);
        SetBall(Vector3.Zero, 0f);
        SetLights([], [], 0);
        SetSurface(DefaultSurface);
    }

    public static void SetFog(Vector3 viewPos, Vector3 fogColor, float density)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _viewPosLoc, viewPos, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _fogColorLoc, fogColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _fogDensityLoc, density, ShaderUniformDataType.Float);
    }

    public static void SetFogHeight(float startY, float depth)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _fogHeightLoc, new Vector2(startY, MathF.Max(depth, 1f)), ShaderUniformDataType.Vec2);
    }

    /// <summary>Tints are multiplied into albedo; keep them near white so textures stay faithful.</summary>
    public static void SetEnvironment(Vector3 sky, Vector3 ground, Vector3 sun)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _skyTintLoc, sky, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _groundTintLoc, ground, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(Shader, _sunTintLoc, sun, ShaderUniformDataType.Vec3);
    }

    public static void SetBall(Vector3 position, float radius)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _ballPosLoc, new Vector4(position, radius), ShaderUniformDataType.Vec4);
    }

    public static void SetLights(Vector4[] positions, Vector3[] colors, int count)
    {
        if (Shader.Id == 0) return;
        count = Math.Min(count, MaxLights);
        Raylib.SetShaderValue(Shader, _lightCountLoc, count, ShaderUniformDataType.Int);
        if (count == 0) return;
        Raylib.SetShaderValueV(Shader, _lightPosLoc, positions, ShaderUniformDataType.Vec4, count);
        Raylib.SetShaderValueV(Shader, _lightColorLoc, colors, ShaderUniformDataType.Vec3, count);
    }

    /// <summary>Per-draw surface response; skipped when unchanged since every set is a GL call.</summary>
    public static void SetSurface(Vector3 surface)
    {
        if (Shader.Id == 0 || surface == _surface) return;
        _surface = surface;
        Raylib.SetShaderValue(Shader, _surfaceLoc, surface, ShaderUniformDataType.Vec3);
    }

    public static void Unload()
    {
        if (Shader.Id != 0) Raylib.UnloadShader(Shader);
        Shader = default;
        _surface = new Vector3(-1f);
    }
}
