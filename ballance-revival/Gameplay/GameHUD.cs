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
        // 1. Top Bar: Level & Score
        Raylib.DrawRectangle(0, 0, screenWidth, 50, new Color(15, 20, 30, 200));
        Raylib.DrawRectangleLines(0, 0, screenWidth, 50, new Color(80, 100, 140, 120));

        string levelTitle = $"BALLANCE - LEVEL {levelNumber:D2}";
        Raylib.DrawText(levelTitle, 20, 14, 22, Color.White);

        string scoreText = $"SCORE: {score:D6}";
        int scoreWidth = Raylib.MeasureText(scoreText, 22);
        Raylib.DrawText(scoreText, screenWidth - scoreWidth - 25, 14, 22, Color.Gold);

        // 2. Bottom Left: Ball Type & Speed
        int bottomY = screenHeight - 65;
        Raylib.DrawRectangle(15, bottomY, 240, 50, new Color(15, 20, 30, 200));
        Raylib.DrawRectangleLines(15, bottomY, 240, 50, new Color(80, 100, 140, 120));

        Color matColor = material switch
        {
            BallMaterial.Stone => new Color(160, 160, 170, 255),
            BallMaterial.Paper => new Color(240, 240, 220, 255),
            _ => new Color(205, 133, 63, 255)
        };

        Raylib.DrawCircle(35, bottomY + 25, 12, matColor);
        Raylib.DrawText($"{material.ToString().ToUpper()} BALL", 58, bottomY + 10, 18, Color.White);
        Raylib.DrawText($"Speed: {speed:F1} u/s", 58, bottomY + 30, 14, Color.LightGray);

        // 3. Bottom Right: Lives
        int livesWidth = 160;
        int livesX = screenWidth - livesWidth - 15;
        Raylib.DrawRectangle(livesX, bottomY, livesWidth, 50, new Color(15, 20, 30, 200));
        Raylib.DrawRectangleLines(livesX, bottomY, livesWidth, 50, new Color(80, 100, 140, 120));

        Raylib.DrawText("LIVES:", livesX + 15, bottomY + 16, 18, Color.White);
        for (int i = 0; i < Math.Min(lives, 5); i++)
        {
            Raylib.DrawCircle(livesX + 85 + i * 16, bottomY + 25, 6, Color.Gold);
        }

        // 4. Center Banner Notification (Checkpoints, Transformations, Level Complete)
        if (!string.IsNullOrEmpty(_bannerText))
        {
            float alpha = Math.Clamp(_bannerTimer / 0.5f, 0f, 1f);
            Color textCol = new Color(_bannerColor.R, _bannerColor.G, _bannerColor.B, (byte)(255 * alpha));
            int bannerWidth = Raylib.MeasureText(_bannerText, 32);
            int bx = (screenWidth - bannerWidth) / 2;
            int by = screenHeight / 4;

            Raylib.DrawRectangle(bx - 30, by - 12, bannerWidth + 60, 55, new Color((byte)10, (byte)15, (byte)25, (byte)(190 * alpha)));
            Raylib.DrawRectangleLines(bx - 30, by - 12, bannerWidth + 60, 55, new Color((byte)_bannerColor.R, (byte)_bannerColor.G, (byte)_bannerColor.B, (byte)(160 * alpha)));
            Raylib.DrawText(_bannerText, bx, by, 32, textCol);
        }

        // 5. Help overlay (toggle with H)
        if (_showHelp)
        {
            int hx = screenWidth - 260;
            int hy = 60;
            Raylib.DrawRectangle(hx, hy, 245, 145, new Color(15, 20, 30, 190));
            Raylib.DrawRectangleLines(hx, hy, 245, 145, new Color(80, 100, 140, 120));

            Raylib.DrawText("CONTROLS (H to toggle):", hx + 10, hy + 8, 14, Color.Gold);
            Raylib.DrawText("WASD / Arrows: Roll Ball", hx + 10, hy + 30, 13, Color.White);
            Raylib.DrawText("Shift + Left/Right: Turn 90°", hx + 10, hy + 50, 13, Color.White);
            Raylib.DrawText("Q / E: Rotate Camera", hx + 10, hy + 70, 13, Color.White);
            Raylib.DrawText("Space (Hold): Overview Tilt", hx + 10, hy + 90, 13, Color.White);
            Raylib.DrawText("R: Respawn at Checkpoint", hx + 10, hy + 110, 13, Color.White);
            Raylib.DrawText("1 - 9: Switch Level Directly", hx + 10, hy + 130, 12, Color.SkyBlue);
        }
    }
}
