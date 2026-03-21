using System;
using System.Collections.Generic;
using System.Diagnostics;
using Patchwork.Handlers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patchwork.GUI;

public static class DevProfiler
{
    private const int FrameSampleCount = 120;
    private const float WindowWidth = 340f;
    private const float WindowHeight = 500f;
    private const float RightMargin = 10f;
    private const float TopMargin = 420f;
    private const float GraphHeight = 50f;
    private const float GraphBarWidth = 2f;

    private static Rect windowRect;
    private static bool initialized;
    private static Vector2 scrollPosition;

    // Frame time ring buffer
    private static readonly float[] FrameTimes = new float[FrameSampleCount];
    private static int frameIndex;
    private static int sampleCount;

    // Rolling FPS stats
    private static float fps;
    private static float frameTimeMs;
    private static float minFps = float.MaxValue;
    private static float maxFps;
    private static float worstFrameTimeMs;

    // Scene load timing
    private static readonly Stopwatch SceneLoadTimer = new();
    private static string lastSceneLoadTime = "-";
    private static string lastSceneLoaded = "-";

    // Update timing (measures Plugin.Update cost)
    private static readonly Stopwatch UpdateTimer = new();
    private static float lastUpdateMs;

    // Graph texture (reused)
    private static Texture2D graphTex;
    private static Texture2D barTex;

    public static void Initialize()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloading;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneLoadTimer.Stop();
        lastSceneLoadTime = $"{SceneLoadTimer.ElapsedMilliseconds}ms";
        lastSceneLoaded = scene.name;
    }

    private static void OnSceneUnloading(Scene scene)
    {
        SceneLoadTimer.Restart();
    }

    public static void BeginUpdateTiming()
    {
        UpdateTimer.Restart();
    }

    public static void EndUpdateTiming()
    {
        UpdateTimer.Stop();
        lastUpdateMs = (float)UpdateTimer.Elapsed.TotalMilliseconds;
    }

    public static void RecordFrame()
    {
        float dt = Time.unscaledDeltaTime;
        FrameTimes[frameIndex] = dt;
        frameIndex = (frameIndex + 1) % FrameSampleCount;
        if (sampleCount < FrameSampleCount) sampleCount++;

        frameTimeMs = dt * 1000f;
        fps = dt > 0f ? 1f / dt : 0f;

        if (fps > 0.5f) // ignore garbage frames
        {
            if (fps < minFps) minFps = fps;
            if (fps > maxFps) maxFps = fps;
            if (frameTimeMs > worstFrameTimeMs) worstFrameTimeMs = frameTimeMs;
        }
    }

    public static void Draw()
    {
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRectFromRight(RightMargin, TopMargin, WindowWidth, WindowHeight);
            initialized = true;
        }

        windowRect = GUILayout.Window(
            6975,
            windowRect,
            DrawWindow,
            "Patchwork Dev Profiler",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    private static void DrawWindow(int windowID)
    {
        GUIHelper.Space(16);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        // --- FPS & Frame Time ---
        SectionHeader("Performance");

        Color fpsColor = fps >= 55 ? Color.green : fps >= 30 ? Color.yellow : Color.red;
        ColorLabel($"FPS: {fps:F0}  ({frameTimeMs:F1}ms)", fpsColor);
        Label($"Min/Max FPS: {minFps:F0} / {maxFps:F0}");
        Label($"Worst frame: {worstFrameTimeMs:F1}ms");
        Label($"Plugin.Update: {lastUpdateMs:F2}ms");

        if (GUILayout.Button("Reset Stats", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
        {
            minFps = float.MaxValue;
            maxFps = 0;
            worstFrameTimeMs = 0;
        }

        GUIHelper.Space(4);
        DrawFrameGraph();

        // --- Memory ---
        GUIHelper.Space(8);
        SectionHeader("Memory");
        long monoUsed = GC.GetTotalMemory(false);
        Label($"Mono heap: {monoUsed / (1024 * 1024)}MB");
        Label($"GC collections: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");

        // --- Scene ---
        GUIHelper.Space(8);
        SectionHeader("Scene");
        Label($"Active: {SceneManager.GetActiveScene().name}");
        Label($"Last load: {lastSceneLoaded} ({lastSceneLoadTime})");
        Label($"Loaded scenes: {SceneManager.sceneCount}");

        // --- Patchwork Caches ---
        GUIHelper.Space(8);
        SectionHeader("Patchwork Assets");
        Label($"Audio clips cached: {AudioHandler.CachedClipCount}");
        Label($"Text sheets cached: {DialogueHandler.CachedSheetCount} ({DialogueHandler.CachedKeyCount} keys)");
        if (DialogueHandler.StaleKeyCount > 0)
            ColorLabel($"  \u26A0 {DialogueHandler.StaleKeyCount} stale text key(s)", Color.yellow);
        Label($"Plugin packs: {Plugin.PluginPackPaths.Count}");

        // List pack paths
        foreach (var path in Plugin.PluginPackPaths)
        {
            var folderName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            Label($"  \u2022 {folderName}");
        }

        // --- Utilities ---
        GUIHelper.Space(8);
        SectionHeader("Utilities");

        if (GUILayout.Button("Force GC Collect", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        if (GUILayout.Button("Reload All Sprites", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            SpriteLoader.Reload();

        if (GUILayout.Button("Reload Audio", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioHandler.Reload();

        if (GUILayout.Button("Reload Text", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            DialogueHandler.Reload();

        if (GUILayout.Button("Log Scene Hierarchy", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            LogSceneHierarchy();

        GUILayout.EndScrollView();
        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    private static void DrawFrameGraph()
    {
        float graphW = GUIHelper.Scaled(WindowWidth - 20);
        float graphH = GUIHelper.Scaled(GraphHeight);
        Rect graphRect = GUILayoutUtility.GetRect(graphW, graphH);

        if (graphTex == null)
        {
            graphTex = new Texture2D(1, 1);
            graphTex.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f, 0.8f));
            graphTex.Apply();
        }
        UnityEngine.GUI.DrawTexture(graphRect, graphTex);

        if (barTex == null)
        {
            barTex = new Texture2D(1, 1);
            barTex.SetPixel(0, 0, Color.white);
            barTex.Apply();
        }

        if (sampleCount < 2) return;

        // Target 16.67ms (60fps) as the baseline; cap display at 50ms
        const float targetMs = 16.667f;
        const float maxDisplayMs = 50f;

        float barW = GUIHelper.Scaled(GraphBarWidth);
        int barsToShow = Mathf.Min(sampleCount, Mathf.FloorToInt(graphRect.width / barW));

        for (int i = 0; i < barsToShow; i++)
        {
            int idx = ((frameIndex - 1 - i) % FrameSampleCount + FrameSampleCount) % FrameSampleCount;
            float ms = FrameTimes[idx] * 1000f;
            float normalized = Mathf.Clamp01(ms / maxDisplayMs);
            float barH = normalized * graphRect.height;

            Color barColor;
            if (ms <= targetMs) barColor = Color.green;
            else if (ms <= 33.33f) barColor = Color.yellow;
            else barColor = Color.red;

            UnityEngine.GUI.color = barColor;
            Rect barRect = new Rect(
                graphRect.xMax - (i + 1) * barW,
                graphRect.yMax - barH,
                barW - 1,
                barH
            );
            UnityEngine.GUI.DrawTexture(barRect, barTex);
        }

        // Draw 60fps target line
        UnityEngine.GUI.color = new Color(1f, 1f, 1f, 0.3f);
        float lineY = graphRect.yMax - (targetMs / maxDisplayMs) * graphRect.height;
        UnityEngine.GUI.DrawTexture(new Rect(graphRect.x, lineY, graphRect.width, 1), barTex);
        UnityEngine.GUI.color = Color.white;
    }

    private static void LogSceneHierarchy()
    {
        var scene = SceneManager.GetActiveScene();
        Plugin.Logger.LogInfo($"[DevProfiler] Scene hierarchy for '{scene.name}':");
        foreach (var root in scene.GetRootGameObjects())
            LogGameObject(root, 0);
    }

    private static void LogGameObject(GameObject go, int depth)
    {
        string indent = new string(' ', depth * 2);
        int componentCount = go.GetComponents<Component>().Length;
        Plugin.Logger.LogInfo($"[DevProfiler] {indent}{go.name} ({componentCount} components, active={go.activeSelf})");
        // Only log 3 levels deep to avoid spam
        if (depth >= 3) return;
        for (int i = 0; i < go.transform.childCount; i++)
            LogGameObject(go.transform.GetChild(i).gameObject, depth + 1);
    }

    // --- Helpers ---

    private static void SectionHeader(string text)
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"--- {text} ---", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
    }

    private static void Label(string text)
    {
        GUILayout.Label(text, GUIHelper.LabelStyle);
    }

    private static void ColorLabel(string text, Color color)
    {
        UnityEngine.GUI.contentColor = color;
        GUILayout.Label(text, GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
    }
}