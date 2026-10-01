using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public class SkyboxRenderer : IDisposable
{
    private readonly TextureManager _textureManager;
    private string _currentPrefix = "Sky_A";
    private readonly Texture2D[] _faces = new Texture2D[6]; // Front, Back, Left, Right, Up, Down

    public SkyboxRenderer(TextureManager textureManager)
    {
        _textureManager = textureManager;
        SetTheme("Sky_A");
    }

    public void SetTheme(string prefix)
    {
        _currentPrefix = prefix;
        // Textures: Front, Back, Left, Right, Down
        _faces[0] = _textureManager.GetTexture($"sky/{prefix}_Front.bmp");
        _faces[1] = _textureManager.GetTexture($"sky/{prefix}_Back.bmp");
        _faces[2] = _textureManager.GetTexture($"sky/{prefix}_Left.bmp");
        _faces[3] = _textureManager.GetTexture($"sky/{prefix}_Right.bmp");
        _faces[4] = _textureManager.GetTexture($"sky/{prefix}_Front.bmp"); // Top fallback
        _faces[5] = _textureManager.GetTexture($"sky/{prefix}_Down.bmp");
    }

    public void Draw(Vector3 cameraPosition)
    {
        float s = 400.0f;
        Vector3 c = cameraPosition;

        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();

        // 1. Front (+Z)
        DrawQuad(_faces[0],
            new Vector3(c.X - s, c.Y - s, c.Z + s),
            new Vector3(c.X + s, c.Y - s, c.Z + s),
            new Vector3(c.X + s, c.Y + s, c.Z + s),
            new Vector3(c.X - s, c.Y + s, c.Z + s));

        // 2. Back (-Z)
        DrawQuad(_faces[1],
            new Vector3(c.X + s, c.Y - s, c.Z - s),
            new Vector3(c.X - s, c.Y - s, c.Z - s),
            new Vector3(c.X - s, c.Y + s, c.Z - s),
            new Vector3(c.X + s, c.Y + s, c.Z - s));

        // 3. Left (-X)
        DrawQuad(_faces[2],
            new Vector3(c.X - s, c.Y - s, c.Z - s),
            new Vector3(c.X - s, c.Y - s, c.Z + s),
            new Vector3(c.X - s, c.Y + s, c.Z + s),
            new Vector3(c.X - s, c.Y + s, c.Z - s));

        // 4. Right (+X)
        DrawQuad(_faces[3],
            new Vector3(c.X + s, c.Y - s, c.Z + s),
            new Vector3(c.X + s, c.Y - s, c.Z - s),
            new Vector3(c.X + s, c.Y + s, c.Z - s),
            new Vector3(c.X + s, c.Y + s, c.Z + s));

        // 5. Down (-Y)
        DrawQuad(_faces[5],
            new Vector3(c.X - s, c.Y - s, c.Z - s),
            new Vector3(c.X + s, c.Y - s, c.Z - s),
            new Vector3(c.X + s, c.Y - s, c.Z + s),
            new Vector3(c.X - s, c.Y - s, c.Z + s));

        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    private static void DrawQuad(Texture2D tex, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);

        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(p0.X, p0.Y, p0.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(p1.X, p1.Y, p1.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(p2.X, p2.Y, p2.Z);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(p3.X, p3.Y, p3.Z);

        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public void Dispose()
    {
    }
}
