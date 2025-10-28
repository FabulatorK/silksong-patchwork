using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Patchwork.Handlers;
using Patchwork.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patchwork;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static new PatchworkConfig Config;
    internal static SpriteFileWatcher FileWatcher;

    private static string PatchworkFolderName = "Patchwork";
    public static string BasePath { get { return Path.Combine(Paths.PluginPath, PatchworkFolderName); } }

    public static HashSet<string> PluginPackPaths = new();

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Config = new PatchworkConfig(base.Config);
        Logger.LogInfo($"Patchwork is loaded! Version: {MyPluginInfo.PLUGIN_VERSION}");

        FindPatchworkFolder();
        ScanPluginPacks();

        TexUtil.Initialize();
        InitializeFolders();
        FileWatcher = new SpriteFileWatcher(); // Needs config to be initialized first

        if (Config.DumpSprites)
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.LogInfo($"Dumping sprites for scene {scene.name}");
                var spriteCollections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
                foreach (var collection in spriteCollections)
                    SpriteDumper.DumpCollection(collection);
                SceneTraverser.OnDumpCompleted();
                Logger.LogInfo($"Finished dumping sprites for scene {scene.name}");
            };
        }

        if (Config.LoadSprites)
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.LogInfo($"Loading sprites for scene {scene.name}");
                var spriteCollections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
                foreach (var collection in spriteCollections)
                    SpriteLoader.LoadCollection(collection);
                Logger.LogInfo($"Finished loading sprites for scene {scene.name}");
            };
        }

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        AudioSourcePatch.ApplyPatches(harmony);
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

    private void Update()
    {
        if (Input.GetKeyDown(Config.ForceReloadKey) && Config.EnableForceReload)
            GameManager.instance.LoadScene(SceneManager.GetActiveScene().name);

        if (Input.GetKeyDown(Config.FullDumpKey) && Config.DumpSprites)
            SceneTraverser.TraverseAllScenes();

        if (SpriteFileWatcher.ReloadScene)
        {
            SpriteFileWatcher.ReloadScene = false;
            GameManager.instance.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
    
    private void InitializeFolders()
    {
        IOUtil.EnsureDirectoryExists(SpriteDumper.DumpPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.LoadPath);
        IOUtil.EnsureDirectoryExists(SpriteLoader.AtlasLoadPath);
        IOUtil.EnsureDirectoryExists(T2DHandler.T2DDumpPath);
    }
}
