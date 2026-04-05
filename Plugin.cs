﻿﻿﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Patchwork.Handlers;
using Patchwork.Util;
using Patchwork.GUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Patchwork.Watchers;
using Patchwork.Packs;

namespace Patchwork;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static new PatchworkConfig Config;
    internal static SpriteFileWatcher SpriteFileWatcher;
    internal static AudioFileWatcher AudioFileWatcher;
    internal static TextFileWatcher TextFileWatcher;
    /// <summary>Singleton reference used by static handler classes that need to start coroutines.</summary>
    public static Plugin Instance { get; private set; }

    private static string PatchworkFolderName = "Patchwork";
    public static string BasePath { get { return Path.Combine(Paths.PluginPath, PatchworkFolderName); } }

    /// <summary>Paths of currently active packs in priority order.
    /// Forwards to PackManager — all handlers iterate this.</summary>
    public static IEnumerable<string> PluginPackPaths => PackManager.ActivePackPaths;

    public static bool ShowPackManager = false;

    // ── New unified UI ────────────────────────────────────────────────────────
    /// <summary>Corner badge overlay. Persisted via config — reads/writes through PatchworkConfig.</summary>
    public static bool ShowStatusOverlay
    {
        get => Config?.ShowStatusOverlay ?? true;
        set { if (Config != null) Config.ShowStatusOverlay = value; }
    }
    /// <summary>Tabbed Dev Hub window for creators.</summary>
    public static bool ShowDevHub = false;
    /// <summary>Currently active Dev Hub tab (0=Graphics … 4=Video).</summary>
    public static int  DevHubTab = 0;

    private void Awake()
    {
        Instance = this;
        // Plugin startup logic
        Logger = base.Logger;
        Config = new PatchworkConfig(base.Config);
        Logger.LogInfo($"Patchwork is loaded! Version: {MyPluginInfo.PLUGIN_VERSION}");

        int reserveMb = Config.HeapReserveMB < 0 ? GcUtil.SuggestReserveMB() : Config.HeapReserveMB;
        if (reserveMb > 0)
        {
            Logger.LogInfo($"[GC] Pre-warming Mono heap: {reserveMb} MB (system RAM: {UnityEngine.SystemInfo.systemMemorySize} MB)");
            GcUtil.PrewarmHeap((long)reserveMb * 1024 * 1024);
        }

        // Detect conflicting plugins that patch the same sprite hooks.
        foreach (var pluginInfo in BepInEx.Bootstrap.Chainloader.PluginInfos)
        {
            string guid = pluginInfo.Key;
            string name = pluginInfo.Value?.Metadata?.Name ?? guid;

            // Check both GUID and display name for known conflicts.
            bool isConflict =
                guid.IndexOf("Customizer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Customizer", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isConflict)
            {
                Logger.LogError(
                    $"[Patchwork] Conflicting plugin detected: '{name}' (GUID: {guid}). " +
                    $"Both mods patch sprite hooks and will conflict, causing broken " +
                    $"sprite loading and enemy AI issues. Remove this plugin — " +
                    $"Patchwork replaces Customizer's functionality.");
            }
        }

        FindPatchworkFolder();
        PackManager.Initialize();

        TexUtil.Initialize();
        InitializeFolders();
        AudioFileWatcher = new AudioFileWatcher();
        SpriteFileWatcher = new SpriteFileWatcher();
        TextFileWatcher = new TextFileWatcher();
        DevProfiler.Initialize();

        if (Config.DumpSprites)
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.LogInfo($"Dumping sprites for scene {scene.name}");
                var spriteCollections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
                foreach (var collection in spriteCollections)
                    SpriteDumper.DumpCollection(collection);
                T2DDumper.DumpAllT2DSprites();
                SceneTraverser.OnDumpCompleted();
                Logger.LogInfo($"Finished dumping sprites for scene {scene.name}");
            };
        }

        T2DLoader.PreloadAllTextures();
        VideoHandler.Reload();

        SceneManager.sceneLoaded += (scene, mode) => T2DLoader.ApplyReplacementsInScene();

        SceneManager.sceneLoaded += (scene, mode) => AudioHandler.Reload();

        SceneManager.sceneLoaded += (scene, mode) => AnimationController.ClearAnimators();

        SceneManager.sceneLoaded += (scene, mode) => DialogueHandler.CheckForStaleKeys();

        SceneManager.sceneLoaded += (scene, mode) => PackManager.OnSceneLoaded(scene.name);

        // Reset the burst window timer first, then run the scene-entry check.
        // Burst window: uninit sweep runs at 50ms intervals for the first 2s after each
        // scene load, catching Instantiate-spawned objects (Thread Storm projectiles, pool
        // objects activated in Start()) well within a single animation frame.
        // After 2s the sweep settles to the steady-state 200ms cadence.
        SceneManager.sceneLoaded += (scene, mode) => T2DLoader.OnSceneLoaded();
        SceneManager.sceneLoaded += (scene, mode) => T2DLoader.CheckForUninitializedSprites();

        SceneManager.sceneUnloaded += _ => T2DLoader.PruneStaleOriginals();
        SceneManager.sceneUnloaded += _ => T2DLoader.PruneSceneState();
        SceneManager.sceneUnloaded += _ => T2DLoader.ResetSceneSeed();
        SceneManager.sceneUnloaded += _ => GcUtil.CollectAtTransition();

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        AudioHandler.ApplyPatches(harmony);
        AnimationController.ApplyPatches(harmony);
        SpriteLoader.ApplyPatches(harmony);
        VideoHandler.ApplyPatches(harmony);
        GUIHelper.InitInputBlocking();

        // ── Wire handler → GUI events (keeps handlers free of GUI references) ──
        DialogueHandler.OnTextAccessed += (sheet, key, text) => TextLog.LogText(sheet, key, text);
        DialogueHandler.OnTextAccessed += (sheet, key, text) => { if (ShowDevHub) DialogueEditor.TrackText(sheet, key, text); };
        AudioHandler.OnAudioPlayed += (clip, src) => AudioLog.LogAudio(clip, src);
        T2DLoader.OnT2DTrigger        += (tex, sprite) => T2DLog.LogTrigger(tex, sprite);

        RawKeyboardLeakBlocker.ApplyPatches(harmony);

        StartCoroutine(AwakeDelayed(harmony));
    }

    private IEnumerator<object> AwakeDelayed(Harmony harmony)
    {
        yield return null;
        DialogueHandler.ApplyPatches(harmony);

        if (Config.DumpText)
            DialogueHandler.DumpText();
    }

    private void FindPatchworkFolder()
    {
        Directory.GetFiles(Paths.PluginPath, "Patchwork.dll", SearchOption.AllDirectories).ToList().ForEach(file =>
        {
            PatchworkFolderName = Path.GetFileName(Path.GetDirectoryName(file));
            Logger.LogDebug($"Found Patchwork folder name: {PatchworkFolderName}");
        });
    }



    private static int _conditionPollFrames = 0;
    private const  int ConditionPollInterval = 120; // ~2 s at 60 fps

    private void Update()
    {
        DevProfiler.RecordFrame();
        DevProfiler.BeginUpdateTiming();

        DevProfiler.StartOp();
        T2DLoader.CheckForUninitializedSprites();
        DevProfiler.RecordUninitMs(DevProfiler.StopOp("UninitCheck"));

        if (++_conditionPollFrames >= ConditionPollInterval)
        {
            _conditionPollFrames = 0;
            PackManager.PollHotReloadConditions();
        }

        GUIHelper.UpdateInputBlocking();
        HotkeyController.Poll();
        ReloadCoordinator.Poll();

        AnimationController.Update();

        DevProfiler.EndUpdateTiming();
    }

    private void LateUpdate()
    {
        DevProfiler.StartOp();
        T2DLoader.EnforceT2DReplacements();
        DevProfiler.RecordEnforceMs(DevProfiler.StopOp("EnforceT2D"));
    }

    private void OnDestroy()
    {
        GUIHelper.ForceRestoreGameActions();
        SpriteFileWatcher?.Dispose();
        AudioFileWatcher?.Dispose();
        TextFileWatcher?.Dispose();
    }

    private void OnGUI()
    {
        GUIHelper.BeginOnGUI();

        // Reset T2D log gate each pass — GraphicsPillar re-enables it if the T2D tab is visible.
        T2DLoader.IsT2DLogActive = false;

        // ── New unified UI ────────────────────────────────────────────────────
        if (ShowStatusOverlay)
            StatusOverlay.Draw();
        if (ShowDevHub)
            DevHub.Draw();

        // ── Core end-user window ──────────────────────────────────────────────
        if (ShowPackManager)
            PackManagerWindow.Draw();

        GUIHelper.EndOnGUI();
    }
    
    private void InitializeFolders()
    {
        IOUtil.EnsureDirectoryExists(SpriteDumper.DumpPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.LoadPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.AtlasLoadPath);
        IOUtil.EnsureDirectoryExists(T2DDumper.DumpPath);
        IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, "T2D"));
        IOUtil.EnsureDirectoryExists(T2DLoader.AtlasLoadPath);
        IOUtil.EnsureDirectoryExists(AudioHandler.SoundFolder);
        IOUtil.EnsureDirectoryExists(VideoHandler.VideoLoadPath);
        IOUtil.EnsureDirectoryExists(DialogueHandler.TextDumpPath);
        IOUtil.EnsureDirectoryExists(DialogueHandler.TextLoadPath);
    }
}