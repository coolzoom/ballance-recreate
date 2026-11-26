using BallanceRevival.Gameplay;
using BallanceRevival.Physics;
using Raylib_cs;

namespace BallanceRevival.Core;

public class AudioManager : IDisposable
{
    private readonly string _soundDirectory;
    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);

    private Sound _rollWood;
    private Sound _rollStone;
    private Sound _rollPaper;
    private bool _isRollingPlaying = false;
    private Sound _currentActiveRollSound;

    private Music _levelMusic;
    private bool _hasLevelMusic;
    private int _musicTheme = -1;

    public AudioManager(string soundDirectory)
    {
        _soundDirectory = soundDirectory;
        Raylib.InitAudioDevice();
        LoadSounds();
    }

    private void LoadSounds()
    {
        LoadSoundFile("checkpoint", "Misc_Checkpoint.wav");
        LoadSoundFile("trafo", "Misc_Trafo.wav");
        LoadSoundFile("extralife", "Extra_Life_Blob.wav");
        LoadSoundFile("extrapoint", "Extra_Hit.wav");
        LoadSoundFile("fall", "Misc_Fall.wav");
        LoadSoundFile("start", "Misc_StartLevel.wav");
        LoadSoundFile("ufo", "Misc_UFO.wav");
        LoadSoundFile("ufo_anim", "Misc_UFO_anim.wav");

        // Impacts
        LoadSoundFile("hit_wood_wood", "Hit_Wood_Wood.wav");
        LoadSoundFile("hit_wood_stone", "Hit_Wood_Stone.wav");
        LoadSoundFile("hit_wood_metal", "Hit_Wood_Metal.wav");
        LoadSoundFile("hit_stone_stone", "Hit_Stone_Stone.wav");
        LoadSoundFile("hit_stone_wood", "Hit_Stone_Wood.wav");
        LoadSoundFile("hit_stone_metal", "Hit_Stone_Metal.wav");
        LoadSoundFile("hit_paper", "Hit_Paper.wav");

        // Authentic looping roll sounds from Sounds/ (surface-material aware)
        LoadSoundFile("roll_wood_stone", "Roll_Wood_Stone.wav");
        LoadSoundFile("roll_wood_wood", "Roll_Wood_Wood.wav");
        LoadSoundFile("roll_wood_metal", "Roll_Wood_Metal.wav");
        LoadSoundFile("roll_stone_stone", "Roll_Stone_Stone.wav");
        LoadSoundFile("roll_stone_wood", "Roll_Stone_Wood.wav");
        LoadSoundFile("roll_stone_metal", "Roll_Stone_Metal.wav");
        LoadSoundFile("roll_paper", "Roll_Paper.wav");

        _rollWood = GetSound("roll_wood_stone");
        _rollStone = GetSound("roll_stone_stone");
        _rollPaper = GetSound("roll_paper");
    }

    public void UpdateLevelMusic(int levelNum)
    {
        int theme = ((Math.Max(levelNum, 1) - 1) % 5) + 1;
        if (theme != _musicTheme)
        {
            StopLevelMusic();
            string path = Path.Combine(_soundDirectory, $"Music_Theme_{theme}_1.wav");
            if (File.Exists(path))
            {
                _levelMusic = Raylib.LoadMusicStream(path);
                _hasLevelMusic = Raylib.IsMusicValid(_levelMusic);
                if (_hasLevelMusic)
                {
                    Raylib.SetMusicVolume(_levelMusic, 0.38f);
                    Raylib.PlayMusicStream(_levelMusic);
                    _musicTheme = theme;
                }
            }
        }
        else if (_hasLevelMusic)
        {
            if (!Raylib.IsMusicStreamPlaying(_levelMusic))
                Raylib.PlayMusicStream(_levelMusic);
            Raylib.UpdateMusicStream(_levelMusic);
        }
    }

    public void StopLevelMusic()
    {
        if (_hasLevelMusic)
        {
            Raylib.StopMusicStream(_levelMusic);
            Raylib.UnloadMusicStream(_levelMusic);
            _hasLevelMusic = false;
        }
        _musicTheme = -1;
    }

    private void LoadSoundFile(string key, string fileName)
    {
        string fullPath = Path.Combine(_soundDirectory, fileName);
        if (File.Exists(fullPath))
        {
            Sound sound = Raylib.LoadSound(fullPath);
            if (Raylib.IsSoundValid(sound))
            {
                _sounds[key] = sound;
            }
        }
    }

    private Sound GetSound(string key)
    {
        return _sounds.TryGetValue(key, out var s) ? s : default;
    }

    public void PlayRollSound(BallMaterial material, float speed, bool isGrounded, SurfaceKind surface = SurfaceKind.Stone)
    {
        string key = (material, surface) switch
        {
            (BallMaterial.Paper, _) => "roll_paper",
            (BallMaterial.Stone, SurfaceKind.Wood) => "roll_stone_wood",
            (BallMaterial.Stone, SurfaceKind.Metal) => "roll_stone_metal",
            (BallMaterial.Stone, _) => "roll_stone_stone",
            (_, SurfaceKind.Wood) => "roll_wood_wood",
            (_, SurfaceKind.Metal) => "roll_wood_metal",
            _ => "roll_wood_stone"
        };
        Sound rollSound = GetSound(key);
        if (!Raylib.IsSoundValid(rollSound))
        {
            rollSound = material switch
            {
                BallMaterial.Stone => _rollStone,
                BallMaterial.Paper => _rollPaper,
                _ => _rollWood
            };
        }

        if (!Raylib.IsSoundValid(rollSound)) return;

        if (isGrounded && speed > 0.5f)
        {
            float vol = Math.Clamp((speed - 0.5f) / 25.0f, 0.05f, 0.7f);
            float pitch = Math.Clamp(0.7f + (speed / 35.0f) * 0.6f, 0.7f, 1.5f);

            Raylib.SetSoundVolume(rollSound, vol);
            Raylib.SetSoundPitch(rollSound, pitch);

            if (!_isRollingPlaying || _currentActiveRollSound.Stream.Buffer != rollSound.Stream.Buffer || !Raylib.IsSoundPlaying(rollSound))
            {
                if (_isRollingPlaying && _currentActiveRollSound.Stream.Buffer != rollSound.Stream.Buffer && Raylib.IsSoundValid(_currentActiveRollSound))
                {
                    Raylib.StopSound(_currentActiveRollSound);
                }
                Raylib.PlaySound(rollSound);
                _currentActiveRollSound = rollSound;
                _isRollingPlaying = true;
            }
        }
        else
        {
            if (_isRollingPlaying && Raylib.IsSoundValid(_currentActiveRollSound))
            {
                Raylib.StopSound(_currentActiveRollSound);
                _isRollingPlaying = false;
            }
        }
    }

    public void PlayImpact(BallMaterial material, float impactSpeed, SurfaceKind surface = SurfaceKind.Stone)
    {
        if (impactSpeed < 2.5f) return;

        string soundKey = (material, surface) switch
        {
            (BallMaterial.Paper, _) => "hit_paper",
            (BallMaterial.Stone, SurfaceKind.Wood) => "hit_stone_wood",
            (BallMaterial.Stone, SurfaceKind.Metal) => "hit_stone_metal",
            (BallMaterial.Stone, _) => "hit_stone_stone",
            (_, SurfaceKind.Wood) => "hit_wood_wood",
            (_, SurfaceKind.Metal) => "hit_wood_metal",
            _ => "hit_wood_stone"
        };

        var sound = GetSound(soundKey);
        if (Raylib.IsSoundValid(sound))
        {
            float vol = Math.Clamp(impactSpeed / 20.0f, 0.1f, 1.0f);
            Raylib.SetSoundVolume(sound, vol);
            Raylib.PlaySound(sound);
        }
    }

    public void PlayCheckpoint() => Play("checkpoint");
    public void PlayTransformer() => Play("trafo");
    public void PlayExtraLife() => Play("extralife");
    public void PlayExtraPoint() => Play("extrapoint");
    public void PlayFall() => Play("fall");
    public void PlayLevelStart() => Play("start");
    public void PlayLevelComplete() => Play("ufo");

    private void Play(string key)
    {
        if (_sounds.TryGetValue(key, out var sound) && Raylib.IsSoundValid(sound))
        {
            Raylib.SetSoundVolume(sound, 0.85f);
            Raylib.PlaySound(sound);
        }
    }

    public void Dispose()
    {
        StopLevelMusic();
        foreach (var sound in _sounds.Values)
        {
            if (Raylib.IsSoundValid(sound))
                Raylib.UnloadSound(sound);
        }
        _sounds.Clear();
        Raylib.CloseAudioDevice();
    }
}
