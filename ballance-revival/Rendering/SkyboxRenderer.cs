using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public class SkyboxRenderer : IDisposable
{
    private const string SunVertexSource = """
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

    private const string SunFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        uniform vec3 sunColor;
        uniform float moodyNight;
        out vec4 finalColor;
        void main()
        {
            vec2 p = fragTexCoord * 2.0 - 1.0;
            float r = length(p);
            if (r > 1.0) discard;

            // Crisp HDR solar/lunar disk + soft multi-layered Mie atmospheric corona
            float diskRadius = mix(0.135, 0.115, moodyNight);
            float disk = smoothstep(diskRadius + 0.024, diskRadius - 0.012, r);
            float innerHalo = exp(-r * 5.8) * 1.15;
            float outerHalo = pow(max(1.0 - r, 0.0), 3.0) * 0.48;

            vec3 coreCol = mix(vec3(5.2, 4.3, 2.7), vec3(2.2, 2.45, 2.9), moodyNight);
            vec3 haloCol = mix(sunColor * vec3(1.55, 1.12, 0.62), vec3(0.45, 0.60, 0.98), moodyNight);

            vec3 col = coreCol * disk + haloCol * (innerHalo + outerHalo);
            float alpha = clamp(disk + innerHalo + outerHalo, 0.0, 1.0);
            finalColor = vec4(col, alpha);
        }
        """;

    private readonly TextureManager _textureManager;
    private Texture2D _front, _back, _left, _right, _down;
    private readonly Shader _sunShader;
    private readonly int _sunColorLoc;
    private readonly int _sunMoodLoc;
    public Vector3 FogColor { get; private set; } = new(0.86f, 0.62f, 0.68f);
    public Vector3 SkyTint { get; private set; } = Vector3.One;
    public Vector3 GroundTint { get; private set; } = Vector3.One;
    public Vector3 SunTint { get; private set; } = Vector3.One;

    public SkyboxRenderer(TextureManager textureManager)
    {
        _textureManager = textureManager;
        _sunShader = Raylib.LoadShaderFromMemory(SunVertexSource, SunFragmentSource);
        if (_sunShader.Id != 0)
        {
            _sunColorLoc = Raylib.GetShaderLocation(_sunShader, "sunColor");
            _sunMoodLoc = Raylib.GetShaderLocation(_sunShader, "moodyNight");
        }
        SetTheme("Sky_A");
    }

    public void SetTheme(string prefix)
    {
        _front = Load($"sky/{prefix}_Front.bmp");
        _back = Load($"sky/{prefix}_Back.bmp");
        _left = Load($"sky/{prefix}_Left.bmp");
        _right = Load($"sky/{prefix}_Right.bmp");
        _down = Load($"sky/{prefix}_Down.bmp");
        FogColor = _textureManager.AverageColor($"sky/{prefix}_Front.bmp");

        Vector3 zenith = _textureManager.AverageColor($"sky/{prefix}_Front.bmp", 0f, 0.15f);
        Vector3 below = _textureManager.AverageColor($"sky/{prefix}_Down.bmp", 0f, 1f);
        SkyTint = Tint(zenith, 0.35f);
        GroundTint = Tint(below, 0.3f) * 0.85f;
        SunTint = Tint(FogColor, 0.25f) * 1.05f;
    }

    /// <summary>Keeps only the hue of a sky color at normalized brightness, blended toward white.</summary>
    private static Vector3 Tint(Vector3 color, float amount)
    {
        float lum = Vector3.Dot(color, new Vector3(0.299f, 0.587f, 0.114f));
        Vector3 hue = color / MathF.Max(lum, 0.05f);
        return Vector3.Clamp(Vector3.Lerp(Vector3.One, hue, amount), new Vector3(0.6f), new Vector3(1.4f));
    }

    private Texture2D Load(string name)
    {
        var tex = _textureManager.GetTexture(name);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return tex;
    }

    public void Draw(Vector3 c, float moodyNight = 0f)
    {
        const float s = 400.0f;
        byte r = (byte)(255 - moodyNight * 175f);
        byte g = (byte)(255 - moodyNight * 165f);
        byte b = (byte)(255 - moodyNight * 125f);

        // rlgl applies depth/cull state when the batch is flushed, so flush around the sky
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();

        // Corners are bottom-left, bottom-right, top-right, top-left as seen from inside the box.
        // Virtools' +Z (front) maps to -Z after the left-to-right-handed mirror.
        DrawQuad(_front, c, new(-s, -s, -s), new(s, -s, -s), new(s, s, -s), new(-s, s, -s), r, g, b);
        DrawQuad(_back, c, new(s, -s, s), new(-s, -s, s), new(-s, s, s), new(s, s, s), r, g, b);
        DrawQuad(_left, c, new(-s, -s, s), new(-s, -s, -s), new(-s, s, -s), new(-s, s, s), r, g, b);
        DrawQuad(_right, c, new(s, -s, -s), new(s, -s, s), new(s, s, s), new(s, s, -s), r, g, b);
        DrawQuad(_down, c, new(-s, -s, s), new(s, -s, s), new(s, -s, -s), new(-s, -s, -s), r, g, b);

        // No top texture ships with the game: stretch the front image's top row over the lid
        DrawQuad(_front, c, new(-s, s, -s), new(s, s, -s), new(s, s, s), new(-s, s, s), r, g, b, topRowOnly: true);

        // Visible HDR Sun (day) / Moon (night) disk + atmospheric Mie corona
        DrawSunDisk(c, moodyNight);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    private void DrawSunDisk(Vector3 camPos, float moodyNight)
    {
        if (_sunShader.Id == 0) return;

        Vector3 sunDir = LitShader.VisualSunDir;
        Vector3 sunCenter = camPos + sunDir * 355f;
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, sunDir));
        Vector3 up = Vector3.Cross(sunDir, right);
        float halfSize = 115f;

        Vector3 bl = sunCenter - right * halfSize - up * halfSize;
        Vector3 br = sunCenter + right * halfSize - up * halfSize;
        Vector3 tr = sunCenter + right * halfSize + up * halfSize;
        Vector3 tl = sunCenter - right * halfSize + up * halfSize;

        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.BeginShaderMode(_sunShader);
        Raylib.SetShaderValue(_sunShader, _sunColorLoc, SunTint, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_sunShader, _sunMoodLoc, moodyNight, ShaderUniformDataType.Float);

        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(bl.X, bl.Y, bl.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(br.X, br.Y, br.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(tr.X, tr.Y, tr.Z);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(tl.X, tl.Y, tl.Z);
        Rlgl.End();
        Rlgl.DrawRenderBatchActive();

        Raylib.EndShaderMode();
        Raylib.EndBlendMode();
    }

    private static void DrawQuad(Texture2D tex, Vector3 c, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, byte r, byte g, byte b, bool topRowOnly = false)
    {
        float vTop = 0.002f;
        float vBottom = topRowOnly ? vTop : 0.998f;

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(r, g, b, 255);
        Rlgl.TexCoord2f(0f, vBottom); Rlgl.Vertex3f(c.X + bl.X, c.Y + bl.Y, c.Z + bl.Z);
        Rlgl.TexCoord2f(1f, vBottom); Rlgl.Vertex3f(c.X + br.X, c.Y + br.Y, c.Z + br.Z);
        Rlgl.TexCoord2f(1f, vTop); Rlgl.Vertex3f(c.X + tr.X, c.Y + tr.Y, c.Z + tr.Z);
        Rlgl.TexCoord2f(0f, vTop); Rlgl.Vertex3f(c.X + tl.X, c.Y + tl.Y, c.Z + tl.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public void Dispose()
    {
        if (_sunShader.Id != 0) Raylib.UnloadShader(_sunShader);
    }
}
