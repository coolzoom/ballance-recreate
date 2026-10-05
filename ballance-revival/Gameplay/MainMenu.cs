using System.Numerics;
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
    private readonly Rectangle _buttonSrc = new(2, 1, 252, 60);
    private readonly Sound _click;
    private readonly bool _hasClick;
    private readonly Music _atmo;
    private readonly bool _hasAtmo;
    private bool _atmoOn;

    private readonly List<(string Label, Rectangle Rect)> _items = [];
    private int _columns = 1;
    private int _index;
    private float _creditsY = 160f;
    private int _laidOutW;
    private int _laidOutH;

    public MenuPage Page { get; private set; } = MenuPage.Main;
    public int ChosenLevel { get; private set; } = 1;
    public bool CoversGame => Page is MenuPage.Pause or MenuPage.ConfirmLeave;

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

    public MainMenu(string texturesDir, string soundsDir)
    {
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

        if (!CoversGame)
            DrawWordmark(w);

        if (Page == MenuPage.Highscore) DrawHighscore(w, h);
        if (Page == MenuPage.Options) DrawOptions(w, h);
        if (Page == MenuPage.Credits) DrawCredits(w, h);
        if (Page is MenuPage.ConfirmQuit or MenuPage.ConfirmLeave) DrawQuestion(w, h);

        for (int i = 0; i < _items.Count; i++)
            DrawButton(_items[i].Rect, _items[i].Label, i == _index);
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
            case MenuPage.Options:
            case MenuPage.Credits:
            case MenuPage.ConfirmQuit:
                Show(MenuPage.Main);
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
                    case 2: Show(MenuPage.Options); break;
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
            case MenuPage.Options:
            case MenuPage.Credits:
                Show(MenuPage.Main);
                return MenuCommand.None;

            case MenuPage.Pause:
                return _index switch
                {
                    0 => MenuCommand.Resume,
                    1 => MenuCommand.RestartLevel,
                    _ => OpenLeave()
                };

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

        switch (Page)
        {
            case MenuPage.Main:
                _columns = 1;
                Column(w, h, ["Start", "Highscore", "Optionen", "Credits", "Beenden"], 200);
                break;
            case MenuPage.Levels:
                _columns = 2;
                LevelGrid(w, h);
                break;
            case MenuPage.Highscore:
            case MenuPage.Options:
            case MenuPage.Credits:
                _columns = 1;
                Column(w, h, ["Zur\u00fcck"], h - 120);
                break;
            case MenuPage.Pause:
                _columns = 1;
                Column(w, h, ["Zur\u00fcck zum Spiel", "Level Neustart", "Level Beenden"], h * 0.34f);
                break;
            case MenuPage.ConfirmQuit:
            case MenuPage.ConfirmLeave:
                _columns = 1;
                Column(w, h, ["Ja", "Nein"], h * 0.42f);
                break;
        }

        if (_index >= _items.Count) _index = 0;
    }

    private void Column(int w, int h, string[] labels, float y)
    {
        float bw = Math.Clamp(w * 0.40f, 340f, 540f);
        float bh = Math.Clamp(h * 0.072f, 44f, 62f);
        float x = (w - bw) / 2f;
        for (int i = 0; i < labels.Length; i++)
        {
            _items.Add((labels[i], new Rectangle(x, y, bw, bh)));
            y += bh + 8f;
        }
    }

    private void LevelGrid(int w, int h)
    {
        float bw = Math.Clamp(w * 0.28f, 240f, 380f);
        float bh = Math.Clamp(h * 0.062f, 40f, 52f);
        float gapX = 16f;
        float gapY = 8f;
        float totalW = bw * 2f + gapX;
        float x0 = (w - totalW) / 2f;
        float y0 = 168f;
        for (int i = 0; i < 12; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float x = x0 + col * (bw + gapX);
            float y = y0 + row * (bh + gapY);
            _items.Add(($"Level {i + 1}", new Rectangle(x, y, bw, bh)));
        }

        float backW = Math.Clamp(w * 0.28f, 240f, 380f);
        float backY = y0 + 6 * (bh + gapY) + 6f;
        _items.Add(("Zur\u00fcck", new Rectangle((w - backW) / 2f, backY, backW, bh)));
    }

    private void DrawButton(Rectangle dest, string label, bool hot)
    {
        Texture2D tex = hot ? _hot : _idle;
        if (tex.Id != 0)
            Raylib.DrawTexturePro(tex, _buttonSrc, dest, Vector2.Zero, 0f, Color.White);

        float size = dest.Height * 0.52f;
        // The chrome sphere sits on the left cap; center the label in the bar.
        float textX = dest.X + dest.Width * 0.58f;
        float textY = dest.Y + (dest.Height - size) / 2f - 1f;
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
        string[] lines =
        [
            "Steuerung",
            "",
            "W A S D / Pfeile     Rolle",
            "Q / E                Kamera drehen",
            "Leertaste            Kamerawinkel",
            "R                    Checkpoint",
            "H                    Hilfe",
            "Esc                  Pause"
        ];
        const float size = 22f;
        float y = 160f;
        foreach (string line in lines)
        {
            if (line.Length > 0)
            {
                var color = line == "Steuerung" ? Ink : new Color(210, 216, 222, 255);
                _font.DrawCentered(line, w / 2f, y, size, color);
            }
            y += 36f;
        }
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
