using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public enum PostProcessDebugMode
{
    None = 0,
    LinearDepth = 1,
    WorldNormal = 2,
    WorldPosition = 3,
    BloomOnly = 4
}

/// <summary>
/// Offscreen HDR floating-point render target (RGBA16F) with an optional sampleable
/// OpenGL 3.3 depth texture (not a renderbuffer) for screen-space reconstruction and effects.
/// </summary>
public sealed unsafe class PostProcessTarget : IDisposable
{
    public uint FboId { get; private set; }
    public Texture2D ColorTexture { get; private set; }
    public Texture2D MaterialTexture { get; private set; }
    public Texture2D DepthTexture { get; private set; }
    public RenderTexture2D RenderTexture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool HasDepth { get; }
    public bool HasMaterialMrt { get; }
    public bool IsValid => FboId != 0 && ColorTexture.Id != 0;

    public PostProcessTarget(int width, int height, bool hasDepth, bool hasMaterialMrt = false)
    {
        HasDepth = hasDepth;
        HasMaterialMrt = hasMaterialMrt;
        Allocate(width, height);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == Width && height == Height && IsValid) return;

        Release();
        Allocate(width, height);
    }

    private void Allocate(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        uint fbo = Rlgl.LoadFramebuffer();
        if (fbo == 0) return;

        Rlgl.EnableFramebuffer(fbo);

        // Attachment 0: HDR color (RGBA16F, OpenGL 3.3 Core standard)
        uint colorId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR16G16B16A16, 1);
        Rlgl.FramebufferAttach(
            fbo,
            colorId,
            FramebufferAttachType.ColorChannel0,
            FramebufferAttachTextureType.Texture2D,
            0);

        // Attachment 1 (optional MRT): Material G-Buffer (r=SSR reflectMask, g=roughness, b=snowMask)
        uint matId = 0;
        if (HasMaterialMrt)
        {
            matId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
            Rlgl.FramebufferAttach(
                fbo,
                matId,
                FramebufferAttachType.ColorChannel1,
                FramebufferAttachTextureType.Texture2D,
                0);
            Rlgl.ActiveDrawBuffers(2);
        }

        uint depthId = 0;
        if (HasDepth)
        {
            // useRenderBuffer = false -> creates a sampleable GL_DEPTH_COMPONENT texture
            depthId = Rlgl.LoadTextureDepth(width, height, false);
            Rlgl.FramebufferAttach(
                fbo,
                depthId,
                FramebufferAttachType.Depth,
                FramebufferAttachTextureType.Texture2D,
                0);
        }

        bool complete = Rlgl.FramebufferComplete(fbo);
        Rlgl.DisableFramebuffer();

        if (!complete)
        {
            if (colorId != 0) Rlgl.UnloadTexture(colorId);
            if (matId != 0) Rlgl.UnloadTexture(matId);
            if (depthId != 0) Rlgl.UnloadTexture(depthId);
            Rlgl.UnloadFramebuffer(fbo);
            Console.WriteLine($"[RenderPipeline] Failed to create HDR FBO ({width}x{height}, depth={HasDepth}, mrt={HasMaterialMrt})");
            return;
        }

        FboId = fbo;
        Width = width;
        Height = height;

        ColorTexture = new Texture2D
        {
            Id = colorId,
            Width = width,
            Height = height,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR16G16B16A16
        };
        Raylib.SetTextureFilter(ColorTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(ColorTexture, TextureWrap.Clamp);

        if (HasMaterialMrt)
        {
            MaterialTexture = new Texture2D
            {
                Id = matId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };
            Raylib.SetTextureFilter(MaterialTexture, TextureFilter.Point);
            Raylib.SetTextureWrap(MaterialTexture, TextureWrap.Clamp);
        }

        if (HasDepth)
        {
            DepthTexture = new Texture2D
            {
                Id = depthId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedGrayscale
            };
            // Point filtering on depth prevents silhouette interpolation artifacts in normal reconstruction
            Raylib.SetTextureFilter(DepthTexture, TextureFilter.Point);
            Raylib.SetTextureWrap(DepthTexture, TextureWrap.Clamp);
        }

        RenderTexture = new RenderTexture2D
        {
            Id = fbo,
            Texture = ColorTexture,
            Depth = DepthTexture
        };
    }

    private void Release()
    {
        if (ColorTexture.Id != 0) Rlgl.UnloadTexture(ColorTexture.Id);
        if (MaterialTexture.Id != 0) Rlgl.UnloadTexture(MaterialTexture.Id);
        if (DepthTexture.Id != 0) Rlgl.UnloadTexture(DepthTexture.Id);
        if (FboId != 0) Rlgl.UnloadFramebuffer(FboId);

        FboId = 0;
        ColorTexture = default;
        MaterialTexture = default;
        DepthTexture = default;
        RenderTexture = default;
        Width = 0;
        Height = 0;
    }

    public void Dispose() => Release();
}

/// <summary>
/// Stage 1 Modern Rendering Infrastructure:
/// - HDR RGBA16F offscreen scene target + sampleable Depth Texture
/// - Ping-Pong HDR targets ready for multi-pass post-processing (Bloom, God Rays, SSR)
/// - World-space position & 3-tap edge-aware normal reconstruction from depth
/// - ACES Filmic Tone Mapping + debug visualization switcher (F1)
/// </summary>
public sealed class RenderPipeline : IDisposable
{
    private const string FullscreenVertexSource = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string BloomPrefilterFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 texelSize;
        uniform float threshold;
        uniform float knee;
        out vec4 finalColor;

        vec3 prefilter(vec3 c)
        {
            float br = max(c.r, max(c.g, c.b));
            float soft = br - threshold + knee;
            soft = clamp(soft, 0.0, 2.0 * knee);
            soft = (soft * soft) / max(4.0 * knee + 1e-4, 1e-4);
            float contrib = max(soft, br - threshold) / max(br, 1e-4);
            return c * max(contrib, 0.0);
        }

        // 4-tap Karis-averaged box filter suppresses single-pixel specular fireflies
        void main()
        {
            vec2 o = texelSize * 0.5;
            vec3 c0 = prefilter(texture(texture0, fragTexCoord + vec2(-o.x, -o.y)).rgb);
            vec3 c1 = prefilter(texture(texture0, fragTexCoord + vec2( o.x, -o.y)).rgb);
            vec3 c2 = prefilter(texture(texture0, fragTexCoord + vec2(-o.x,  o.y)).rgb);
            vec3 c3 = prefilter(texture(texture0, fragTexCoord + vec2( o.x,  o.y)).rgb);

            float w0 = 1.0 / (1.0 + max(c0.r, max(c0.g, c0.b)));
            float w1 = 1.0 / (1.0 + max(c1.r, max(c1.g, c1.b)));
            float w2 = 1.0 / (1.0 + max(c2.r, max(c2.g, c2.b)));
            float w3 = 1.0 / (1.0 + max(c3.r, max(c3.g, c3.b)));

            vec3 avg = (c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3) / max(w0 + w1 + w2 + w3, 1e-4);
            finalColor = vec4(avg, 1.0);
        }
        """;

    // COD: Advanced Warfare 13-tap energy-preserving downsample filter (ringing-free)
    private const string BloomDownsampleFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 texelSize;
        out vec4 finalColor;

        void main()
        {
            float x = texelSize.x;
            float y = texelSize.y;
            vec2 uv = fragTexCoord;

            vec3 a = texture(texture0, uv + vec2(-2.0*x,  2.0*y)).rgb;
            vec3 b = texture(texture0, uv + vec2( 0.0,    2.0*y)).rgb;
            vec3 c = texture(texture0, uv + vec2( 2.0*x,  2.0*y)).rgb;
            vec3 d = texture(texture0, uv + vec2(-2.0*x,  0.0  )).rgb;
            vec3 e = texture(texture0, uv).rgb;
            vec3 f = texture(texture0, uv + vec2( 2.0*x,  0.0  )).rgb;
            vec3 g = texture(texture0, uv + vec2(-2.0*x, -2.0*y)).rgb;
            vec3 h = texture(texture0, uv + vec2( 0.0,   -2.0*y)).rgb;
            vec3 i = texture(texture0, uv + vec2( 2.0*x, -2.0*y)).rgb;

            vec3 j = texture(texture0, uv + vec2(-x,  y)).rgb;
            vec3 k = texture(texture0, uv + vec2( x,  y)).rgb;
            vec3 l = texture(texture0, uv + vec2(-x, -y)).rgb;
            vec3 m = texture(texture0, uv + vec2( x, -y)).rgb;

            vec3 res = e * 0.125;
            res += (a + c + g + i) * 0.03125;
            res += (b + d + f + h) * 0.0625;
            res += (j + k + l + m) * 0.125;
            finalColor = vec4(max(res, vec3(0.0)), 1.0);
        }
        """;

    // 9-tap 3x3 Tent upsample filter (accumulated additively up the pyramid)
    private const string BloomUpsampleFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 filterRadius;
        out vec4 finalColor;

        void main()
        {
            float x = filterRadius.x;
            float y = filterRadius.y;
            vec2 uv = fragTexCoord;

            vec3 a = texture(texture0, uv + vec2(-x,  y)).rgb;
            vec3 b = texture(texture0, uv + vec2(0.0, y)).rgb;
            vec3 c = texture(texture0, uv + vec2( x,  y)).rgb;
            vec3 d = texture(texture0, uv + vec2(-x,  0.0)).rgb;
            vec3 e = texture(texture0, uv).rgb;
            vec3 f = texture(texture0, uv + vec2( x,  0.0)).rgb;
            vec3 g = texture(texture0, uv + vec2(-x, -y)).rgb;
            vec3 h = texture(texture0, uv + vec2(0.0,-y)).rgb;
            vec3 i = texture(texture0, uv + vec2( x, -y)).rgb;

            vec3 upsample = e * 4.0 + (b + d + f + h) * 2.0 + (a + c + g + i);
            finalColor = vec4(upsample * (1.0 / 16.0), 1.0);
        }
        """;

    // Stage 4A: Screen-space Volumetric God Rays (Interleaved Gradient Noise + Henyey-Greenstein scatter)
    private const string GodRaysFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;     // Depth texture (bound as primary)
        uniform vec2 sunScreenPos;      // Projected sun UV in [0, 1]
        uniform vec3 sunColor;          // Warm/twilight volumetric tint
        uniform float sunVisible;       // 0 when sun is behind camera, 1 in front
        uniform float intensity;
        out vec4 finalColor;

        // Jorge Jimenez Interleaved Gradient Noise (eliminates raymarching banding)
        float ign(vec2 p)
        {
            return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
        }

        void main()
        {
            if (intensity <= 0.001 || sunVisible <= 0.001)
            {
                finalColor = vec4(0.0);
                return;
            }

            const int SAMPLES = 40;
            vec2 uv = fragTexCoord;
            vec2 delta = (sunScreenPos - uv) * (0.92 / float(SAMPLES));
            float jitter = ign(gl_FragCoord.xy);
            vec2 sampleUv = uv + delta * jitter;

            float illumDecay = 1.0;
            float scatter = 0.0;
            for (int i = 0; i < SAMPLES; i++)
            {
                sampleUv += delta;
                if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
                    break;

                float d = texture(texture0, sampleUv).r;
                // Unoccluded sky (depth >= 0.9998) transmits light shafts; tracks & pillars cast volumetric shadows
                float sky = step(0.9998, d);
                float distToSun = length(sampleUv - sunScreenPos);
                // Henyey-Greenstein-like forward scattering lobe centered on the sun disk
                float hg = exp(-distToSun * 2.4) * 0.85 + 0.15;
                scatter += sky * hg * illumDecay;
                illumDecay *= 0.965;
            }

            scatter = (scatter / float(SAMPLES)) * sunVisible * intensity;
            finalColor = vec4(sunColor * scatter, 1.0);
        }
        """;

    // Stage 4B: Metallic Screen-Space Reflections (MRT reflectMask + 24-step depth raymarch + binary refine + Schlick Fresnel)
    private const string SsrFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;     // HDR scene color
        uniform sampler2D depthTex;     // Sampleable hardware depth
        uniform sampler2D materialTex;  // MRT location 1: r=reflectMask, g=roughness
        uniform mat4 matViewProj;       // OpenGL column-vector P * V
        uniform mat4 matInvViewProj;    // OpenGL column-vector Inv(P * V)
        uniform vec3 viewPos;
        uniform vec2 resolution;
        out vec4 finalColor;            // rgb = reflected HDR color, a = blend weight (Schlick Fresnel * hitFade)

        vec3 reconstructWorldPos(vec2 uv, float depth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec4 world = matInvViewProj * clip;
            return world.xyz / max(abs(world.w), 1e-6) * sign(world.w);
        }

        vec3 reconstructWorldNormal(vec2 uv, float depthCenter, vec3 posCenter)
        {
            vec2 texel = 1.0 / max(resolution, vec2(1.0));
            float dL = texture(depthTex, uv - vec2(texel.x, 0.0)).r;
            float dR = texture(depthTex, uv + vec2(texel.x, 0.0)).r;
            float dD = texture(depthTex, uv - vec2(0.0, texel.y)).r;
            float dU = texture(depthTex, uv + vec2(0.0, texel.y)).r;
            vec3 dPdx = (abs(dR - depthCenter) < abs(dL - depthCenter))
                ? (reconstructWorldPos(uv + vec2(texel.x, 0.0), dR) - posCenter)
                : (posCenter - reconstructWorldPos(uv - vec2(texel.x, 0.0), dL));
            vec3 dPdy = (abs(dU - depthCenter) < abs(dD - depthCenter))
                ? (reconstructWorldPos(uv + vec2(0.0, texel.y), dU) - posCenter)
                : (posCenter - reconstructWorldPos(uv - vec2(0.0, texel.y), dD));
            vec3 n = cross(dPdx, dPdy);
            float lenSq = dot(n, n);
            return lenSq > 1e-10 ? n * inversesqrt(lenSq) : vec3(0.0, 1.0, 0.0);
        }

        vec3 projectToScreen(vec3 worldPos)
        {
            vec4 clip = matViewProj * vec4(worldPos, 1.0);
            vec3 ndc = clip.xyz / max(abs(clip.w), 1e-6) * sign(clip.w);
            return ndc * 0.5 + 0.5;
        }

        void main()
        {
            vec2 uv = fragTexCoord;
            float depth = texture(depthTex, uv).r;
            vec4 mat = texture(materialTex, uv);
            float reflectMask = mat.r;
            float roughness = mat.g;

            if (depth >= 0.9998 || reflectMask < 0.12)
            {
                finalColor = vec4(0.0);
                return;
            }

            vec3 worldPos = reconstructWorldPos(uv, depth);
            vec3 n = reconstructWorldNormal(uv, depth, worldPos);
            vec3 viewDir = normalize(worldPos - viewPos);
            vec3 reflDir = reflect(viewDir, n);

            // World-space raymarching with adaptive step growth + 4-step binary refinement
            float stepLen = 0.85;
            vec3 marchPos = worldPos + n * 0.18 + reflDir * 0.35;
            vec3 prevPos = marchPos;
            bool hit = false;
            vec2 hitUv = vec2(0.0);

            for (int i = 0; i < 24; i++)
            {
                prevPos = marchPos;
                marchPos += reflDir * stepLen;
                stepLen *= 1.12;

                vec3 proj = projectToScreen(marchPos);
                if (proj.x <= 0.0 || proj.x >= 1.0 || proj.y <= 0.0 || proj.y >= 1.0 || proj.z <= 0.0 || proj.z >= 1.0)
                    break;

                float sampledDepth = texture(depthTex, proj.xy).r;
                if (sampledDepth < 0.9998 && proj.z > sampledDepth && (proj.z - sampledDepth) < 0.018)
                {
                    // 4-step binary search refinement
                    vec3 lo = prevPos;
                    vec3 hi = marchPos;
                    for (int b = 0; b < 4; b++)
                    {
                        vec3 mid = (lo + hi) * 0.5;
                        vec3 mp = projectToScreen(mid);
                        float md = texture(depthTex, mp.xy).r;
                        if (mp.z > md) hi = mid; else lo = mid;
                    }
                    vec3 finalProj = projectToScreen(hi);
                    hitUv = finalProj.xy;
                    hit = true;
                    break;
                }
            }

            if (!hit)
            {
                finalColor = vec4(0.0);
                return;
            }

            // Smooth screen-edge fade + Schlick Fresnel weighted by MRT reflectMask & smoothness
            vec2 edge = smoothstep(0.0, 0.12, hitUv) * smoothstep(1.0, 0.88, hitUv);
            float edgeFade = edge.x * edge.y;
            float ndotv = clamp(dot(n, -viewDir), 0.0, 1.0);
            float fresnel = 0.25 + 0.75 * pow(1.0 - ndotv, 5.0);
            float weight = clamp(reflectMask * (1.0 - roughness * 0.65) * fresnel * edgeFade * 0.82, 0.0, 0.85);

            vec3 reflColor = texture(texture0, hitUv).rgb;
            finalColor = vec4(reflColor, weight);
        }
        """;

    private const string CompositeFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;     // HDR scene color (RGBA16F)
        uniform sampler2D depthTex;     // Sampleable hardware depth texture
        uniform sampler2D bloomTex;     // Multi-level upsampled HDR Bloom pyramid
        uniform sampler2D godRaysTex;   // Stage 4A Volumetric God Rays
        uniform sampler2D ssrTex;       // Stage 4B Metallic SSR (rgb=refl, a=weight)
        uniform mat4 matInvViewProj;    // Inverse(Projection * View) in OpenGL column-vector convention
        uniform vec2 resolution;        // FBO pixel dimensions
        uniform float nearPlane;
        uniform float farPlane;
        uniform float exposure;
        uniform float bloomIntensity;
        uniform float godRaysIntensity;
        uniform float ssrEnabled;
        uniform int debugMode;          // 0=Full, 1=LinearDepth, 2=WorldNormal, 3=WorldPosGrid, 4=BloomOnly
        out vec4 finalColor;

        // Linearize OpenGL hardware depth [0, 1] -> view-space distance [nearPlane, farPlane]
        float linearizeDepth(float d)
        {
            float z = d * 2.0 - 1.0;
            return (2.0 * nearPlane * farPlane) / max(farPlane + nearPlane - z * (farPlane - nearPlane), 1e-4);
        }

        // Reconstruct world-space position from screen UV [0, 1] (bottom-left origin) and hardware depth [0, 1]
        vec3 reconstructWorldPos(vec2 uv, float depth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec4 world = matInvViewProj * clip;
            return world.xyz / max(abs(world.w), 1e-6) * sign(world.w);
        }

        // Edge-aware 3-tap depth-derivative world normal reconstruction (avoids dark halos at silhouettes)
        vec3 reconstructWorldNormal(vec2 uv, float depthCenter, vec3 posCenter)
        {
            vec2 texel = 1.0 / max(resolution, vec2(1.0));
            float dL = texture(depthTex, uv - vec2(texel.x, 0.0)).r;
            float dR = texture(depthTex, uv + vec2(texel.x, 0.0)).r;
            float dD = texture(depthTex, uv - vec2(0.0, texel.y)).r;
            float dU = texture(depthTex, uv + vec2(0.0, texel.y)).r;

            vec3 dPdx = (abs(dR - depthCenter) < abs(dL - depthCenter))
                ? (reconstructWorldPos(uv + vec2(texel.x, 0.0), dR) - posCenter)
                : (posCenter - reconstructWorldPos(uv - vec2(texel.x, 0.0), dL));

            vec3 dPdy = (abs(dU - depthCenter) < abs(dD - depthCenter))
                ? (reconstructWorldPos(uv + vec2(0.0, texel.y), dU) - posCenter)
                : (posCenter - reconstructWorldPos(uv - vec2(0.0, texel.y), dD));

            vec3 n = cross(dPdx, dPdy);
            float lenSq = dot(n, n);
            return lenSq > 1e-10 ? n * inversesqrt(lenSq) : vec3(0.0, 1.0, 0.0);
        }

        // Narkowicz 2015 ACES Filmic Tone Mapping curve (expects linear HDR input)
        vec3 acesFilm(vec3 x)
        {
            const float a = 2.51;
            const float b = 0.03;
            const float c = 2.43;
            const float d = 0.59;
            const float e = 0.14;
            return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
        }

        void main()
        {
            // DrawTexturePro flips source height (-H) so fragTexCoord.y runs 0 (bottom) -> 1 (top),
            // matching OpenGL FBO texture storage and NDC Y in [-1, 1].
            vec2 uv = fragTexCoord;
            vec3 hdr = texture(texture0, uv).rgb;
            float depth = texture(depthTex, uv).r;

            if (debugMode == 1)
            {
                // Linear Depth visualization (sky = black, near geometry = bright)
                if (depth >= 0.9999)
                {
                    finalColor = vec4(0.02, 0.03, 0.06, 1.0);
                    return;
                }
                float lin = linearizeDepth(depth);
                float vis = 1.0 - clamp((lin - nearPlane) / 260.0, 0.0, 1.0);
                vis = pow(vis, 1.3);
                finalColor = vec4(vec3(vis), 1.0);
                return;
            }
            else if (debugMode == 2)
            {
                // Reconstructed World-Space Normal visualization (+Y up = green, +X = red, +Z = blue)
                if (depth >= 0.9999)
                {
                    finalColor = vec4(0.05, 0.05, 0.08, 1.0);
                    return;
                }
                vec3 pos = reconstructWorldPos(uv, depth);
                vec3 n = reconstructWorldNormal(uv, depth, pos);
                finalColor = vec4(n * 0.5 + 0.5, 1.0);
                return;
            }
            else if (debugMode == 3)
            {
                // World-Space Position 10-unit grid (verifies inverse ViewProj matrix accuracy)
                if (depth >= 0.9999)
                {
                    finalColor = vec4(0.04, 0.04, 0.07, 1.0);
                    return;
                }
                vec3 pos = reconstructWorldPos(uv, depth);
                vec3 grid = fract(pos * 0.1);
                finalColor = vec4(grid, 1.0);
                return;
            }
            else if (debugMode == 4)
            {
                // Isolated HDR Bloom buffer visualization
                vec3 bVis = texture(bloomTex, uv).rgb * max(bloomIntensity, 0.5);
                finalColor = vec4(pow(acesFilm(bVis), vec3(1.0 / 2.2)), 1.0);
                return;
            }

            // Blend Stage 4B Metallic SSR (smoothly falls back to Sphere Env-Map when ssr.a == 0)
            vec3 sceneHdr = max(hdr, vec3(0.0));
            if (ssrEnabled > 0.5)
            {
                vec4 ssr = texture(ssrTex, uv);
                sceneHdr = mix(sceneHdr, max(ssr.rgb, vec3(0.0)), clamp(ssr.a, 0.0, 0.85));
            }

            // Combine HDR scene + Stage 3 Bloom + Stage 4A Volumetric God Rays before ACES Tone Mapping
            vec3 bloom = texture(bloomTex, uv).rgb * bloomIntensity;
            vec3 godRays = texture(godRaysTex, uv).rgb * (godRaysIntensity > 0.001 ? 1.0 : 0.0);
            vec3 hdrTotal = sceneHdr + max(bloom, vec3(0.0)) + max(godRays, vec3(0.0));

            // Convert sRGB-authored HDR scene lighting to linear -> ACES Filmic -> sRGB gamma
            vec3 linearColor = pow(hdrTotal, vec3(2.2));
            vec3 mapped = acesFilm(linearColor * exposure);
            vec3 srgb = pow(mapped, vec3(1.0 / 2.2));
            finalColor = vec4(srgb, 1.0);
        }
        """;

    private const int BloomMipCount = 5;
    private PostProcessTarget? _sceneTarget;
    private PostProcessTarget? _pingPongA;
    private PostProcessTarget? _pingPongB;
    private PostProcessTarget? _godRaysTarget;
    private PostProcessTarget? _ssrTarget;
    private readonly PostProcessTarget?[] _bloomMips = new PostProcessTarget?[BloomMipCount];

    private Shader _compositeShader;
    private Shader _bloomPrefilterShader;
    private Shader _bloomDownsampleShader;
    private Shader _bloomUpsampleShader;
    private Shader _godRaysShader;
    private Shader _ssrShader;

    private int _depthTexLoc, _bloomTexLoc, _godRaysTexLoc, _ssrTexLoc;
    private int _invViewProjLoc, _resolutionLoc, _nearPlaneLoc, _farPlaneLoc, _exposureLoc;
    private int _bloomIntensityLoc, _godRaysIntensityLoc, _ssrEnabledLoc, _debugModeLoc;
    private int _preTexelLoc, _preThresholdLoc, _preKneeLoc;
    private int _downTexelLoc;
    private int _upRadiusLoc;
    private int _grSunPosLoc, _grSunColorLoc, _grSunVisibleLoc, _grIntensityLoc;
    private int _ssrDepthLoc, _ssrMatLoc, _ssrVpLoc, _ssrInvVpLoc, _ssrViewPosLoc, _ssrResLoc;

    public PostProcessDebugMode DebugMode { get; set; } = PostProcessDebugMode.None;
    public float Exposure { get; set; } = 1.08f;
    public float BloomThreshold { get; set; } = 1.05f;
    public float BloomKnee { get; set; } = 0.25f;
    public float BloomIntensity { get; set; } = 0.45f;
    public float GodRaysIntensity { get; set; } = 0.45f;
    public bool EnableSsr { get; set; } = true;
    public bool IsReady => _compositeShader.Id != 0 && _sceneTarget is { IsValid: true };

    public PostProcessTarget? SceneTarget => _sceneTarget;
    public PostProcessTarget? PingPongA => _pingPongA;
    public PostProcessTarget? PingPongB => _pingPongB;

    public RenderPipeline()
    {
        _compositeShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, CompositeFragmentSource);
        if (_compositeShader.Id != 0)
        {
            _depthTexLoc = Raylib.GetShaderLocation(_compositeShader, "depthTex");
            _bloomTexLoc = Raylib.GetShaderLocation(_compositeShader, "bloomTex");
            _godRaysTexLoc = Raylib.GetShaderLocation(_compositeShader, "godRaysTex");
            _ssrTexLoc = Raylib.GetShaderLocation(_compositeShader, "ssrTex");
            _invViewProjLoc = Raylib.GetShaderLocation(_compositeShader, "matInvViewProj");
            _resolutionLoc = Raylib.GetShaderLocation(_compositeShader, "resolution");
            _nearPlaneLoc = Raylib.GetShaderLocation(_compositeShader, "nearPlane");
            _farPlaneLoc = Raylib.GetShaderLocation(_compositeShader, "farPlane");
            _exposureLoc = Raylib.GetShaderLocation(_compositeShader, "exposure");
            _bloomIntensityLoc = Raylib.GetShaderLocation(_compositeShader, "bloomIntensity");
            _godRaysIntensityLoc = Raylib.GetShaderLocation(_compositeShader, "godRaysIntensity");
            _ssrEnabledLoc = Raylib.GetShaderLocation(_compositeShader, "ssrEnabled");
            _debugModeLoc = Raylib.GetShaderLocation(_compositeShader, "debugMode");
        }

        _bloomPrefilterShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, BloomPrefilterFragmentSource);
        if (_bloomPrefilterShader.Id != 0)
        {
            _preTexelLoc = Raylib.GetShaderLocation(_bloomPrefilterShader, "texelSize");
            _preThresholdLoc = Raylib.GetShaderLocation(_bloomPrefilterShader, "threshold");
            _preKneeLoc = Raylib.GetShaderLocation(_bloomPrefilterShader, "knee");
        }

        _bloomDownsampleShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, BloomDownsampleFragmentSource);
        if (_bloomDownsampleShader.Id != 0)
        {
            _downTexelLoc = Raylib.GetShaderLocation(_bloomDownsampleShader, "texelSize");
        }

        _bloomUpsampleShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, BloomUpsampleFragmentSource);
        if (_bloomUpsampleShader.Id != 0)
        {
            _upRadiusLoc = Raylib.GetShaderLocation(_bloomUpsampleShader, "filterRadius");
        }

        _godRaysShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, GodRaysFragmentSource);
        if (_godRaysShader.Id != 0)
        {
            _grSunPosLoc = Raylib.GetShaderLocation(_godRaysShader, "sunScreenPos");
            _grSunColorLoc = Raylib.GetShaderLocation(_godRaysShader, "sunColor");
            _grSunVisibleLoc = Raylib.GetShaderLocation(_godRaysShader, "sunVisible");
            _grIntensityLoc = Raylib.GetShaderLocation(_godRaysShader, "intensity");
        }

        _ssrShader = Raylib.LoadShaderFromMemory(FullscreenVertexSource, SsrFragmentSource);
        if (_ssrShader.Id != 0)
        {
            _ssrDepthLoc = Raylib.GetShaderLocation(_ssrShader, "depthTex");
            _ssrMatLoc = Raylib.GetShaderLocation(_ssrShader, "materialTex");
            _ssrVpLoc = Raylib.GetShaderLocation(_ssrShader, "matViewProj");
            _ssrInvVpLoc = Raylib.GetShaderLocation(_ssrShader, "matInvViewProj");
            _ssrViewPosLoc = Raylib.GetShaderLocation(_ssrShader, "viewPos");
            _ssrResLoc = Raylib.GetShaderLocation(_ssrShader, "resolution");
        }
    }

    public void CycleDebugMode()
    {
        DebugMode = (PostProcessDebugMode)(((int)DebugMode + 1) % 5);
    }

    public void EnsureSize(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(pixelWidth, 1);
        pixelHeight = Math.Max(pixelHeight, 1);
        int halfW = Math.Max(pixelWidth / 2, 1);
        int halfH = Math.Max(pixelHeight / 2, 1);

        if (_sceneTarget == null)
        {
            _sceneTarget = new PostProcessTarget(pixelWidth, pixelHeight, hasDepth: true, hasMaterialMrt: true);
            _pingPongA = new PostProcessTarget(pixelWidth, pixelHeight, hasDepth: false);
            _pingPongB = new PostProcessTarget(pixelWidth, pixelHeight, hasDepth: false);
            _godRaysTarget = new PostProcessTarget(halfW, halfH, hasDepth: false);
            _ssrTarget = new PostProcessTarget(halfW, halfH, hasDepth: false);
        }
        else
        {
            _sceneTarget.Resize(pixelWidth, pixelHeight);
            _pingPongA?.Resize(pixelWidth, pixelHeight);
            _pingPongB?.Resize(pixelWidth, pixelHeight);
            _godRaysTarget?.Resize(halfW, halfH);
            _ssrTarget?.Resize(halfW, halfH);
        }

        int mw = halfW;
        int mh = halfH;
        for (int i = 0; i < BloomMipCount; i++)
        {
            if (_bloomMips[i] == null)
                _bloomMips[i] = new PostProcessTarget(mw, mh, hasDepth: false);
            else
                _bloomMips[i]!.Resize(mw, mh);
            mw = Math.Max(mw / 2, 1);
            mh = Math.Max(mh / 2, 1);
        }
    }

    /// <summary>Binds the HDR scene FBO (RGBA16F + sampleable Depth Texture) for 3D rendering.</summary>
    public bool BeginScene(int pixelWidth, int pixelHeight)
    {
        EnsureSize(pixelWidth, pixelHeight);
        if (!IsReady || _sceneTarget == null) return false;

        Raylib.BeginTextureMode(_sceneTarget.RenderTexture);
        Raylib.ClearBackground(new Color(135, 206, 235, 255));
        return true;
    }

    /// <summary>Ends 3D scene capture into the HDR FBO and restores the default framebuffer.</summary>
    public void EndScene()
    {
        if (!IsReady) return;
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Runs a fullscreen post-processing pass between two targets (or to the screen when <paramref name="dest"/> is null).
    /// Handles OpenGL FBO V-axis flipping automatically.
    /// </summary>
    public static void DrawFullscreenPass(Texture2D sourceColor, PostProcessTarget? dest, int outWidth, int outHeight, Shader shader)
    {
        if (dest != null)
            Raylib.BeginTextureMode(dest.RenderTexture);

        Raylib.BeginShaderMode(shader);
        // Negative source height flips OpenGL's bottom-up FBO texture to Raylib's top-down 2D quad,
        // while keeping fragTexCoord.y in [0 (bottom) -> 1 (top)] matching NDC Y in [-1, 1].
        var src = new Rectangle(0f, 0f, sourceColor.Width, -sourceColor.Height);
        var dst = new Rectangle(0f, 0f, outWidth, outHeight);
        Raylib.DrawTexturePro(sourceColor, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();

        if (dest != null)
            Raylib.EndTextureMode();
    }

    private void ExecuteBloomPasses()
    {
        if (_sceneTarget == null || _bloomPrefilterShader.Id == 0 || _bloomDownsampleShader.Id == 0 || _bloomUpsampleShader.Id == 0)
            return;
        var mip0 = _bloomMips[0];
        if (mip0 == null || !mip0.IsValid) return;

        // 1. Prefilter + Karis average: Scene -> Mip 0
        Raylib.SetShaderValue(_bloomPrefilterShader, _preTexelLoc, new Vector2(1f / _sceneTarget.Width, 1f / _sceneTarget.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_bloomPrefilterShader, _preThresholdLoc, BloomThreshold, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_bloomPrefilterShader, _preKneeLoc, BloomKnee, ShaderUniformDataType.Float);
        DrawFullscreenPass(_sceneTarget.ColorTexture, mip0, mip0.Width, mip0.Height, _bloomPrefilterShader);

        // 2. 13-tap Downsample pyramid: Mip[i-1] -> Mip[i]
        for (int i = 1; i < BloomMipCount; i++)
        {
            var src = _bloomMips[i - 1]!;
            var dst = _bloomMips[i]!;
            Raylib.SetShaderValue(_bloomDownsampleShader, _downTexelLoc, new Vector2(1f / src.Width, 1f / src.Height), ShaderUniformDataType.Vec2);
            DrawFullscreenPass(src.ColorTexture, dst, dst.Width, dst.Height, _bloomDownsampleShader);
        }

        // 3. 9-tap 3x3 Tent Upsample with Additive accumulation: Mip[i] -> Mip[i-1]
        for (int i = BloomMipCount - 1; i > 0; i--)
        {
            var src = _bloomMips[i]!;
            var dst = _bloomMips[i - 1]!;
            // Radius in UV space (~1.4 texels of source mip for lush wide halo)
            Raylib.SetShaderValue(_bloomUpsampleShader, _upRadiusLoc, new Vector2(1.4f / src.Width, 1.4f / src.Height), ShaderUniformDataType.Vec2);

            Raylib.BeginTextureMode(dst.RenderTexture);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.BeginShaderMode(_bloomUpsampleShader);
            var sRect = new Rectangle(0f, 0f, src.ColorTexture.Width, -src.ColorTexture.Height);
            var dRect = new Rectangle(0f, 0f, dst.Width, dst.Height);
            Raylib.DrawTexturePro(src.ColorTexture, sRect, dRect, Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
            Raylib.EndBlendMode();
            Raylib.EndTextureMode();
        }
    }

    private void ExecuteGodRaysPass(Vector3 sunTint, float atmosphereMood)
    {
        if (_sceneTarget == null || _godRaysTarget == null || !_godRaysTarget.IsValid || _godRaysShader.Id == 0)
            return;

        // Project distant sun point along LitShader.SunDir into screen UV [0, 1]
        Vector3 sunWorld = RenderQueue.ViewPosition + LitShader.SunDir * 900f;
        Vector4 sunClip = Vector4.Transform(new Vector4(sunWorld, 1f), RenderQueue.ViewProjectionMatrix);
        float sunVis = sunClip.W > 0.1f ? 1f : 0f;
        Vector2 sunUv = new(0.5f, 0.85f);
        if (sunClip.W > 0.1f)
        {
            sunUv = new Vector2((sunClip.X / sunClip.W) * 0.5f + 0.5f, (sunClip.Y / sunClip.W) * 0.5f + 0.5f);
            // Smooth fade when sun moves far outside screen
            float offscreen = MathF.Max(MathF.Abs(sunUv.X - 0.5f), MathF.Abs(sunUv.Y - 0.5f));
            sunVis = Math.Clamp(1.6f - offscreen, 0.25f, 1f);
        }

        Vector3 rayColor = Vector3.Lerp(sunTint * new Vector3(1.0f, 0.88f, 0.68f), new Vector3(0.45f, 0.58f, 0.95f), atmosphereMood);
        Raylib.SetShaderValue(_godRaysShader, _grSunPosLoc, sunUv, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_godRaysShader, _grSunColorLoc, rayColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_godRaysShader, _grSunVisibleLoc, sunVis, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_godRaysShader, _grIntensityLoc, GodRaysIntensity, ShaderUniformDataType.Float);

        DrawFullscreenPass(_sceneTarget.DepthTexture, _godRaysTarget, _godRaysTarget.Width, _godRaysTarget.Height, _godRaysShader);
    }

    private void ExecuteSsrPass(Matrix4x4 vpForGl, Matrix4x4 invVpForGl)
    {
        if (_sceneTarget == null || _ssrTarget == null || !_ssrTarget.IsValid || _ssrShader.Id == 0)
            return;

        Raylib.SetShaderValueMatrix(_ssrShader, _ssrVpLoc, vpForGl);
        Raylib.SetShaderValueMatrix(_ssrShader, _ssrInvVpLoc, invVpForGl);
        Raylib.SetShaderValue(_ssrShader, _ssrViewPosLoc, RenderQueue.ViewPosition, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_ssrShader, _ssrResLoc, new Vector2(_sceneTarget.Width, _sceneTarget.Height), ShaderUniformDataType.Vec2);

        Raylib.BeginTextureMode(_ssrTarget.RenderTexture);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginShaderMode(_ssrShader);
        Raylib.SetShaderValueTexture(_ssrShader, _ssrDepthLoc, _sceneTarget.DepthTexture);
        Raylib.SetShaderValueTexture(_ssrShader, _ssrMatLoc, _sceneTarget.MaterialTexture);
        var src = new Rectangle(0f, 0f, _sceneTarget.ColorTexture.Width, -_sceneTarget.ColorTexture.Height);
        var dst = new Rectangle(0f, 0f, _ssrTarget.Width, _ssrTarget.Height);
        Raylib.DrawTexturePro(_sceneTarget.ColorTexture, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Final composite pass: executes Stage 3 HDR Bloom, Stage 4A Volumetric God Rays,
    /// Stage 4B Metallic SSR, and ACES Filmic Tone Mapping (or debug view) to screen.
    /// </summary>
    public void ComposeToScreen(int screenWidth, int screenHeight, Vector3 sunTint, float atmosphereMood = 0f)
    {
        if (!IsReady || _sceneTarget == null) return;

        Matrix4x4 vpForGl = Matrix4x4.Transpose(RenderQueue.ViewProjectionMatrix);
        Matrix4x4 invVpForGl = Matrix4x4.Transpose(RenderQueue.InvViewProjectionMatrix);

        bool runBloom = (BloomIntensity > 0.001f || DebugMode == PostProcessDebugMode.BloomOnly) && _bloomMips[0] is { IsValid: true };
        if (runBloom)
            ExecuteBloomPasses();

        bool runGodRays = GodRaysIntensity > 0.001f && DebugMode == PostProcessDebugMode.None && _godRaysTarget is { IsValid: true };
        if (runGodRays)
            ExecuteGodRaysPass(sunTint, atmosphereMood);

        bool runSsr = EnableSsr && DebugMode == PostProcessDebugMode.None && _ssrTarget is { IsValid: true };
        if (runSsr)
            ExecuteSsrPass(vpForGl, invVpForGl);

        Raylib.SetShaderValueMatrix(_compositeShader, _invViewProjLoc, invVpForGl);
        Raylib.SetShaderValue(_compositeShader, _resolutionLoc, new Vector2(_sceneTarget.Width, _sceneTarget.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_compositeShader, _nearPlaneLoc, RenderQueue.NearPlane, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _farPlaneLoc, RenderQueue.FarPlane, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _exposureLoc, Exposure, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _bloomIntensityLoc, runBloom ? BloomIntensity : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _godRaysIntensityLoc, runGodRays ? GodRaysIntensity : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _ssrEnabledLoc, runSsr ? 1f : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_compositeShader, _debugModeLoc, (int)DebugMode, ShaderUniformDataType.Int);

        Raylib.BeginShaderMode(_compositeShader);
        Raylib.SetShaderValueTexture(_compositeShader, _depthTexLoc, _sceneTarget.DepthTexture);
        if (_bloomMips[0] is { IsValid: true } mip0)
            Raylib.SetShaderValueTexture(_compositeShader, _bloomTexLoc, mip0.ColorTexture);
        if (_godRaysTarget is { IsValid: true } gr)
            Raylib.SetShaderValueTexture(_compositeShader, _godRaysTexLoc, gr.ColorTexture);
        if (_ssrTarget is { IsValid: true } ssr)
            Raylib.SetShaderValueTexture(_compositeShader, _ssrTexLoc, ssr.ColorTexture);
        var src = new Rectangle(0f, 0f, _sceneTarget.ColorTexture.Width, -_sceneTarget.ColorTexture.Height);
        var dst = new Rectangle(0f, 0f, screenWidth, screenHeight);
        Raylib.DrawTexturePro(_sceneTarget.ColorTexture, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
    }

    public void Dispose()
    {
        _sceneTarget?.Dispose();
        _pingPongA?.Dispose();
        _pingPongB?.Dispose();
        _godRaysTarget?.Dispose();
        _ssrTarget?.Dispose();
        for (int i = 0; i < BloomMipCount; i++)
        {
            _bloomMips[i]?.Dispose();
            _bloomMips[i] = null;
        }
        if (_compositeShader.Id != 0) { Raylib.UnloadShader(_compositeShader); _compositeShader = default; }
        if (_bloomPrefilterShader.Id != 0) { Raylib.UnloadShader(_bloomPrefilterShader); _bloomPrefilterShader = default; }
        if (_bloomDownsampleShader.Id != 0) { Raylib.UnloadShader(_bloomDownsampleShader); _bloomDownsampleShader = default; }
        if (_bloomUpsampleShader.Id != 0) { Raylib.UnloadShader(_bloomUpsampleShader); _bloomUpsampleShader = default; }
        if (_godRaysShader.Id != 0) { Raylib.UnloadShader(_godRaysShader); _godRaysShader = default; }
        if (_ssrShader.Id != 0) { Raylib.UnloadShader(_ssrShader); _ssrShader = default; }
    }
}
