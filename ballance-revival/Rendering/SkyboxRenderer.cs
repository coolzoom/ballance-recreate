using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public class SkyboxRenderer : IDisposable
{
    private readonly TextureManager _textureManager;
    private Texture2D _front, _back, _left, _right, _down;
    public Vector3 FogColor { get; private set; } = new(0.86f, 0.62f, 0.68f);

    public SkyboxRenderer(TextureManager textureManager)
    {
        _textureManager = textureManager;
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
    }

    private Texture2D Load(string name)
    {
        var tex = _textureManager.GetTexture(name);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return tex;
    }

    public void Draw(Vector3 c)
    {
        const float s = 400.0f;

        // rlgl applies depth/cull state when the batch is flushed, so flush around the sky
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();

        // Corners are bottom-left, bottom-right, top-right, top-left as seen from inside the box.
        // Virtools' +Z (front) maps to -Z after the left-to-right-handed mirror.
        DrawQuad(_front, c, new(-s, -s, -s), new(s, -s, -s), new(s, s, -s), new(-s, s, -s));
        DrawQuad(_back, c, new(s, -s, s), new(-s, -s, s), new(-s, s, s), new(s, s, s));
        DrawQuad(_left, c, new(-s, -s, s), new(-s, -s, -s), new(-s, s, -s), new(-s, s, s));
        DrawQuad(_right, c, new(s, -s, -s), new(s, -s, s), new(s, s, s), new(s, s, -s));
        DrawQuad(_down, c, new(-s, -s, s), new(s, -s, s), new(s, -s, -s), new(-s, -s, -s));

        // No top texture ships with the game: stretch the front image's top row over the lid
        DrawQuad(_front, c, new(-s, s, -s), new(s, s, -s), new(s, s, s), new(-s, s, s), topRowOnly: true);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    private static void DrawQuad(Texture2D tex, Vector3 c, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, bool topRowOnly = false)
    {
        float vTop = 0.002f;
        float vBottom = topRowOnly ? vTop : 0.998f;

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.TexCoord2f(0f, vBottom); Rlgl.Vertex3f(c.X + bl.X, c.Y + bl.Y, c.Z + bl.Z);
        Rlgl.TexCoord2f(1f, vBottom); Rlgl.Vertex3f(c.X + br.X, c.Y + br.Y, c.Z + br.Z);
        Rlgl.TexCoord2f(1f, vTop); Rlgl.Vertex3f(c.X + tr.X, c.Y + tr.Y, c.Z + tr.Z);
        Rlgl.TexCoord2f(0f, vTop); Rlgl.Vertex3f(c.X + tl.X, c.Y + tl.Y, c.Z + tl.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public void Dispose()
    {
    }
}
