using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Gameplay;

public class GameHUD
{
    private string _bannerText = string.Empty;
    private float _bannerTimer = 0f;
    private Color _bannerColor = Color.Gold;
    private bool _showHelp = false;

    public void ShowBanner(string text, Color color, float duration = 2.5f)
    {
        _bannerText = text;
        _bannerColor = color;
        _bannerTimer = duration;
    }

    public void Update(float dt)
    {
        if (_bannerTimer > 0f)
        {
            _bannerTimer -= dt;
            if (_bannerTimer <= 0f)
            {
                _bannerText = string.Empty;
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.H))
        {
            _showHelp = !_showHelp;
        }
    }

    public void Draw(int screenWidth, int screenHeight, int levelNumber, int score, int lives, BallMaterial material, float speed)
    {
        DrawScore(screenWidth, screenHeight, score);
        DrawLives(screenWidth, screenHeight, lives);

        if (_showHelp)
            DrawHelp(screenWidth);
    }

    // Original in-game HUD is only the score plate and the life spheres.
    private static void DrawScore(int screenWidth, int screenHeight, int score)
    {
        const int plateW = 156;
        const int plateH = 46;
        int x = 78;
        int y = screenHeight - plateH - 28;
        var plate = new Rectangle(x, y, plateW, plateH);
        var metal = new Color((byte)186, (byte)196, (byte)206, (byte)230);
        var metalDim = new Color((byte)120, (byte)132, (byte)144, (byte)210);

        Raylib.DrawRectangleRounded(plate, 0.35f, 8, new Color((byte)28, (byte)34, (byte)42, (byte)210));
        Raylib.DrawRectangleRoundedLines(plate, 0.35f, 8, metal);
        Raylib.DrawRectangleRoundedLines(new Rectangle(x + 3, y + 3, plateW - 6, plateH - 6), 0.32f, 8, metalDim);

        DrawScoreSwoosh(x, y, plateH, metal, metalDim);

        string digits = Math.Max(0, score).ToString();
        const int font = 28;
        int textW = MeasureSpaced(digits, font, 3);
        int tx = x + (plateW - textW) / 2;
        int ty = y + (plateH - font) / 2 - 1;
        DrawSpaced(digits, tx, ty, font, 3, new Color((byte)236, (byte)240, (byte)244, (byte)255));
    }

    private static void DrawScoreSwoosh(int boxX, int boxY, int boxH, Color metal, Color metalDim)
    {
        // Two parallel strokes that leave the left of the plate and curl down, matching the original bracket.
        DrawCurve(boxX + 2, boxY + 10, boxX - 46, boxY + boxH + 10, -22f, metal, 2.2f);
        DrawCurve(boxX + 2, boxY + 18, boxX - 34, boxY + boxH + 16, -16f, metalDim, 1.6f);
    }

    private static void DrawLives(int screenWidth, int screenHeight, int lives)
    {
        int shown = Math.Clamp(lives, 0, 8);
        if (shown == 0) return;

        const int radius = 13;
        const int gap = 8;
        int rowW = shown * (radius * 2) + (shown - 1) * gap;
        int cx0 = screenWidth - 56 - rowW + radius;
        int cy = screenHeight - 52;
        var metal = new Color((byte)176, (byte)186, (byte)196, (byte)230);

        float left = cx0 - radius - 6;
        float right = cx0 + (shown - 1) * (radius * 2 + gap) + radius + 6;
        float top = cy - radius - 8;
        float bottom = cy + radius + 10;
        DrawCurve(left, top, left - 4, bottom, 16f, metal, 2f);
        DrawCurve(right, top, right + 4, bottom, -16f, metal, 2f);

        for (int i = 0; i < shown; i++)
        {
            int cx = cx0 + i * (radius * 2 + gap);
            DrawLifeSphere(cx, cy, radius);
        }
    }

    private static void DrawLifeSphere(int cx, int cy, int radius)
    {
        Raylib.DrawCircle(cx, cy, radius, new Color((byte)92, (byte)100, (byte)110, (byte)255));
        Raylib.DrawCircle(cx - 1, cy - 1, radius - 2, new Color((byte)168, (byte)176, (byte)186, (byte)255));
        Raylib.DrawCircle(cx - 3, cy - 4, radius - 6, new Color((byte)226, (byte)232, (byte)238, (byte)255));
        Raylib.DrawCircle(cx - 5, cy - 6, 3.5f, new Color((byte)255, (byte)255, (byte)255, (byte)230));
    }

    private static void DrawCurve(float x0, float y0, float x1, float y1, float bulge, Color color, float thick)
    {
        const int steps = 14;
        Vector2 prev = new(x0, y0);
        float dx = x1 - x0;
        float dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;
        float nx = -dy / len;
        float ny = dx / len;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float bow = MathF.Sin(t * MathF.PI) * bulge;
            var p = new Vector2(x0 + dx * t + nx * bow, y0 + dy * t + ny * bow);
            Raylib.DrawLineEx(prev, p, thick, color);
            prev = p;
        }
    }

    private static int MeasureSpaced(string text, int fontSize, int spacing)
    {
        int w = 0;
        for (int i = 0; i < text.Length; i++)
        {
            w += Raylib.MeasureText(text[i].ToString(), fontSize);
            if (i + 1 < text.Length) w += spacing;
        }
        return w;
    }

    private static void DrawSpaced(string text, int x, int y, int fontSize, int spacing, Color color)
    {
        int cursor = x;
        foreach (char ch in text)
        {
            string s = ch.ToString();
            Raylib.DrawText(s, cursor, y, fontSize, color);
            cursor += Raylib.MeasureText(s, fontSize) + spacing;
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
}
