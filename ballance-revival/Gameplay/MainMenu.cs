using System.Numerics;
using BallanceRevival.Core;
using BallanceRevival.Rendering;
using Raylib_cs;

namespace BallanceRevival.Gameplay;

public enum MenuPage
{
    Main,
    Levels,
    Highscore,
    Options,
    Credits,
    Pause,
    ConfirmQuit,
    ConfirmLeave
}

public enum MenuCommand
{
    None,
    Quit,
    StartLevel,
    Resume,
    RestartLevel,
    LeaveLevel
}

/// <summary>
/// Main, level, pause and credits screens. Button art and labels come from the
/// original Menu.nmo strings and Button01 textures.
/// </summary>
public sealed class MainMenu : IDisposable
{
    private readonly Texture2D _idle;
    private readonly Texture2D _hot;
    private readonly GameFont _font;
    private static readonly Color Ink = new(244, 246, 248, 255);

    // Exact button sub-rectangles from Menu.nmo (Button01_deselect/select.tga, 256x256)
    private static readonly Rectangle SrcMainButton = new(0f, 0.5137f * 256f, 256f, (0.7451f - 0.5137f) * 256f);
    private static readonly Rectangle SrcLevelButton = new(0f, 0.247f * 256f, 0.647f * 256f, (0.3686f - 0.247f) * 256f);
    private static readonly Rectangle SrcBackButton = new(0.2392f * 256f, 0.7529f * 256f, (0.8666f - 0.2392f) * 256f, (0.9843f - 0.7529f) * 256f);
    private static readonly Rectangle SrcYesNoButton = new(0f, 0.8235f * 256f, 0.226f * 256f, (0.9098f - 0.8235f) * 256f);

    private enum ButtonStyle { Main, Level, Back, YesNo, Setting }

    private readonly GraphicsSettings _settings;
    private MenuPage _optionsReturnPage = MenuPage.Main;

    private readonly Sound _click;
    private readonly bool _hasClick;
    private readonly Music _atmo;
    private readonly bool _hasAtmo;
    private bool _atmoOn;

    private readonly List<(string Label, Rectangle Rect, ButtonStyle Style)> _items = [];
    private int _columns = 1;
    private int _index;
    private float _creditsY = 160f;
    private int _laidOutW;
    private int _laidOutH;

    public MenuPage Page { get; private set; } = MenuPage.Main;
    public int ChosenLevel { get; private set; } = 1;
    public bool CoversGame =>
        Page is MenuPage.Pause or MenuPage.ConfirmLeave
        || (Page == MenuPage.Options && _optionsReturnPage == MenuPage.Pause);

    private static readonly string[] Credits =
    [
        "BALLANCE",
        "",
        "A Cyparade production",
        "All rights reserved.",
        "Berlin 2004.",
        "",
        "for",
        "LISA MARIE",
        "",
        "Monamur Musikproduktion",
        "Sound Design and Music",
        "Klaus Riech",
        "",
        "Game Design",
        "Project Management",
        "Art Direction",
        "Mirco Nierenz",
        "",
        "Lead Scripting",
        "Software Development",
        "Stephan Bludau",
        "",
        "Technical Direction",
        "Software Development",
        "Britta Fahrenbruch",
        "",
        "Lead Level Design",
        "Graphic Design",
        "Michael Herm",
        "",
        "Sky Design",
        "Graphic Design",
        "Constantin Rahn",
        "",
        "Interface Design",
        "Ulrich Weinberg",
        "",
        "Producing",
        "Ruth Meiners",
        "",
        "Lead Testing",
        "Level Design",
        "Matthias Bauer",
        "",
        "special thanks to Panda!"
    ];

    public MainMenu(string texturesDir, string soundsDir, GraphicsSettings settings)
    {
        _settings = settings;
        _idle = LoadTex(Path.Combine(texturesDir, "Button01_deselect.tga"));
        _hot = LoadTex(Path.Combine(texturesDir, "Button01_select.tga"));
        _font = new GameFont(Path.Combine(texturesDir, "Font_1.tga"));

        string clickPath = Path.Combine(soundsDir, "Menu_click.wav");
        if (File.Exists(clickPath))
        {
            _click = Raylib.LoadSound(clickPath);
            _hasClick = Raylib.IsSoundValid(_click);
        }

        string atmoPath = Path.Combine(soundsDir, "Menu_atmo.wav");
        if (File.Exists(atmoPath))
        {
            _atmo = Raylib.LoadMusicStream(atmoPath);
            _hasAtmo = Raylib.IsMusicValid(_atmo);
            if (_hasAtmo) Raylib.SetMusicVolume(_atmo, 0.55f);
        }
    }

    public void Show(MenuPage page)
    {
        Page = page;
        _index = 0;
        if (page == MenuPage.Credits) _creditsY = 180f;
        _laidOutW = 0;
    }

    public void ShowOptions(MenuPage returnPage)
    {
        _optionsReturnPage = returnPage;
        Show(MenuPage.Options);
    }

    public void SetAmbient(bool on)
    {
        if (!_hasAtmo) return;
        if (on && !_atmoOn)
        {
            Raylib.PlayMusicStream(_atmo);
            _atmoOn = true;
        }
        else if (!on && _atmoOn)
        {
            Raylib.StopMusicStream(_atmo);
            _atmoOn = false;
        }

        if (_atmoOn) Raylib.UpdateMusicStream(_atmo);
    }

    public MenuCommand Update(float dt)
    {
        int w = Raylib.GetScreenWidth();
        int h = Raylib.GetScreenHeight();
        if (w != _laidOutW || h != _laidOutH) Layout(w, h);

        if (Page == MenuPage.Credits)
        {
            _creditsY -= 28f * dt;
            if (_creditsY < -Credits.Length * 28f) _creditsY = h * 0.4f;
        }

        int prev = _index;
        int n = _items.Count;
        if (n == 0) return MenuCommand.None;

        if (Raylib.IsKeyPressed(KeyboardKey.Up)) _index = (_index - _columns + n) % n;
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) _index = (_index + _columns) % n;
        if (_columns > 1 && Raylib.IsKeyPressed(KeyboardKey.Left)) _index = (_index - 1 + n) % n;
        if (_columns > 1 && Raylib.IsKeyPressed(KeyboardKey.Right)) _index = (_index + 1) % n;

        if (Page == MenuPage.Options && _index < 6)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A))
            {
                PlayClick();
                AdjustOption(_index, -1);
                return MenuCommand.None;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D))
            {
                PlayClick();
                AdjustOption(_index, +1);
                return MenuCommand.None;
            }
        }

        Vector2 mouse = Raylib.GetMousePosition();
        bool overItem = false;
        for (int i = 0; i < n; i++)
        {
            if (Raylib.CheckCollisionPointRec(mouse, _items[i].Rect))
            {
                _index = i;
                overItem = true;
            }
        }

        bool activate = Raylib.IsKeyPressed(KeyboardKey.Enter)
            || Raylib.IsKeyPressed(KeyboardKey.KpEnter)
            || Raylib.IsKeyPressed(KeyboardKey.Space)
            || (Raylib.IsMouseButtonPressed(MouseButton.Left) && overItem);

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            PlayClick();
            return Escape();
        }

        if (activate)
        {
            PlayClick();
            return Activate();
        }

        if (_index != prev) PlayClick();
        return MenuCommand.None;
    }

    public void Draw(int w, int h)
    {
        if (w != _laidOutW || h != _laidOutH) Layout(w, h);

        // Subtle center column backing (M_BlackScreen from Menu.nmo: [0.3..0.7] in 4:3)
        float refW = h * (4f / 3f);
        float panelW = Page == MenuPage.Options
            ? MathF.Min(w, refW * 0.56f)
            : MathF.Min(w, refW * 0.44f);
        float panelX = (w - panelW) * 0.5f;
        byte panelAlpha = CoversGame ? (byte)(Page == MenuPage.Options ? 135 : 155) : (byte)85;
        Raylib.DrawRectangle((int)panelX, 0, (int)panelW, h, new Color((byte)14, (byte)18, (byte)28, panelAlpha));

        if (!CoversGame)
            DrawWordmark(w);

        if (Page == MenuPage.Highscore) DrawHighscore(w, h);
        if (Page == MenuPage.Options) DrawOptions(w, h);
        if (Page == MenuPage.Credits) DrawCredits(w, h);
        if (Page is MenuPage.ConfirmQuit or MenuPage.ConfirmLeave) DrawQuestion(w, h);

        for (int i = 0; i < _items.Count; i++)
            DrawButton(_items[i].Rect, _items[i].Label, _items[i].Style, i == _index);
    }

    private MenuCommand Escape()
    {
        switch (Page)
        {
            case MenuPage.Main:
                Show(MenuPage.ConfirmQuit);
                return MenuCommand.None;
            case MenuPage.Levels:
            case MenuPage.Highscore:
            case MenuPage.Credits:
            case MenuPage.ConfirmQuit:
                Show(MenuPage.Main);
                return MenuCommand.None;
            case MenuPage.Options:
                Show(_optionsReturnPage);
                return MenuCommand.None;
            case MenuPage.Pause:
                return MenuCommand.Resume;
            case MenuPage.ConfirmLeave:
                Show(MenuPage.Pause);
                return MenuCommand.None;
            default:
                return MenuCommand.None;
        }
    }

    private MenuCommand Activate()
    {
        switch (Page)
        {
            case MenuPage.Main:
                switch (_index)
                {
                    case 0: Show(MenuPage.Levels); break;
                    case 1: Show(MenuPage.Highscore); break;
                    case 2:
                        _optionsReturnPage = MenuPage.Main;
                        Show(MenuPage.Options);
                        break;
                    case 3: Show(MenuPage.Credits); break;
                    case 4: Show(MenuPage.ConfirmQuit); break;
                }
                return MenuCommand.None;

            case MenuPage.Levels:
                if (_index < 12)
                {
                    ChosenLevel = _index + 1;
                    Show(MenuPage.Main);
                    return MenuCommand.StartLevel;
                }
                Show(MenuPage.Main);
                return MenuCommand.None;

            case MenuPage.Highscore:
            case MenuPage.Credits:
                Show(MenuPage.Main);
                return MenuCommand.None;

            case MenuPage.Options:
                if (_index < 7)
                {
                    AdjustOption(_index, +1);
                    return MenuCommand.None;
                }
                Show(_optionsReturnPage);
                return MenuCommand.None;

            case MenuPage.Pause:
                switch (_index)
                {
                    case 0: return MenuCommand.Resume;
                    case 1:
                        _optionsReturnPage = MenuPage.Pause;
                        Show(MenuPage.Options);
                        return MenuCommand.None;
                    case 2: return MenuCommand.RestartLevel;
                    default: return OpenLeave();
                }

            case MenuPage.ConfirmQuit:
                if (_index == 0) return MenuCommand.Quit;
                Show(MenuPage.Main);
                return MenuCommand.None;

            case MenuPage.ConfirmLeave:
                if (_index == 0) return MenuCommand.LeaveLevel;
                Show(MenuPage.Pause);
                return MenuCommand.None;

            default:
                return MenuCommand.None;
        }
    }

    private MenuCommand OpenLeave()
    {
        Show(MenuPage.ConfirmLeave);
        return MenuCommand.None;
    }

    private void Layout(int w, int h)
    {
        _laidOutW = w;
        _laidOutH = h;
        _items.Clear();

        float refW = h * (4f / 3f);

        switch (Page)
        {
            case MenuPage.Main:
                _columns = 1;
                // Menu.nmo M_Main_But_1..5: height 0.0938*h, width 0.30*refW (4.26:1 matching 256x59.2 sprite)
                Column(w, h, refW, ["Start", "Highscore", "Optionen", "Credits", "Beenden"], h * 0.21f, ButtonStyle.Main);
                break;
            case MenuPage.Levels:
                _columns = 2;
                LevelGrid(w, h, refW);
                break;
            case MenuPage.Highscore:
            case MenuPage.Credits:
                _columns = 1;
                Column(w, h, refW, ["Zur\u00fcck"], h * 0.84f, ButtonStyle.Back);
                break;
            case MenuPage.Options:
                _columns = 1;
                OptionsColumn(w, h);
                break;
            case MenuPage.Pause:
                _columns = 1;
                Column(w, h, refW, ["Zur\u00fcck zum Spiel", "Optionen", "Level Neustart", "Level Beenden"], h * 0.24f, ButtonStyle.Main);
                break;
            case MenuPage.ConfirmQuit:
            case MenuPage.ConfirmLeave:
                _columns = 2;
                YesNoRow(w, h, refW);
                break;
        }

        if (_index >= _items.Count) _index = 0;
    }

    private void Column(int w, int h, float refW, string[] labels, float y, ButtonStyle style)
    {
        float bh = Math.Clamp(h * 0.0938f, 48f, 78f);
        float aspect = style == ButtonStyle.Back
            ? SrcBackButton.Width / SrcBackButton.Height
            : SrcMainButton.Width / SrcMainButton.Height;
        float bw = bh * aspect;
        float gap = bh * 0.24f;
        float x = (w - bw) / 2f;
        for (int i = 0; i < labels.Length; i++)
        {
            _items.Add((labels[i], new Rectangle(x, y, bw, bh), style));
            y += bh + gap;
        }
    }

    private void YesNoRow(int w, int h, float refW)
    {
        float bh = Math.Clamp(h * 0.062f, 36f, 52f);
        float bw = bh * (SrcYesNoButton.Width / SrcYesNoButton.Height);
        float gap = bh * 0.9f;
        float totalW = bw * 2f + gap;
        float x0 = (w - totalW) / 2f;
        float y = h * 0.50f;
        _items.Add(("Ja", new Rectangle(x0, y, bw, bh), ButtonStyle.YesNo));
        _items.Add(("Nein", new Rectangle(x0 + bw + gap, y, bw, bh), ButtonStyle.YesNo));
    }

    private void LevelGrid(int w, int h, float refW)
    {
        float bh = Math.Clamp(h * 0.056f, 34f, 48f);
        float bw = bh * (SrcLevelButton.Width / SrcLevelButton.Height);
        float gapX = bh * 0.6f;
        float gapY = bh * 0.28f;
        float totalW = bw * 2f + gapX;
        float x0 = (w - totalW) / 2f;
        float y0 = h * 0.20f;
        for (int i = 0; i < 12; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float x = x0 + col * (bw + gapX);
            float y = y0 + row * (bh + gapY);
            _items.Add(($"Level {i + 1}", new Rectangle(x, y, bw, bh), ButtonStyle.Level));
        }

        float backH = Math.Clamp(h * 0.0938f, 48f, 72f);
        float backW = backH * (SrcBackButton.Width / SrcBackButton.Height);
        float backY = MathF.Min(h - backH - 20f, y0 + 6 * (bh + gapY) + 14f);
        _items.Add(("Zur\u00fcck", new Rectangle((w - backW) / 2f, backY, backW, backH), ButtonStyle.Back));
    }

    private void AdjustOption(int index, int dir)
    {
        switch (index)
        {
            case 0: _settings.ToggleAtmosphere(); break;
            case 1: _settings.CycleBloom(dir); break;
            case 2: _settings.CycleSnow(dir); break;
            case 3: _settings.CycleGodRays(dir); break;
            case 4: _settings.ToggleSsr(); break;
            case 5: _settings.CycleDebugMode(dir); break;
            case 6: _settings.ResetDefaults(); break;
            default: return;
        }
        _settings.Save();
        _laidOutW = 0; // Refresh option labels on next frame
    }

    private void OptionsColumn(int w, int h)
    {
        string[] labels =
        [
            $"Atmosphere: {_settings.AtmosphereLabel}",
            $"HDR Bloom: {_settings.BloomLabel}",
            $"Snow: {_settings.SnowLabel}",
            $"God Rays: {_settings.GodRaysLabel}",
            $"Metallic SSR: {_settings.SsrLabel}",
            $"Debug View: {_settings.DebugModeLabel}",
            "Reset Defaults"
        ];

        float bh = Math.Clamp(h * 0.060f, 34f, 48f);
        float bw = MathF.Min(w * 0.56f, bh * 7.6f);
        float gap = bh * 0.22f;
        float x = (w - bw) * 0.5f;
        float y = CoversGame ? h * 0.16f : h * 0.19f;

        foreach (string label in labels)
        {
            _items.Add((label, new Rectangle(x, y, bw, bh), ButtonStyle.Setting));
            y += bh + gap;
        }

        float backH = Math.Clamp(h * 0.085f, 44f, 66f);
        float backW = backH * (SrcBackButton.Width / SrcBackButton.Height);
        float backY = MathF.Min(h - backH - 36f, y + 10f);
        _items.Add(("Zur\u00fcck", new Rectangle((w - backW) * 0.5f, backY, backW, backH), ButtonStyle.Back));
    }

    private void DrawButton(Rectangle dest, string label, ButtonStyle style, bool hot)
    {
        Rectangle src = style switch
        {
            ButtonStyle.Level => SrcLevelButton,
            ButtonStyle.Back => SrcBackButton,
            ButtonStyle.YesNo => SrcYesNoButton,
            _ => SrcMainButton
        };

        Texture2D tex = hot ? _hot : _idle;
        if (tex.Id != 0)
            Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f, Color.White);

        float size = style switch
        {
            ButtonStyle.Level => dest.Height * 0.56f,
            ButtonStyle.YesNo => dest.Height * 0.52f,
            ButtonStyle.Setting => dest.Height * 0.48f,
            _ => dest.Height * 0.42f
        };
        // Offset text center past the left chrome sphere cap (except Yes/No pill which has no sphere)
        float textCenterFrac = style switch
        {
            ButtonStyle.YesNo => 0.50f,
            ButtonStyle.Back => 0.60f,
            ButtonStyle.Level => 0.55f,
            ButtonStyle.Setting => 0.54f,
            _ => 0.56f
        };
        float textX = dest.X + dest.Width * textCenterFrac;
        float textY = dest.Y + (dest.Height - size) * (style == ButtonStyle.Main ? 0.42f : 0.48f);
        _font.DrawCentered(label, textX, textY, size, Ink);
    }

    private void DrawWordmark(int w)
    {
        const string word = "BALLANCE";
        const float size = 54f;
        float x = (w - _font.Measure(word, size)) / 2f;
        _font.Draw(word, x + 2f, 46f, size, new Color(28, 34, 42, 140));
        _font.Draw(word, x, 43f, size, Ink);
    }

    private void DrawQuestion(int w, int h)
    {
        string q = Page == MenuPage.ConfirmLeave ? "Level Beenden?" : "Beenden?";
        const float size = 32f;
        _font.DrawCentered(q, w / 2f, h * 0.30f, size, Ink);
    }

    private void DrawHighscore(int w, int h)
    {
        const float size = 22f;
        float y = 150f;
        for (int i = 1; i <= 10; i++)
        {
            _font.DrawCentered($"{i,2}      -----", w / 2f, y, size, new Color(220, 226, 232, 255));
            y += 34f;
        }
    }

    private void DrawOptions(int w, int h)
    {
        float titleY = CoversGame ? h * 0.08f : h * 0.125f;
        _font.DrawCentered("Graphics Options", w * 0.5f, titleY, 26f, Ink);

        string hint = "Click / Left-Right to change   |   F1-F6 Hotkeys   |   Esc Back";
        _font.DrawCentered(hint, w * 0.5f, h - 24f, 15f, new Color(200, 210, 224, 210));
    }

    private void DrawCredits(int w, int h)
    {
        float y = _creditsY;
        foreach (string line in Credits)
        {
            bool title = line is "BALLANCE" or "LISA MARIE";
            float size = title ? 28f : 20f;
            if (y > 100f && y < h - 140f && line.Length > 0)
            {
                var color = title ? Ink : new Color(210, 216, 222, 230);
                _font.DrawCentered(line, w / 2f, y, size, color);
            }
            y += line.Length == 0 ? 16f : size + 8f;
        }
    }

    private void PlayClick()
    {
        if (!_hasClick) return;
        Raylib.SetSoundVolume(_click, 0.7f);
        Raylib.PlaySound(_click);
    }

    private static Texture2D LoadTex(string path)
    {
        if (!File.Exists(path) || !ImageDecoder.TryLoad(path, out Image img))
            return default;
        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        if (tex.Id != 0)
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        return tex;
    }

    public void Dispose()
    {
        _font.Dispose();
        if (_idle.Id != 0) Raylib.UnloadTexture(_idle);
        if (_hot.Id != 0) Raylib.UnloadTexture(_hot);
        if (_hasClick) Raylib.UnloadSound(_click);
        if (_hasAtmo) Raylib.UnloadMusicStream(_atmo);
    }
}
