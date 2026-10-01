using BallanceRevival.Gameplay;
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

        _rollWood = GetSound("hit_wood_wood");
        _rollStone = GetSound("hit_stone_stone");
        _rollPaper = GetSound("hit_paper");
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

    public void PlayRollSound(BallMaterial material, float speed, bool isGrounded)
    {
        Sound rollSound = material switch
        {
            BallMaterial.Stone => _rollStone,
            BallMaterial.Paper => _rollPaper,
            _ => _rollWood
        };

        if (!Raylib.IsSoundValid(rollSound)) return;

        if (isGrounded && speed > 0.5f)
        {
            float vol = Math.Clamp((speed - 0.5f) / 25.0f, 0.05f, 0.7f);
            float pitch = Math.Clamp(0.7f + (speed / 35.0f) * 0.6f, 0.7f, 1.5f);

            Raylib.SetSoundVolume(rollSound, vol);
            Raylib.SetSoundPitch(rollSound, pitch);

            if (!_isRollingPlaying || _currentActiveRollSound.Stream.Buffer != rollSound.Stream.Buffer)
            {
                if (_isRollingPlaying && Raylib.IsSoundValid(_currentActiveRollSound))
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

    public void PlayImpact(BallMaterial material, float impactSpeed)
    {
        if (impactSpeed < 2.5f) return;

        string soundKey = material switch
        {
            BallMaterial.Stone => "hit_stone_stone",
            BallMaterial.Paper => "hit_paper",
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
        foreach (var sound in _sounds.Values)
        {
            if (Raylib.IsSoundValid(sound))
                Raylib.UnloadSound(sound);
        }
        _sounds.Clear();
        Raylib.CloseAudioDevice();
    }
}
