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
        uniform vec4 surface;        // x: specStrength, y: specPower, z: receivesBallShadow, w: >0.5 sphere env-map / <-0.5 self-lit
        uniform float atmosphereMood;// 0.0 = Classic Vivid, 1.0 = Moody / Night High-Contrast
        uniform float snowIntensity; // 0.0 ~ 1.0 Dynamic Y-Up Snow Accumulation (Stage 2)
        uniform int lightCount;
        uniform vec4 lightPos[MAX_LIGHTS];   // xyz, radius
        uniform vec3 lightColor[MAX_LIGHTS];
        layout(location = 0) out vec4 finalColor;
        layout(location = 1) out vec4 materialData; // MRT G-Buffer: r=SSR reflectMask, g=roughness, b=snowMask, a=1

        // Lightweight procedural 2D hash & value noise for organic snow edges & ice crystal glints
        float hash21(vec2 p)
        {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        float valueNoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash21(i);
            float b = hash21(i + vec2(1.0, 0.0));
            float c = hash21(i + vec2(0.0, 1.0));
            float d = hash21(i + vec2(1.0, 1.0));
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }

        float ballShadow()
        {
            if (surface.z < 0.5 || ballPos.w <= 0.0) return 0.0;
            float h = ballPos.y - fragWorld.y;
            if (h < -0.2 || h > 20.0) return 0.0;
            float d = length(fragWorld.xz - ballPos.xz);
            // Tighter shadow at the contact point, spreading as height increases
            float r = ballPos.w * (0.7 + h * 0.05);
            float soft = ballPos.w * (0.2 + h * 0.1);
            float s = 1.0 - smoothstep(r - soft, r + soft, d);
            // Faster vertical decay for a more grounded look
            return s * (1.0 - smoothstep(2.0, 15.0, h)) * 0.8;
        }

        vec3 softClip(vec3 c)
        {
            const float k = 0.82;
            vec3 over = max(c - k, 0.0);
            return min(c, vec3(k)) + (1.0 - k) * (1.0 - exp(-over / (1.0 - k)));
        }

        void main()
        {
            vec3 n = normalize(fragNormal);
            if (!gl_FrontFacing) n = -n;
            vec3 toView = viewPos - fragWorld;
            float dist = length(toView);
            vec3 v = toView / max(dist, 1e-4);

            bool isEnvMap = surface.w > 0.5;
            bool isEmissive = surface.w < -0.5;

            vec4 texel;
            if (isEnvMap)
            {
                // Virtools spherical environment mapping (Direct3D8 sphere-map reflection in view space)
                vec3 camRight = normalize(cross(vec3(0.0, 1.0, 0.0), v));
                vec3 camUp = cross(v, camRight);
                vec3 r = reflect(-v, n);
                vec3 rv = vec3(dot(r, camRight), dot(r, camUp), dot(r, -v));
                float m = 2.0 * sqrt(rv.x * rv.x + rv.y * rv.y + (rv.z + 1.0) * (rv.z + 1.0) + 1e-4);
                vec2 envUV = vec2(rv.x / m + 0.5, -rv.y / m + 0.5);
                vec3 envCol = texture(texture0, envUV).rgb;
                // Blend chrome reflection with material diffuse tint so steel rails and colored trafos gleam
                vec3 chrome = mix(envCol * colDiffuse.rgb * 1.45, envCol * (0.55 + 0.65 * colDiffuse.rgb), 0.45);
                texel = vec4(chrome, colDiffuse.a);
            }
            else
            {
                texel = texture(texture0, fragTexCoord) * colDiffuse;
            }
            if (texel.a < 0.04) discard;

            float shadow = ballShadow();
            float ndl = dot(n, sunDir);
            bool isBall = surface.z < 0.5 && surface.x > 0.0 && !isEnvMap && !isEmissive;

            // Stage 2: Dynamic Y-Up Snow Accumulation (excludes player ball, emissive lanterns, and alpha decals)
            float snowMask = 0.0;
            if (snowIntensity > 0.001 && !isBall && !isEmissive && texel.a > 0.92)
            {
                float upDot = dot(n, vec3(0.0, 1.0, 0.0));
                float nCoarse = valueNoise(fragWorld.xz * 0.16);
                float nFine = valueNoise(fragWorld.xz * 0.65);
                float edgeNoise = (nCoarse * 0.65 + nFine * 0.35) - 0.5;
                // Surface faces upward when dot(n, +Y) > 0.6 with organic noisy transition
                snowMask = smoothstep(0.56 + edgeNoise * 0.16, 0.78 + edgeNoise * 0.10, upDot) * snowIntensity;
                // Chrome metal rails only catch light frost so they stay recognizably metallic
                if (isEnvMap) snowMask *= 0.30;

                vec3 snowAlbedo = vec3(0.90, 0.93, 0.98);
                texel.rgb = mix(texel.rgb, snowAlbedo, snowMask);
            }

            vec3 light;
            float ambScale = mix(1.0, 0.26, atmosphereMood);
            vec3 moodyTint = mix(vec3(1.0), vec3(0.72, 0.80, 1.12), atmosphereMood);
            float sunScale = mix(1.0, 0.52, atmosphereMood);
            float ptScale = mix(1.15, 2.25, atmosphereMood);

            if (isEmissive)
            {
                // True HDR self-illuminated lantern glass & energy glows (> 1.0 triggers Bloom)
                light = vec3(1.95, 1.82, 1.55);
            }
            else if (isBall)
            {
                // Hero ball lighting: neutral-warm directional shading with deep shadowed underside
                vec3 ballAmbient = mix(vec3(0.16, 0.16, 0.18), mix(vec3(0.54), skyTint * 0.52, 0.35), n.y * 0.5 + 0.5) * ambScale * moodyTint;
                float ballWrap = clamp((ndl + 0.12) / 1.12, 0.0, 1.0);
                light = ballAmbient + mix(vec3(1.0, 0.98, 0.94), sunTint, 0.25) * (0.76 * sunScale * ballWrap);
            }
            else if (isEnvMap)
            {
                // Metallic chrome surfaces keep bright sky/sun reflection contrast
                vec3 envAmbient = mix(groundTint * 0.55, skyTint * 0.92, n.y * 0.5 + 0.5) * 0.78 * ambScale * moodyTint;
                float envWrap = clamp((ndl + 0.25) / 1.25, 0.0, 1.0);
                light = envAmbient * (1.0 - 0.35 * shadow) + sunTint * (0.55 * sunScale * envWrap) * (1.0 - shadow);
            }
            else
            {
                vec3 ambient = mix(groundTint * 0.72, skyTint, n.y * 0.5 + 0.5) * 0.62 * ambScale * moodyTint;
                float wrapped = clamp((ndl + 0.3) / 1.3, 0.0, 1.0);
                light = ambient * (1.0 - 0.45 * shadow) + sunTint * (0.50 * sunScale * wrapped) * (1.0 - shadow);
            }

            vec3 spec = vec3(0.0);
            if (surface.x > 0.0 && ndl > 0.0 && !isEmissive)
            {
                vec3 h = normalize(sunDir + v);
                spec = sunTint * surface.x * (isEnvMap ? 1.45 : 1.0) * pow(max(dot(n, h), 0.0), surface.y) * (1.0 - shadow);
            }
            // Ice crystal micro-glint + cool Fresnel sheen on snow-covered surfaces
            if (snowMask > 0.05 && ndl > 0.0)
            {
                vec3 h = normalize(sunDir + v);
                float sparkle = pow(hash21(floor(fragWorld.xz * 6.5)), 16.0);
                float iceSpec = pow(max(dot(n, h), 0.0), 32.0) * (0.14 + sparkle * 0.75);
                spec += vec3(0.85, 0.93, 1.0) * iceSpec * snowMask * (1.0 - shadow);
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
                float lightScale = (isBall ? 0.42 : 1.0) * ptScale;
                light += lightColor[i] * att * lambert * lightScale;
            }

            // Subtle Fresnel rim on lit edges (stronger metallic sheen on env-mapped chrome & icy snow)
            // Keep true HDR dynamic range (> 1.0) without legacy softClip; ACES Tone Mapping runs in RenderPipeline.
            float fresnel = pow(1.0 - max(dot(n, v), 0.0), isEnvMap ? 2.4 : 3.2) * clamp(ndl * 0.5 + 0.5, 0.0, 1.0);
            vec3 rimColor = mix(mix(skyTint, sunTint, max(dot(n, sunDir), 0.0)), vec3(0.85, 0.94, 1.0), snowMask * 0.7);
            float rimStrength = isEnvMap ? 0.28 : (isBall ? 0.05 : mix(0.08, 0.22, snowMask));
            vec3 color = texel.rgb * light + spec + rimColor * fresnel * rimStrength;

            float fog = 1.0 - exp(-fogDensity * dist);
            float hf = clamp((fogHeight.x - fragWorld.y) / fogHeight.y, 0.0, 1.0);
            fog = 1.0 - (1.0 - fog) * (1.0 - hf * hf * 0.9);
            finalColor = vec4(mix(color, fogColor, clamp(fog, 0.0, 0.9)), texel.a);

            // MRT location 1: SSR reflection mask & roughness for Stage 4B metallic reflections
            float reflectMask = isEnvMap ? (1.0 - snowMask * 0.6) : (isBall ? 0.0 : clamp(surface.x * 0.45, 0.0, 0.35) * (1.0 - snowMask));
            float roughness = isEnvMap ? 0.12 : clamp(1.0 - surface.y / 96.0, 0.25, 0.95);
            materialData = vec4(reflectMask, roughness, snowMask, 1.0);
        }
        """;

    public static Shader Shader { get; private set; }

    private static int _viewPosLoc, _fogColorLoc, _fogDensityLoc, _fogHeightLoc;
    private static int _skyTintLoc, _groundTintLoc, _sunTintLoc, _sunDirLoc;
    public static readonly Vector3 SunDir = Vector3.Normalize(new Vector3(-0.35f, 0.9f, -0.25f));
    private static int _ballPosLoc, _surfaceLoc, _moodLoc, _snowLoc, _lightCountLoc, _lightPosLoc, _lightColorLoc;
    private static Vector4 _surface = new(-1f);

    public static readonly Vector4 DefaultSurface = new(0.08f, 24f, 1f, 0f);

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
        _moodLoc = Raylib.GetShaderLocation(Shader, "atmosphereMood");
        _snowLoc = Raylib.GetShaderLocation(Shader, "snowIntensity");
        _lightCountLoc = Raylib.GetShaderLocation(Shader, "lightCount");
        _lightPosLoc = Raylib.GetShaderLocation(Shader, "lightPos");
        _lightColorLoc = Raylib.GetShaderLocation(Shader, "lightColor");

        Raylib.SetShaderValue(Shader, _sunDirLoc, SunDir, ShaderUniformDataType.Vec3);
        SetEnvironment(Vector3.One, Vector3.One, Vector3.One);
        SetFogHeight(-1e6f, 1f);
        SetBall(Vector3.Zero, 0f);
        SetLights([], [], 0);
        SetSurface(DefaultSurface);
        SetAtmosphereMood(0f);
        SetSnowIntensity(0f);
    }

    public static void SetAtmosphereMood(float mood)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _moodLoc, Math.Clamp(mood, 0f, 1f), ShaderUniformDataType.Float);
    }

    public static void SetSnowIntensity(float intensity)
    {
        if (Shader.Id == 0) return;
        Raylib.SetShaderValue(Shader, _snowLoc, Math.Clamp(intensity, 0f, 1f), ShaderUniformDataType.Float);
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
    public static void SetSurface(Vector4 surface)
    {
        if (Shader.Id == 0 || surface == _surface) return;
        _surface = surface;
        Raylib.SetShaderValue(Shader, _surfaceLoc, surface, ShaderUniformDataType.Vec4);
    }

    public static void Unload()
    {
        if (Shader.Id != 0) Raylib.UnloadShader(Shader);
        Shader = default;
        _surface = new Vector4(-1f);
    }
}
