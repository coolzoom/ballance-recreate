using System.Numerics;
using BallanceRevival.Rendering;
using Raylib_cs;

namespace BallanceRevival.Gameplay;

/// <summary>
/// In-game HUD built directly from the original Camera.nmo sprite layout,
/// Button01_special.tga atlas, and Font_1.tga bitmap font.
/// </summary>
public class GameHUD : IDisposable
{
    private readonly Texture2D _atlas;
    private readonly GameFont? _font;

    // Exact UV pixel rectangles in Button01_special.tga (256x256), from Camera.nmo 0x10f000 chunks
    private static readonly Rectangle SrcPointsBg = new(0.3255f * 256f, 0.73f * 256f, (1.0f - 0.3255f) * 256f, (0.9804f - 0.73f) * 256f);
    private static readonly Rectangle SrcPointsGlow = new(0.435f * 256f, 0.508f * 256f, (0.995f - 0.435f) * 256f, (0.713f - 0.508f) * 256f);
    private static readonly Rectangle SrcLifeBall = new(0.0667f * 256f, 0.5294f * 256f, (0.1765f - 0.0667f) * 256f, (0.6392f - 0.5294f) * 256f);
    private static readonly Rectangle SrcLifeStart = new(0.1843f * 256f, 0.4667f * 256f, (0.4078f - 0.1843f) * 256f, (0.702f - 0.4667f) * 256f);
    private static readonly Rectangle SrcLifeEnd = new(0.004f * 256f, 0.5255f * 256f, (0.06f - 0.004f) * 256f, (0.641f - 0.5255f) * 256f);

    private string _bannerText = string.Empty;
    private float _bannerTimer = 0f;
    private float _bannerDuration = 2.5f;
    private Color _bannerColor = Color.Gold;
    private bool _showHelp = false;

    private int _lastScore = -1;
    private float _scoreGlow = 0f;

    public GameHUD(string? texturesDir = null)
    {
        if (!string.IsNullOrEmpty(texturesDir))
        {
            string atlasPath = Path.Combine(texturesDir, "Button01_special.tga");
            if (File.Exists(atlasPath) && ImageDecoder.TryLoad(atlasPath, out Image img))
            {
                _atlas = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
                if (_atlas.Id != 0)
                    Raylib.SetTextureFilter(_atlas, TextureFilter.Bilinear);
            }

            string fontPath = Path.Combine(texturesDir, "Font_1.tga");
            if (File.Exists(fontPath))
            {
                _font = new GameFont(fontPath);
            }
        }
    }

    public void ShowBanner(string text, Color color, float duration = 2.5f)
    {
        _bannerText = text;
        _bannerColor = color;
        _bannerTimer = duration;
        _bannerDuration = MathF.Max(duration, 0.1f);
    }

    public void Update(float dt)
    {
        if (_bannerTimer > 0f)
        {
            _bannerTimer -= dt;
            if (_bannerTimer <= 0f)
                _bannerText = string.Empty;
        }

        if (_scoreGlow > 0f)
            _scoreGlow = MathF.Max(0f, _scoreGlow - dt * 1.4f);

        if (Raylib.IsKeyPressed(KeyboardKey.H))
            _showHelp = !_showHelp;
    }

    public void Draw(int screenWidth, int screenHeight, int levelNumber, int score, int lives, BallMaterial material, float speed)
    {
        if (_lastScore >= 0 && score > _lastScore)
            _scoreGlow = 1f;
        _lastScore = score;

        // Preserve original 4:3 HUD proportions on widescreen displays so life spheres stay circular
        float h = screenHeight;
        float refW = h * (4f / 3f);

        DrawScore(h, refW, score);
        DrawLives(screenWidth, h, refW, lives);

        if (!string.IsNullOrEmpty(_bannerText))
            DrawBanner(screenWidth, screenHeight);

        if (_showHelp)
            DrawHelp(screenWidth);
    }

    private void DrawScore(float h, float refW, int score)
    {
        // Camera.nmo Interface_Points_bg: [0.015, 0.87, 0.235, 0.98]
        var bgDest = new Rectangle(0.015f * refW, 0.87f * h, (0.235f - 0.015f) * refW, (0.98f - 0.87f) * h);

        if (_atlas.Id != 0)
        {
            if (_scoreGlow > 0.01f)
            {
                byte glowAlpha = (byte)(Math.Clamp(_scoreGlow, 0f, 1f) * 220f);
                Raylib.DrawTexturePro(_atlas, SrcPointsGlow, bgDest, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, glowAlpha));
            }
            Raylib.DrawTexturePro(_atlas, SrcPointsBg, bgDest, Vector2.Zero, 0f, Color.White);
        }
        else
        {
            Raylib.DrawRectangleRounded(bgDest, 0.35f, 8, new Color((byte)24, (byte)30, (byte)38, (byte)215));
            Raylib.DrawRectangleRoundedLines(bgDest, 0.35f, 8, new Color((byte)186, (byte)196, (byte)206, (byte)230));
        }

        // Camera.nmo Interface_Points_digits: [0.05, 0.8842, 0.2037, 0.9342]
        // Gameplay.nmo Font_Punktezähler: Space=(1.5,1.0), Scale=(0.8,0.9), right-aligned with drop shadow
        string digits = Math.Max(0, score).ToString();
        float rightX = 0.201f * refW;
        float digitH = (0.9342f - 0.8842f) * h * 0.92f;
        float digitY = 0.8842f * h + ((0.9342f - 0.8842f) * h - digitH) * 0.45f;

        if (_font != null && _font.Ready)
        {
            const float aspectX = 1.18f;
            const float extraSpace = 4.0f;
            float shadowOff = MathF.Max(1.5f, h * 0.0022f);
            _font.DrawRight(digits, rightX + shadowOff, digitY + shadowOff, digitH, new Color((byte)0, (byte)0, (byte)0, (byte)110), aspectX, extraSpace);
            _font.DrawRight(digits, rightX, digitY, digitH, new Color((byte)248, (byte)250, (byte)255, (byte)255), aspectX, extraSpace);
        }
        else
        {
            int fontSize = Math.Max(16, (int)(digitH * 0.85f));
            int tw = Raylib.MeasureText(digits, fontSize);
            Raylib.DrawText(digits, (int)(rightX - tw), (int)digitY, fontSize, Color.White);
        }
    }

    private void DrawLives(int screenWidth, float h, float refW, int lives)
    {
        int shown = Math.Clamp(lives, 0, 8);
        if (shown == 0) return;

        // Right-anchor all life sprites preserving 4:3 proportions:
        // screenX = screenWidth - (1.0 - normX) * refW
        float AnchorX(float normX) => screenWidth - (1f - normX) * refW;

        // Gameplay.nmo constants: LifeBall Start x = 0.95, Offset LifeBalls x = 0.039, LifeEnd Start x = 0.929
        const float step = 0.039f;
        const float ballW = 0.9934f - 0.9496f; // 0.0438
        const float ballH = 0.9438f - 0.8854f; // 0.0584

        if (_atlas.Id != 0)
        {
            // 1. Right C-bracket (Interface_Life_Startbogen: [0.8906, 0.85, 0.9813, 0.9771])
            var startDest = new Rectangle(
                AnchorX(0.8906f),
                0.85f * h,
                (0.9813f - 0.8906f) * refW,
                (0.9771f - 0.85f) * h
            );
            Raylib.DrawTexturePro(_atlas, SrcLifeStart, startDest, Vector2.Zero, 0f, Color.White);

            // 2. Life spheres (right-to-left)
            for (int i = 0; i < shown; i++)
            {
                float normLeft = 0.95f - (i + 1) * step;
                var ballDest = new Rectangle(
                    AnchorX(normLeft),
                    0.8854f * h,
                    ballW * refW,
                    ballH * h
                );
                Raylib.DrawTexturePro(_atlas, SrcLifeBall, ballDest, Vector2.Zero, 0f, Color.White);
            }

            // 3. Left end-cap (Interface_Life_End: width 0.0234, y [0.8813..0.9438])
            float endLeft = 0.929f - shown * step;
            var endDest = new Rectangle(
                AnchorX(endLeft),
                0.8813f * h,
                (0.9522f - 0.9288f) * refW,
                (0.9438f - 0.8813f) * h
            );
            Raylib.DrawTexturePro(_atlas, SrcLifeEnd, endDest, Vector2.Zero, 0f, Color.White);
        }
        else
        {
            int radius = Math.Max(8, (int)(ballH * h * 0.45f));
            int cy = (int)(0.914f * h);
            for (int i = 0; i < shown; i++)
            {
                int cx = (int)AnchorX(0.95f - (i + 1) * step + ballW * 0.5f);
                Raylib.DrawCircle(cx, cy, radius, Color.LightGray);
            }
        }
    }

    private void DrawBanner(int screenWidth, int screenHeight)
    {
        float fade = Math.Clamp(_bannerTimer / MathF.Min(0.6f, _bannerDuration), 0f, 1f);
        byte alpha = (byte)(fade * 255f);
        var col = new Color(_bannerColor.R, _bannerColor.G, _bannerColor.B, alpha);
        var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)(fade * 150f));
        float y = screenHeight * 0.12f;

        if (_font != null && _font.Ready)
        {
            float size = Math.Clamp(screenHeight * 0.042f, 22f, 38f);
            _font.DrawCentered(_bannerText, screenWidth * 0.5f + 2f, y + 2f, size, shadow);
            _font.DrawCentered(_bannerText, screenWidth * 0.5f, y, size, col);
        }
        else
        {
            int fontSize = 26;
            int tw = Raylib.MeasureText(_bannerText, fontSize);
            Raylib.DrawText(_bannerText, (screenWidth - tw) / 2 + 2, (int)y + 2, fontSize, shadow);
            Raylib.DrawText(_bannerText, (screenWidth - tw) / 2, (int)y, fontSize, col);
        }
    }

    private static void DrawHelp(int screenWidth)
    {
        int hx = screenWidth - 260;
        int hy = 18;
        Raylib.DrawRectangle(hx, hy, 245, 145, new Color((byte)15, (byte)20, (byte)30, (byte)190));
        Raylib.DrawRectangleLines(hx, hy, 245, 145, new Color((byte)80, (byte)100, (byte)140, (byte)120));
        Raylib.DrawText("CONTROLS (H to toggle):", hx + 10, hy + 8, 14, Color.Gold);
        Raylib.DrawText("WASD / Arrows: Roll Ball", hx + 10, hy + 30, 13, Color.White);
        Raylib.DrawText("Shift + Left/Right: Turn 90°", hx + 10, hy + 50, 13, Color.White);
        Raylib.DrawText("Q / E: Rotate Camera", hx + 10, hy + 70, 13, Color.White);
        Raylib.DrawText("Space (Hold): Overview Tilt", hx + 10, hy + 90, 13, Color.White);
        Raylib.DrawText("R: Respawn at Checkpoint", hx + 10, hy + 110, 13, Color.White);
        Raylib.DrawText("1 - 9: Switch Level Directly", hx + 10, hy + 130, 12, Color.SkyBlue);
    }

    public void Dispose()
    {
        _font?.Dispose();
        if (_atlas.Id != 0) Raylib.UnloadTexture(_atlas);
    }
}
