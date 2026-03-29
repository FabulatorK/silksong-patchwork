using BepInEx.Configuration;

namespace Patchwork;

public class PatchworkConfig
{
    private readonly ConfigEntry<bool> _DumpSprites;
    public bool DumpSprites { get { return _DumpSprites.Value; } }

    private readonly ConfigEntry<bool> _ConvertSpritesheets;
    public bool ConvertSpritesheets { get { return _ConvertSpritesheets.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _FullDumpKey = null;
    public UnityEngine.KeyCode FullDumpKey { get { return _FullDumpKey.Value; } }

    private readonly ConfigEntry<bool> _DumpText;
    public bool DumpText { get { return _DumpText.Value; } }

    private readonly ConfigEntry<double> _LogAudioDuration;
    public double LogAudioDuration { get { return _LogAudioDuration.Value; } }

    private readonly ConfigEntry<bool> _HideModdedAudioInLog;
    public bool HideModdedAudioInLog { get { return _HideModdedAudioInLog.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerPauseKey;
    public UnityEngine.KeyCode AnimationControllerPauseKey { get { return _AnimationControllerPauseKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerNextFrameKey;
    public UnityEngine.KeyCode AnimationControllerNextFrameKey { get { return _AnimationControllerNextFrameKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerPrevFrameKey;
    public UnityEngine.KeyCode AnimationControllerPrevFrameKey { get { return _AnimationControllerPrevFrameKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerFreezeKey;
    public UnityEngine.KeyCode AnimationControllerFreezeKey { get { return _AnimationControllerFreezeKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowPackManager;
    public UnityEngine.KeyCode ShowPackManagerKey { get { return _ShowPackManager.Value; } }

    // ── Dev Hub keybinds — Alpha2–7 map to all six tabs ─────────────────────
    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubDashboardKey;
    public UnityEngine.KeyCode DevHubDashboardKey { get { return _DevHubDashboardKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubGraphicsKey;
    public UnityEngine.KeyCode DevHubGraphicsKey { get { return _DevHubGraphicsKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubTextKey;
    public UnityEngine.KeyCode DevHubTextKey { get { return _DevHubTextKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubVideoKey;
    public UnityEngine.KeyCode DevHubVideoKey { get { return _DevHubVideoKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubAudioKey;
    public UnityEngine.KeyCode DevHubAudioKey { get { return _DevHubAudioKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _DevHubPerformanceKey;
    public UnityEngine.KeyCode DevHubPerformanceKey { get { return _DevHubPerformanceKey.Value; } }

    private readonly ConfigEntry<double> _TextLogDuration;
    public double TextLogDuration { get { return _TextLogDuration.Value; } }

    private readonly ConfigEntry<int> _TextLogMaxVisible;
    public int TextLogMaxVisible { get { return _TextLogMaxVisible.Value; } }

    private readonly ConfigEntry<int> _AudioLogMaxVisible;
    public int AudioLogMaxVisible { get { return _AudioLogMaxVisible.Value; } }

    // GC every N scenes during full dump
    private readonly ConfigEntry<int> _GCEveryNScenes;
    public int GCEveryNScenes { get { return _GCEveryNScenes.Value; } }

    private readonly ConfigEntry<int> _HeapReserveMB;
    /// <summary>
    /// MB to pre-allocate at startup to raise the Mono GC's high-water mark.
    /// -1 = auto (25% of system RAM, capped at 512 MB). 0 = disabled.
    /// </summary>
    public int HeapReserveMB => _HeapReserveMB.Value;

    private readonly ConfigEntry<bool> _ShowStatusOverlay;
    /// <summary>Show/hide the HUD status badge. Persists across sessions.</summary>
    public bool ShowStatusOverlay
    {
        get => _ShowStatusOverlay.Value;
        set => _ShowStatusOverlay.Value = value;
    }

    public PatchworkConfig(ConfigFile config)
    {
        _LogAudioDuration = config.Bind("GUI", "LogAudioDuration", 5.0, "Duration (in seconds) to keep audio log entries visible.");
        _HideModdedAudioInLog = config.Bind("GUI", "HideModdedAudioInLog", true, "Hide modded audio clips from the audio log.");
        _TextLogDuration = config.Bind("GUI", "TextLogDuration", 10.0, "Duration (in seconds) for bumped log entries to fade out after leaving the visible slots.");
        _TextLogMaxVisible = config.Bind("GUI", "TextLogMaxVisible", 15, "Number of latest text log entries always visible. Entries beyond this fade out over TextLogDuration seconds. (Range: 5-50)");
        _AudioLogMaxVisible = config.Bind("GUI", "AudioLogMaxVisible", 15, "Number of latest audio log entries always visible. Entries beyond this fade out over LogAudioDuration seconds. (Range: 5-50)");

        _DumpSprites = config.Bind("Dumping", "DumpSprites", false, "Enable dumping of sprites");
        _DumpText = config.Bind("Dumping", "DumpText", false, "Enable dumping of text when the game starts.");
        _ConvertSpritesheets = config.Bind("Dumping", "ConvertSpritesheets", false, "Automatically convert modded spritesheets to individual Patchwork-compatible PNGs.");

        _FullDumpKey = config.Bind("Keybinds", "FullDumpKey", UnityEngine.KeyCode.F6, "Key to load all scenes in the game and dump all their sprites. Only works when DumpSprites is enabled.");
        _ShowPackManager = config.Bind("Keybinds", "ShowPackManager", UnityEngine.KeyCode.Alpha1, "Key to toggle the Pack Manager.");

        // Dev Hub tab keybinds (Alpha2–7 = all six tabs). Pressing the active tab's key closes the Hub.
        _DevHubDashboardKey   = config.Bind("Keybinds", "DevHubDashboard",   UnityEngine.KeyCode.Alpha2, "Open Dev Hub at Dashboard tab (press again to close).");
        _DevHubGraphicsKey    = config.Bind("Keybinds", "DevHubGraphics",    UnityEngine.KeyCode.Alpha3, "Open Dev Hub at Graphics tab (press again to close).");
        _DevHubTextKey        = config.Bind("Keybinds", "DevHubText",        UnityEngine.KeyCode.Alpha4, "Open Dev Hub at Text tab (press again to close).");
        _DevHubVideoKey       = config.Bind("Keybinds", "DevHubVideo",       UnityEngine.KeyCode.Alpha5, "Open Dev Hub at Video tab (press again to close).");
        _DevHubAudioKey       = config.Bind("Keybinds", "DevHubAudio",       UnityEngine.KeyCode.Alpha6, "Open Dev Hub at Audio tab (press again to close).");
        _DevHubPerformanceKey = config.Bind("Keybinds", "DevHubPerformance", UnityEngine.KeyCode.Alpha7, "Open Dev Hub at Performance tab (press again to close).");

        _AnimationControllerPauseKey = config.Bind("Keybinds", "AnimationControllerPauseKey", UnityEngine.KeyCode.Home, "Key to pause/unpause the selected animator in the animation controller.");
        _AnimationControllerNextFrameKey = config.Bind("Keybinds", "AnimationControllerNextFrameKey", UnityEngine.KeyCode.PageUp, "Key to advance one frame in the selected animator in the animation controller when paused.");
        _AnimationControllerPrevFrameKey = config.Bind("Keybinds", "AnimationControllerPrevFrameKey", UnityEngine.KeyCode.Insert, "Key to go back one frame in the selected animator in the animation controller when paused.");
        _AnimationControllerFreezeKey = config.Bind("Keybinds", "AnimationControllerFreezeKey", UnityEngine.KeyCode.End, "Key to freeze/unfreeze the object of the selected animator in the animation controller.");

        _GCEveryNScenes = config.Bind("Dumping", "GCEveryNScenes", 10, "Run garbage collection every N scenes during full dump. Lower = more stable but slower.");

        _HeapReserveMB = config.Bind("Performance", "HeapReserveMB", -1,
            "Mono heap reserve in MB. Pre-allocates this much memory at startup to raise the GC's " +
            "high-water mark, reducing mid-gameplay stutter from heap-growth collections when custom " +
            "asset packs are loaded. -1 = auto (25% of system RAM, capped at 512 MB). 0 = disabled.");

        _ShowStatusOverlay = config.Bind("GUI", "ShowStatusOverlay", true,
            "Show the HUD status badge in the bottom-left corner. Can also be toggled from the Pack Manager.");
    }
}