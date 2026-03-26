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

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAudioLog;
    public UnityEngine.KeyCode ShowAudioLogKey { get { return _ShowAudioLog.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAudioList;
    public UnityEngine.KeyCode ShowAudioListKey { get { return _ShowAudioList.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowAnimationController;
    public UnityEngine.KeyCode ShowAnimationControllerKey { get { return _ShowAnimationController.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerPauseKey;
    public UnityEngine.KeyCode AnimationControllerPauseKey { get { return _AnimationControllerPauseKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerNextFrameKey;
    public UnityEngine.KeyCode AnimationControllerNextFrameKey { get { return _AnimationControllerNextFrameKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerPrevFrameKey;
    public UnityEngine.KeyCode AnimationControllerPrevFrameKey { get { return _AnimationControllerPrevFrameKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _AnimationControllerFreezeKey;
    public UnityEngine.KeyCode AnimationControllerFreezeKey { get { return _AnimationControllerFreezeKey.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowTextLog;
    public UnityEngine.KeyCode ShowTextLogKey { get { return _ShowTextLog.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowSkinStatus;
    public UnityEngine.KeyCode ShowSkinStatusKey { get { return _ShowSkinStatus.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowDevProfiler;
    public UnityEngine.KeyCode ShowDevProfilerKey { get { return _ShowDevProfiler.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowDialogueEditor;
    public UnityEngine.KeyCode ShowDialogueEditorKey { get { return _ShowDialogueEditor.Value; } }

    private readonly ConfigEntry<UnityEngine.KeyCode> _ShowPackManager;
    public UnityEngine.KeyCode ShowPackManagerKey { get { return _ShowPackManager.Value; } }

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
        _ShowAudioLog = config.Bind("Keybinds", "ShowAudioLog", UnityEngine.KeyCode.Alpha1, "Key to toggle the audio log display.");
        _ShowAudioList = config.Bind("Keybinds", "ShowAudioList", UnityEngine.KeyCode.Alpha2, "Key to toggle the audio list display.");
        _ShowAnimationController = config.Bind("Keybinds", "ShowAnimationController", UnityEngine.KeyCode.Alpha3, "Key to toggle the animation controller display.");
        _ShowTextLog = config.Bind("Keybinds", "ShowTextLog", UnityEngine.KeyCode.Alpha4, "Key to toggle the text log display.");
        _ShowSkinStatus = config.Bind("Keybinds", "ShowSkinStatus", UnityEngine.KeyCode.Alpha5, "Key to toggle the skin status overlay.");
        _ShowDevProfiler = config.Bind("Keybinds", "ShowDevProfiler", UnityEngine.KeyCode.Alpha6, "Key to toggle the dev profiler overlay.");
        _ShowDialogueEditor = config.Bind("Keybinds", "ShowDialogueEditor", UnityEngine.KeyCode.Alpha7, "Key to toggle the in-game dialogue editor.");
        _ShowPackManager = config.Bind("Keybinds", "ShowPackManager", UnityEngine.KeyCode.Alpha8, "Key to toggle the resource pack manager.");

        _AnimationControllerPauseKey = config.Bind("Keybinds", "AnimationControllerPauseKey", UnityEngine.KeyCode.Home, "Key to pause/unpause the selected animator in the animation controller.");
        _AnimationControllerNextFrameKey = config.Bind("Keybinds", "AnimationControllerNextFrameKey", UnityEngine.KeyCode.PageUp, "Key to advance one frame in the selected animator in the animation controller when paused.");
        _AnimationControllerPrevFrameKey = config.Bind("Keybinds", "AnimationControllerPrevFrameKey", UnityEngine.KeyCode.Insert, "Key to go back one frame in the selected animator in the animation controller when paused.");
        _AnimationControllerFreezeKey = config.Bind("Keybinds", "AnimationControllerFreezeKey", UnityEngine.KeyCode.End, "Key to freeze/unfreeze the object of the selected animator in the animation controller.");

        _GCEveryNScenes = config.Bind("Dumping", "GCEveryNScenes", 10, "Run garbage collection every N scenes during full dump. Lower = more stable but slower.");

        _HeapReserveMB = config.Bind("Performance", "HeapReserveMB", -1,
            "Mono heap reserve in MB. Pre-allocates this much memory at startup to raise the GC's " +
            "high-water mark, reducing mid-gameplay stutter from heap-growth collections when custom " +
            "asset packs are loaded. -1 = auto (25% of system RAM, capped at 512 MB). 0 = disabled.");
    }
}