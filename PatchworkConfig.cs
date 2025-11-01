using BepInEx.Configuration;

namespace Patchwork;

public class PatchworkConfig
{
    private readonly ConfigEntry<bool> _DumpSprites;
    public bool DumpSprites { get { return _DumpSprites.Value; } }

    private readonly ConfigEntry<bool> _LoadSprites;
    public bool LoadSprites { get { return _LoadSprites.Value; } }

    private readonly ConfigEntry<bool> _CacheAtlases;
    public bool CacheAtlases { get { return _CacheAtlases.Value; } }

    private readonly ConfigEntry<bool> _ReloadSceneOnChange;
    public bool ReloadSceneOnChange { get { return _ReloadSceneOnChange.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _FullDumpKey = null;
    public UnityEngine.KeyCode FullDumpKey { get { return _FullDumpKey.Value; } }

    private readonly ConfigEntry<bool> _LogSpriteLoading;
    public bool LogSpriteLoading { get { return _LogSpriteLoading.Value; } }

    private readonly ConfigEntry<bool> _LogSpriteDumping;
    public bool LogSpriteDumping { get { return _LogSpriteDumping.Value; } }

    private readonly ConfigEntry<bool> _LogSpriteWarnings;
    public bool LogSpriteWarnings { get { return _LogSpriteWarnings.Value; } }

    private readonly ConfigEntry<bool> _EnablePerformanceTimers;
    public bool EnablePerformanceTimers { get { return _EnablePerformanceTimers.Value; } }

    private readonly ConfigEntry<bool> _ShowAudioLog;
    public bool ShowAudioLog { get { return _ShowAudioLog.Value; } }

    private readonly ConfigEntry<double> _LogAudioDuration;
    public double LogAudioDuration { get { return _LogAudioDuration.Value; } }

    private readonly ConfigEntry<bool> _HideModdedAudioInLog;
    public bool HideModdedAudioInLog { get { return _HideModdedAudioInLog.Value; } }

    public PatchworkConfig(ConfigFile config)
    {
        _DumpSprites = config.Bind("General", "DumpSprites", false, "Enable dumping of sprites");
        _LoadSprites = config.Bind("General", "LoadSprites", true, "Enable loading of custom sprites");

        _CacheAtlases = config.Bind("Advanced", "CacheAtlases", true, "Enable caching of sprite atlases in memory to speed up sprite loading");

        _ReloadSceneOnChange = config.Bind("Reloading", "ReloadSceneOnChange", true, "Enable automatic scene reload when a sprite file changes.");

        _FullDumpKey = config.Bind("Keybinds", "FullDumpKey", UnityEngine.KeyCode.F6, "Key to load all scenes in the game and dump all their sprites. Only works when DumpSprites is enabled.");

        _LogSpriteLoading = config.Bind("Logging", "LogSpriteLoading", false, "Enable detailed logging of sprite loading operations. May slow down the game.");
        _LogSpriteDumping = config.Bind("Logging", "LogSpriteDumping", false, "Enable detailed logging of sprite dumping operations. May slow down the game.");
        _LogSpriteWarnings = config.Bind("Logging", "LogSpriteWarnings", false, "Enable logging of warnings related to sprite loading and dumping.");
        _EnablePerformanceTimers = config.Bind("Logging", "EnablePerformanceTimers", false, "Measure and log the time taken for sprite loading and dumping operations. May impact performance.");

        _ShowAudioLog = config.Bind("Audio", "ShowAudioLog", false, "Show the audio play log on screen.");
        _LogAudioDuration = config.Bind("Audio", "LogAudioDuration", 5.0, "Duration (in seconds) to keep audio log entries visible.");
        _HideModdedAudioInLog = config.Bind("Audio", "HideModdedAudioInLog", true, "Hide modded audio clips from the audio log.");
    }
}