using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>
/// Real-time directional shadow map (2048x2048 hardware depth texture, OpenGL 3.3 Core).
/// Follows the player with texel snapping to eliminate shimmering during camera/ball motion.
/// </summary>
public sealed unsafe class ShadowMap : IDisposable
{
    public const int Resolution = 2048;
    public const float OrthoHalfSize = 72f;
    private const float LightDistance = 120f;
    private const float NearClip = 4f;
    private const float FarClip = 260f;

    private const string DepthVertexSource = """
        #version 330
        in vec3 vertexPosition;
        uniform mat4 mvp;
        void main()
        {
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string DepthFragmentSource = """
        #version 330
        out vec4 finalColor;
        void main()
        {
            finalColor = vec4(1.0);
        }
        """;

    public uint FboId { get; private set; }
    public Texture2D DepthTexture { get; private set; }
    private uint _dummyColorId;

    public Shader DepthShader { get; private set; }
    public static Material DepthMaterial { get; private set; }
    public Matrix4x4 LightViewProjection { get; private set; } = Matrix4x4.Identity;
    public bool IsValid => FboId != 0 && DepthTexture.Id != 0 && DepthShader.Id != 0;
    private Matrix4x4 _savedProj;
    private Matrix4x4 _savedView;

    public ShadowMap()
    {
        DepthShader = Raylib.LoadShaderFromMemory(DepthVertexSource, DepthFragmentSource);
        if (DepthShader.Id == 0)
        {
            Console.WriteLine("[ShadowMap] Failed to compile depth shader.");
            return;
        }

        Material mat = Raylib.LoadMaterialDefault();
        mat.Shader = DepthShader;
        DepthMaterial = mat;

        uint fbo = Rlgl.LoadFramebuffer();
        if (fbo == 0) return;

        Rlgl.EnableFramebuffer(fbo);

        // Attach a minimal 1-byte grayscale color target alongside the hardware depth texture
        // so framebuffer completeness is guaranteed across macOS Apple Silicon & desktop GL 3.3 drivers.
        uint colorId = Rlgl.LoadTexture(null, Resolution, Resolution, PixelFormat.UncompressedGrayscale, 1);
        Rlgl.FramebufferAttach(fbo, colorId, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);

        uint depthId = Rlgl.LoadTextureDepth(Resolution, Resolution, false);
        Rlgl.FramebufferAttach(fbo, depthId, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);

        bool complete = Rlgl.FramebufferComplete(fbo);
        Rlgl.DisableFramebuffer();

        if (!complete)
        {
            if (colorId != 0) Rlgl.UnloadTexture(colorId);
            if (depthId != 0) Rlgl.UnloadTexture(depthId);
            Rlgl.UnloadFramebuffer(fbo);
            Console.WriteLine("[ShadowMap] Shadow FBO incomplete; dynamic shadows disabled.");
            return;
        }

        FboId = fbo;
        _dummyColorId = colorId;
        DepthTexture = new Texture2D
        {
            Id = depthId,
            Width = Resolution,
            Height = Resolution,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedGrayscale
        };
        Raylib.SetTextureFilter(DepthTexture, TextureFilter.Point);
        Raylib.SetTextureWrap(DepthTexture, TextureWrap.Clamp);
    }

    public bool BeginShadowPass(Vector3 focusWorld, Vector3 sunDir)
    {
        if (!IsValid) return false;

        Vector3 dir = Vector3.Normalize(sunDir);
        Vector3 up = MathF.Abs(dir.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;

        // Build an un-snapped light basis to snap the focus point onto exact shadow-map texels
        Matrix4x4 baseView = Matrix4x4.CreateLookAt(focusWorld + dir * LightDistance, focusWorld, up);
        float texelWorld = (OrthoHalfSize * 2f) / Resolution;
        Vector3 focusLS = Vector3.Transform(focusWorld, baseView);
        float snapX = MathF.Floor(focusLS.X / texelWorld) * texelWorld - focusLS.X;
        float snapY = MathF.Floor(focusLS.Y / texelWorld) * texelWorld - focusLS.Y;

        Vector3 lightRight = new(baseView.M11, baseView.M21, baseView.M31);
        Vector3 lightUp = new(baseView.M12, baseView.M22, baseView.M32);
        Vector3 snappedFocus = focusWorld + lightRight * snapX + lightUp * snapY;

        Vector3 lightPos = snappedFocus + dir * LightDistance;
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, snappedFocus, up);
        Matrix4x4 lightProj = Matrix4x4.CreateOrthographic(OrthoHalfSize * 2f, OrthoHalfSize * 2f, NearClip, FarClip);
        LightViewProjection = lightView * lightProj;

        Rlgl.DrawRenderBatchActive();
        _savedProj = Rlgl.GetMatrixProjection();
        _savedView = Rlgl.GetMatrixModelview();

        Rlgl.EnableFramebuffer(FboId);
        Rlgl.Viewport(0, 0, Resolution, Resolution);
        Rlgl.ClearScreenBuffers();

        // Raylib's DrawMesh multiplies (model * GetMatrixModelview() * GetMatrixProjection()) in column-vector layout
        Rlgl.SetMatrixProjection(Matrix4x4.Transpose(lightProj));
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
        Rlgl.MultMatrixf(Matrix4x4.Transpose(lightView));

        Rlgl.EnableDepthTest();
        Rlgl.EnableDepthMask();
        Rlgl.DisableBackfaceCulling();

        RenderQueue.BeginShadowPass(snappedFocus, OrthoHalfSize * 1.45f);
        return true;
    }

    public void EndShadowPass()
    {
        if (!IsValid) return;

        Rlgl.DrawRenderBatchActive();
        RenderQueue.EndShadowPass();
        Rlgl.EnableBackfaceCulling();

        Rlgl.SetMatrixProjection(_savedProj);
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
        Rlgl.MultMatrixf(_savedView);
        Rlgl.DisableFramebuffer();
    }

    public void Dispose()
    {
        if (DepthTexture.Id != 0) Rlgl.UnloadTexture(DepthTexture.Id);
        if (_dummyColorId != 0) Rlgl.UnloadTexture(_dummyColorId);
        if (FboId != 0) Rlgl.UnloadFramebuffer(FboId);
        if (DepthShader.Id != 0) Raylib.UnloadShader(DepthShader);
        if (DepthMaterial.Maps != null) Raylib.MemFree(DepthMaterial.Maps);
        DepthTexture = default;
        DepthShader = default;
        DepthMaterial = default;
        _dummyColorId = 0;
        FboId = 0;
    }
}
