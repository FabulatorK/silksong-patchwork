using BepInEx.Configuration;

namespace Patchwork;

public class PatchworkConfig
{
    private readonly ConfigEntry<bool> _DumpSprites;
    public bool DumpSprites { get { return _DumpSprites.Value; } }

    private readonly ConfigEntry<bool> _LoadSprites;
    public bool LoadSprites { get { return _LoadSprites.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _FullDumpKey = null;
    public UnityEngine.KeyCode FullDumpKey { get { return _FullDumpKey.Value; } }

    private readonly ConfigEntry<double> _LogAudioDuration;
    public double LogAudioDuration { get { return _LogAudioDuration.Value; } }

    private readonly ConfigEntry<bool> _HideModdedAudioInLog;
    public bool HideModdedAudioInLog { get { return _HideModdedAudioInLog.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAudioLog;
    public UnityEngine.KeyCode ShowAudioLogKey { get { return _ShowAudioLog.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAudioList;
    public UnityEngine.KeyCode ShowAudioListKey { get { return _ShowAudioList.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAnimationController;
    public UnityEngine.KeyCode ShowAnimationControllerKey { get { return _ShowAnimationController.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerPauseKey;
    public UnityEngine.KeyCode AnimationControllerPauseKey { get { return _AnimationControllerPauseKey.Value; } }

    public PatchworkConfig(ConfigFile config)
    {
        _LogAudioDuration = config.Bind("Audio", "LogAudioDuration", 5.0, "Duration (in seconds) to keep audio log entries visible.");
        _HideModdedAudioInLog = config.Bind("Audio", "HideModdedAudioInLog", true, "Hide modded audio clips from the audio log.");

        _DumpSprites = config.Bind("Sprites", "DumpSprites", false, "Enable dumping of sprites");
        _LoadSprites = config.Bind("Sprites", "LoadSprites", true, "Enable loading of custom sprites");

        _FullDumpKey = config.Bind("Keybinds", "FullDumpKey", UnityEngine.KeyCode.F6, "Key to load all scenes in the game and dump all their sprites. Only works when DumpSprites is enabled.");
        _ShowAudioLog = config.Bind("Keybinds", "ShowAudioLog", UnityEngine.KeyCode.Alpha1, "Key to toggle the audio log display.");
        _ShowAudioList = config.Bind("Keybinds", "ShowAudioList", UnityEngine.KeyCode.Alpha2, "Key to toggle the audio list display.");
        _ShowAnimationController = config.Bind("Keybinds", "ShowAnimationController", UnityEngine.KeyCode.Alpha3, "Key to toggle the animation controller display.");

        _AnimationControllerPauseKey = config.Bind("Keybinds", "AnimationControllerPauseKey", UnityEngine.KeyCode.Home, "Key to pause/unpause the selected animator in the animation controller.");
    }
}