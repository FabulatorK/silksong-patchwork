using System.Collections.Generic;
using Patchwork.Util;
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


        // Collect on N-scene interval OR when heap pressure exceeds 75% of TC's threshold.
        // Pressure-based trigger handles heavy scenes that would otherwise push us over threshold
        // and invite a TC mid-gameplay collect on the following frame.
        bool heapPressure = GcUtil.HeapPressure > 0.75;
        if (_scenesSinceLastGC >= GCEveryNScenes || heapPressure)
        {
            string reason = heapPressure ? $"heap pressure {GcUtil.HeapPressure:P0}" : $"{_scenesSinceLastGC} scenes";
            Plugin.Logger.LogInfo($"[GC] Running cleanup after {reason}...");
            Resources.UnloadUnusedAssets();
            GcUtil.ForceCollect();
            _scenesSinceLastGC = 0;
        }

        // Progress logger only for full dump
        if (_isTraversing)
        {
            Plugin.Logger.LogInfo($"Progress: {scenesProcessed}/{scenesProcessed + sceneQueue.Count} scenes");
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
        GcUtil.ForceCollect();
        return false;
    }
}