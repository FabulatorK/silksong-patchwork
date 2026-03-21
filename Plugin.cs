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

    public static HashSet<string> PluginPackPaths = new();

    public static bool ShowAudioLog = false;
    public static bool ShowAudioList = false;
    public static bool ShowAnimationController = false;
    public static bool ShowTextLog = false;
    public static bool ShowSkinStatus = false;
    public static bool ShowDevProfiler = false;
    public static bool ShowDialogueEditor = false;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Config = new PatchworkConfig(base.Config);
        Logger.LogInfo($"Patchwork is loaded! Version: {MyPluginInfo.PLUGIN_VERSION}");

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
        ScanPluginPacks();

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
                T2DHandler.DumpAllT2DSprites();
                SceneTraverser.OnDumpCompleted();
                Logger.LogInfo($"Finished dumping sprites for scene {scene.name}");
            };
        }

        T2DHandler.PreloadAllT2DTextures();

        SceneManager.sceneLoaded += (scene, mode) => T2DHandler.ApplyT2DReplacementsInScene();

        SceneManager.sceneLoaded += (scene, mode) => AudioHandler.Reload();

        SceneManager.sceneLoaded += (scene, mode) => AnimationController.ClearAnimators();

        SceneManager.sceneLoaded += (scene, mode) => DialogueHandler.CheckForStaleKeys();

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        AudioHandler.ApplyPatches(harmony);
        AnimationController.ApplyPatches(harmony);
        SpriteLoader.ApplyPatches(harmony);
        VideoHandler.ApplyPatches(harmony);
        GUIHelper.InitInputBlocking();

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

    private void ScanPluginPacks()
    {
        Directory.GetDirectories(BepInEx.Paths.PluginPath, "Patchwork", SearchOption.AllDirectories).ToList().ForEach(dir =>
        {
            if (BasePath.Equals(dir))
                return;
            Logger.LogDebug($"Found Patchwork plugin pack at {dir}");
            PluginPackPaths.Add(dir);
        });
    }
    private static int _frameCounter = 0;
    private void Update()
    {
        DevProfiler.RecordFrame();
        DevProfiler.BeginUpdateTiming();

        // Disable/re-enable the keyboard device in Unity's new Input System based
        // on whether a Patchwork text field is focused. This blocks the game from
        // reading any key presses while the user is typing.
        GUIHelper.UpdateInputBlocking();

        // Patchwork's own hotkeys also skip when a text field is focused.
        if (!GUIHelper.IsTextFieldFocused)
        {
            if (Input.GetKeyDown(Config.FullDumpKey) && Config.DumpSprites)
                SceneTraverser.TraverseAllScenes();

            if (Input.GetKeyDown(Config.ShowAudioLogKey))
                ShowAudioLog = !ShowAudioLog;
            if (Input.GetKeyDown(Config.ShowAudioListKey))
                ShowAudioList = !ShowAudioList;
            if (Input.GetKeyDown(Config.ShowAnimationControllerKey))
                ShowAnimationController = !ShowAnimationController;
            if (Input.GetKeyDown(Config.ShowTextLogKey))
                ShowTextLog = !ShowTextLog;
            if (Input.GetKeyDown(Config.ShowSkinStatusKey))
                ShowSkinStatus = !ShowSkinStatus;
            if (Input.GetKeyDown(Config.ShowDevProfilerKey))
                ShowDevProfiler = !ShowDevProfiler;
            if (Input.GetKeyDown(Config.ShowDialogueEditorKey))
                ShowDialogueEditor = !ShowDialogueEditor;
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
            T2DHandler.ReloadSpritesInScene();
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
            T2DHandler.CheckForUninitializedSprites();

        DevProfiler.EndUpdateTiming();
    }

    private void LateUpdate()
    {
        T2DHandler.EnforceT2DReplacements();
    }

    private void OnDestroy()
    {
        GUIHelper.ForceRestoreGameActions();
    }

    private void OnGUI()
    {
        GUIHelper.BeginOnGUI();

        if (ShowAudioLog)
            AudioLog.DrawAudioLog();
        if (ShowAudioList)
            AudioList.DrawAudioList();
        if (ShowAnimationController)
            AnimationController.DrawAnimationController();
        if (ShowTextLog)
            TextLog.DrawTextLog();
        if (ShowSkinStatus)
            SkinStatus.Draw();
        if (ShowDevProfiler)
            DevProfiler.Draw();
        if (ShowDialogueEditor)
            DialogueEditor.Draw();
    }
    
    private void InitializeFolders()
    {
        IOUtil.EnsureDirectoryExists(SpriteDumper.DumpPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.LoadPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.AtlasLoadPath);
        IOUtil.EnsureDirectoryExists(T2DHandler.T2DDumpPath);
        IOUtil.EnsureDirectoryExists(T2DHandler.T2DAtlasLoadPath);
        IOUtil.EnsureDirectoryExists(AudioHandler.SoundFolder);
        IOUtil.EnsureDirectoryExists(VideoHandler.VideoLoadPath);
        IOUtil.EnsureDirectoryExists(DialogueHandler.TextDumpPath);
        IOUtil.EnsureDirectoryExists(DialogueHandler.TextLoadPath);
    }
}