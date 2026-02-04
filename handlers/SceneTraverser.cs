using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.Handlers;

public static class SceneTraverser
{
    private static Queue<string> sceneQueue = new();
    private static int scenesProcessed = 0;
    private static int GCEveryNScenes => Plugin.Config.GCEveryNScenes;
    private static bool _isTraversing = false;
    private static int _scenesSinceLastGC = 0;

    public static void TraverseAllScenes()
    {
        Plugin.Logger.LogInfo("Starting scene traversal for full sprite dump...");
        _isTraversing = true;
        _scenesSinceLastGC = 0;
        sceneQueue.Clear();
        scenesProcessed = 0;

        var teleportMap = SceneTeleportMap.GetTeleportMap();
        foreach (var sceneName in teleportMap.Keys)
        {
            if (!sceneQueue.Contains(sceneName) && teleportMap[sceneName].MapZone != GlobalEnums.MapZone.NONE)
            {
                Plugin.Logger.LogInfo($"Enqueued scene: {sceneName} : {teleportMap[sceneName].MapZone}");
                sceneQueue.Enqueue(sceneName);
            }
        }

        Plugin.Logger.LogInfo($"Total scenes to process: {sceneQueue.Count}");
        LoadNextScene();
    }

    public static void OnDumpCompleted()
    {
        _scenesSinceLastGC++;


        // GC every N scenes for both dumping options
        if (_scenesSinceLastGC >= GCEveryNScenes)
        {
            Plugin.Logger.LogInfo($"[GC] Dumped {_scenesSinceLastGC} scenes, running cleanup...");
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            _scenesSinceLastGC = 0;
        }

        // Progress logger only for full dump
        if (_isTraversing)
        {
            int totalProcessed = sceneQueue.Count == 0 ? _scenesSinceLastGC : (scenesProcessed + 1);
            Plugin.Logger.LogInfo($"Progress: {scenesProcessed + 1}/{scenesProcessed + 1 + sceneQueue.Count} scenes");
            LoadNextScene();
        }
    }

    private static bool LoadNextScene()
    {
        if (sceneQueue.Count > 0)
        {
            string nextScene = sceneQueue.Dequeue();
            scenesProcessed++;
            GameManager.instance.LoadScene(nextScene);
            return true;
        }

        // Full dump complete
        _isTraversing = false;
        Plugin.Logger.LogInfo($"Full dump complete! Processed {scenesProcessed} scenes. Running final cleanup...");
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        return false;
    }
}