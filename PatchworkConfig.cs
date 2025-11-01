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

    private readonly ConfigEntry<bool> _ShowAudioLog;
    public bool ShowAudioLog { get { return _ShowAudioLog.Value; } }

    private readonly ConfigEntry<double> _LogAudioDuration;
    public double LogAudioDuration { get { return _LogAudioDuration.Value; } }

    private readonly ConfigEntry<bool> _HideModdedAudioInLog;
    public bool HideModdedAudioInLog { get { return _HideModdedAudioInLog.Value; } }

    public PatchworkConfig(ConfigFile config)
    {
        _ShowAudioLog = config.Bind("Audio", "ShowAudioLog", false, "Show the audio play log on screen.");
        _LogAudioDuration = config.Bind("Audio", "LogAudioDuration", 5.0, "Duration (in seconds) to keep audio log entries visible.");
        _HideModdedAudioInLog = config.Bind("Audio", "HideModdedAudioInLog", true, "Hide modded audio clips from the audio log.");

        _DumpSprites = config.Bind("Sprites", "DumpSprites", false, "Enable dumping of sprites");
        _LoadSprites = config.Bind("Sprites", "LoadSprites", true, "Enable loading of custom sprites");

        _FullDumpKey = config.Bind("Keybinds", "FullDumpKey", UnityEngine.KeyCode.F6, "Key to load all scenes in the game and dump all their sprites. Only works when DumpSprites is enabled.");
    }
}