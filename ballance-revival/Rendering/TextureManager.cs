using System.Numerics;
using Raylib_cs;

namespace BallanceRevival.Rendering;

public class TextureManager : IDisposable
{
    private readonly string _textureDirectory;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fileMap = new(StringComparer.OrdinalIgnoreCase);
    private Texture2D _whiteTexture;

    public TextureManager(string textureDirectory)
    {
        _textureDirectory = textureDirectory;
        IndexDirectory();
        CreateDefaultTexture();
    }

    private void CreateDefaultTexture()
    {
        Image whiteImg = Raylib.GenImageColor(2, 2, Color.White);
        _whiteTexture = Raylib.LoadTextureFromImage(whiteImg);
        Raylib.UnloadImage(whiteImg);
    }

    private void IndexDirectory()
    {
        if (!Directory.Exists(_textureDirectory)) return;

        foreach (var file in Directory.EnumerateFiles(_textureDirectory))
        {
            string fileName = Path.GetFileName(file);
            _fileMap[fileName] = file;

            string nameNoExt = Path.GetFileNameWithoutExtension(file);
            _fileMap[nameNoExt] = file;
        }
    }

    public Texture2D GetTexture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return _whiteTexture;

        if (_textures.TryGetValue(name, out var cached))
            return cached;

        string? filePath = null;
        if (_fileMap.TryGetValue(name, out var found))
        {
            filePath = found;
        }
        else if (_fileMap.TryGetValue(name + ".bmp", out found))
        {
            filePath = found;
        }
        else if (_fileMap.TryGetValue(name + ".tga", out found))
        {
            filePath = found;
        }
        else
        {
            string directPath = Path.Combine(_textureDirectory, name);
            if (File.Exists(directPath)) filePath = directPath;
            else if (File.Exists(directPath + ".bmp")) filePath = directPath + ".bmp";
            else if (File.Exists(directPath + ".tga")) filePath = directPath + ".tga";
        }

        if (filePath != null && File.Exists(filePath))
        {
            Texture2D tex = default;
            if (ImageDecoder.TryLoad(filePath, out Image img))
            {
                tex = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
            }
            if (tex.Id > 0)
            {
                Raylib.GenTextureMipmaps(ref tex);
                Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
                Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
                _textures[name] = tex;
                return tex;
            }
        }

        return _whiteTexture;
    }

    public unsafe Vector3 AverageColor(string name)
    {
        string path = Path.Combine(_textureDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || !ImageDecoder.TryLoad(path, out Image img))
            return new Vector3(0.86f, 0.62f, 0.68f);

        byte* p = (byte*)img.Data;
        int w = img.Width;
        int h = img.Height;
        long r = 0, g = 0, b = 0, n = 0;
        int y0 = h / 5;
        int y1 = h / 2;
        for (int y = y0; y < y1; y += 6)
        {
            for (int x = 0; x < w; x += 6)
            {
                int i = (y * w + x) * 4;
                r += p[i];
                g += p[i + 1];
                b += p[i + 2];
                n++;
            }
        }
        Raylib.UnloadImage(img);
        if (n == 0) return new Vector3(0.86f, 0.62f, 0.68f);
        return new Vector3(r / (n * 255f), g / (n * 255f), b / (n * 255f));
    }

    public void Dispose()
    {
        foreach (var tex in _textures.Values)
        {
            if (tex.Id > 0)
                Raylib.UnloadTexture(tex);
        }
        _textures.Clear();

        if (_whiteTexture.Id > 0)
            Raylib.UnloadTexture(_whiteTexture);
    }
}
