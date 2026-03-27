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

    private static string PatchworkFolderName = "Patchwork";
    public static string BasePath { get { return Path.Combine(Paths.PluginPath, PatchworkFolderName); } }

    /// <summary>Paths of currently active packs in priority order.
    /// Forwards to PackManager — all handlers iterate this.</summary>
    public static IEnumerable<string> PluginPackPaths => PackManager.ActivePackPaths;

    // ── Legacy standalone shims (one release only, then removed) ─────────────
    public static bool ShowDialogueEditor = false;  // set by TextLogWindow (dead path); kept to avoid compile error
    public static bool ShowPackManager = false;

    // ── New unified UI ────────────────────────────────────────────────────────
    /// <summary>Corner badge always-on overlay for end users.</summary>
    public static bool ShowStatusOverlay = true;
    /// <summary>Tabbed Dev Hub window for creators.</summary>
    public static bool ShowDevHub = false;
    /// <summary>Currently active Dev Hub tab (0=Graphics … 4=Video).</summary>
    public static int  DevHubTab = 0;

    private void Awake()
    {
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

        SceneManager.sceneLoaded += (scene, mode) => T2DLoader.ApplyReplacementsInScene();

        SceneManager.sceneLoaded += (scene, mode) => AudioHandler.Reload();

        SceneManager.sceneLoaded += (scene, mode) => AnimationController.ClearAnimators();

        SceneManager.sceneLoaded += (scene, mode) => DialogueHandler.CheckForStaleKeys();

        SceneManager.sceneLoaded += (scene, mode) => PackManager.OnSceneLoaded(scene.name);

        SceneManager.sceneLoaded += (scene, mode) => StartCoroutine(T2DLoader.WarmSprites());

        SceneManager.sceneUnloaded += _ => T2DLoader.PruneStaleOriginals();
        SceneManager.sceneUnloaded += _ => GcUtil.CollectAtTransition();

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        AudioHandler.ApplyPatches(harmony);
        AnimationController.ApplyPatches(harmony);
        SpriteLoader.ApplyPatches(harmony);
        VideoHandler.ApplyPatches(harmony);
        GUIHelper.InitInputBlocking();
        
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


    private static int _frameCounter = 0;
    private static int _conditionPollFrames = 0;
    private const  int ConditionPollInterval = 120; // ~2 s at 60 fps

    private void Update()
    {
        DevProfiler.RecordFrame();
        DevProfiler.BeginUpdateTiming();

        // Poll hot-reload conditions periodically (for future non-scene condition types).
        if (++_conditionPollFrames >= ConditionPollInterval)
        {
            _conditionPollFrames = 0;
            PackManager.PollHotReloadConditions();
        }

        // Disable/re-enable the keyboard device in Unity's new Input System based
        // on whether a Patchwork text field is focused. This blocks the game from
        // reading any key presses while the user is typing.
        GUIHelper.UpdateInputBlocking();

        // Patchwork's own hotkeys also skip when a text field is focused.
        if (!GUIHelper.IsTextFieldFocused)
        {
            if (Input.GetKeyDown(Config.FullDumpKey) && Config.DumpSprites)
                SceneTraverser.TraverseAllScenes();

            // New keybind layout (Step 8 / gui-ux-redesign):
            //   Alpha1 → Pack Manager
            //   Alpha2 → Dev Hub (Graphics tab)
            //   Alpha3 → Dev Hub (Audio tab)
            //   Alpha4 → Dev Hub (Text tab)
            //   Alpha5 → Dev Hub (Performance tab)
            if (Input.GetKeyDown(Config.ShowPackManagerKey))
                ShowPackManager = !ShowPackManager;

            // Alpha2–5: open Dev Hub at the first four tabs.
            // Alpha2–7 → Dev Hub tabs. Pressing the active tab's key closes the hub.
            if (Input.GetKeyDown(Config.DevHubDashboardKey))
                DevHub.ToggleAt(DevHub.TabDashboard);
            if (Input.GetKeyDown(Config.DevHubGraphicsKey))
                DevHub.ToggleAt(DevHub.TabGraphics);
            if (Input.GetKeyDown(Config.DevHubTextKey))
                DevHub.ToggleAt(DevHub.TabText);
            if (Input.GetKeyDown(Config.DevHubVideoKey))
                DevHub.ToggleAt(DevHub.TabVideo);
            if (Input.GetKeyDown(Config.DevHubAudioKey))
                DevHub.ToggleAt(DevHub.TabAudio);
            if (Input.GetKeyDown(Config.DevHubPerformanceKey))
                DevHub.ToggleAt(DevHub.TabPerformance);
        }

        if (SpriteFileWatcher.ReloadSprites)
        {
            Logger.LogInfo("[Update] ReloadSprites flag set — triggering SpriteLoader.Reload()");
            SpriteFileWatcher.ReloadSprites = false;
            SpriteLoader.Reload();
        }

        if (SpriteFileWatcher.ReloadT2DSprites)
        {
            Logger.LogInfo("[Update] ReloadT2DSprites flag set — triggering T2DHandler.ReloadSpritesInScene()");
            SpriteFileWatcher.ReloadT2DSprites = false;
            T2DLoader.ReloadSpritesInScene();
        }

        if (AudioFileWatcher.ReloadAudio)
        {
            AudioFileWatcher.ReloadAudio = false;
            AudioHandler.Reload();
        }

        if (TextFileWatcher.ReloadText)
        {
            TextFileWatcher.ReloadText = false;
            DialogueHandler.Reload();
        }

        AnimationController.Update();

        // Periodic discovery sweep: find newly-instantiated renderers (Object.Instantiate
        // clones bypass the C# sprite setter, so our Harmony postfix never fires for them).
        // This uses FindObjectsByType which is expensive, so run it every 30 frames (~0.5s)
        // rather than every frame. LateUpdate enforcement only re-checks tracked renderers.
        if (++_frameCounter % 30 == 0)
            T2DLoader.CheckForUninitializedSprites();

        DevProfiler.EndUpdateTiming();
    }

    private void LateUpdate()
    {
        T2DLoader.EnforceT2DReplacements();
    }

    private void OnDestroy()
    {
        GUIHelper.ForceRestoreGameActions();
    }

    private void OnGUI()
    {
        GUIHelper.BeginOnGUI();

        // ── New unified UI ────────────────────────────────────────────────────
        if (ShowStatusOverlay)
            StatusOverlay.Draw();
        if (ShowDevHub)
            DevHub.Draw();

        // ── Core end-user window ──────────────────────────────────────────────
        if (ShowPackManager)
            PackManagerWindow.Draw();

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