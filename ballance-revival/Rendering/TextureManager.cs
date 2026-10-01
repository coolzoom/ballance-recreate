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
            Texture2D tex = Raylib.LoadTexture(filePath);
            if (tex.Id > 0)
            {
                Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
                Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
                _textures[name] = tex;
                return tex;
            }
        }

        return _whiteTexture;
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
