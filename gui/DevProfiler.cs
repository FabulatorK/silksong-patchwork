using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Patchwork.Handlers;
using Patchwork.Packs;
using Patchwork.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patchwork.GUI;

public static class DevProfiler
{
    private const int FrameSampleCount = 120;
    private const float GraphHeight = 50f;
    private const float GraphBarWidth = 2f;

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

    // Per-operation timing (shared Stopwatch — main thread only)
    private static readonly Stopwatch OpTimer = new();

    // Spike log — ring buffer of operations that exceeded the spike threshold
    private const int SpikeLogCapacity = 20;
    private const float SpikeThresholdMs = 5f;   // anything ≥ 5ms gets logged
    private static readonly string[] _spikeLabels    = new string[SpikeLogCapacity];
    private static readonly float[]  _spikeDurations = new float[SpikeLogCapacity];
    private static readonly float[]  _spikeTimes     = new float[SpikeLogCapacity];
    private static int  _spikeHead;
    private static int  _spikeCount;
    private static Vector2 _spikeScroll;

    // Per-op stats (rolling max over last FrameSampleCount frames)
    private static float _maxEnforceMs;
    private static float _maxUninitMs;
    private static float _lastEnforceMs;
    private static float _lastUninitMs;

    // Graph texture (reused)
    private static Texture2D graphTex;
    private static Texture2D barTex;
    // Separate bar texture for reload-frame highlights
    private static Texture2D reloadBarTex;
    // Ring buffer: true on frames where a reload fired
    private static readonly bool[] ReloadFrames = new bool[FrameSampleCount];

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
        ReloadFrames[frameIndex] = false;   // cleared each frame; set by MarkReloadFrame()
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

    /// <summary>
    /// Starts the shared per-operation stopwatch.  Must be paired with a
    /// <see cref="StopOp"/> call.  Main thread only.
    /// </summary>
    public static void StartOp() => OpTimer.Restart();

    /// <summary>
    /// Stops the per-op timer, records a spike if the elapsed time exceeds
    /// <see cref="SpikeThresholdMs"/>, and returns the elapsed ms.
    /// </summary>
    public static float StopOp(string label)
    {
        OpTimer.Stop();
        float ms = (float)OpTimer.Elapsed.TotalMilliseconds;
        if (ms >= SpikeThresholdMs && !string.IsNullOrEmpty(label))
            RecordSpike(label, ms);
        return ms;
    }

    /// <summary>
    /// Unconditionally records a spike entry (use for reloads that are always notable).
    /// </summary>
    public static void RecordSpike(string label, float durationMs)
    {
        _spikeLabels[_spikeHead]    = label;
        _spikeDurations[_spikeHead] = durationMs;
        _spikeTimes[_spikeHead]     = Time.realtimeSinceStartup;
        _spikeHead = (_spikeHead + 1) % SpikeLogCapacity;
        if (_spikeCount < SpikeLogCapacity) _spikeCount++;
        // Mark the current frame bar orange in the frame graph
        int lastIdx = ((frameIndex - 1) % FrameSampleCount + FrameSampleCount) % FrameSampleCount;
        ReloadFrames[lastIdx] = true;
    }

    /// <summary>Records the last enforcement sweep duration for display.</summary>
    public static void RecordEnforceMs(float ms)
    {
        _lastEnforceMs = ms;
        if (ms > _maxEnforceMs) _maxEnforceMs = ms;
    }

    /// <summary>Records the last uninit-check sweep duration for display.</summary>
    public static void RecordUninitMs(float ms)
    {
        _lastUninitMs = ms;
        if (ms > _maxUninitMs) _maxUninitMs = ms;
    }

    /// <summary>
    /// Renders the profiler content without a window chrome.
    /// Called by PerformancePillar inside the Dev Hub window.
    /// The caller is responsible for wrapping this in a scroll view if desired.
    /// </summary>
    public static void DrawPillarContent()
    {
        DrawContent();
    }

    private static void DrawContent()
    {
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
            _maxEnforceMs = 0;
            _maxUninitMs  = 0;
        }

        Label($"Enforce T2D: {_lastEnforceMs:F2}ms  (max {_maxEnforceMs:F2}ms)");
        Label($"Uninit check: {_lastUninitMs:F2}ms  (max {_maxUninitMs:F2}ms)");

        GUIHelper.Space(4);
        DrawFrameGraph();

        // --- Spike Log ---
        GUIHelper.Space(8);
        SectionHeader("Spike Log");
        if (_spikeCount == 0)
        {
            Label("No spikes recorded (threshold: 5ms).");
        }
        else
        {
            if (GUILayout.Button("Clear", GUIHelper.ButtonStyle, GUIHelper.Height(20)))
            {
                _spikeCount = 0;
                _spikeHead  = 0;
            }
            float now = Time.realtimeSinceStartup;
            float logH = GUIHelper.Scaled(120f);
            _spikeScroll = GUILayout.BeginScrollView(_spikeScroll, GUIHelper.Height(logH));
            // Iterate newest-first
            for (int i = 0; i < _spikeCount; i++)
            {
                int idx = ((_spikeHead - 1 - i) % SpikeLogCapacity + SpikeLogCapacity) % SpikeLogCapacity;
                float ms  = _spikeDurations[idx];
                float age = now - _spikeTimes[idx];
                Color c   = ms >= 33.3f ? Color.red : ms >= 16.7f ? Color.yellow : new Color(1f, 0.65f, 0.2f);
                ColorLabel($"{age:F1}s ago  {_spikeLabels[idx]}  {ms:F1}ms", c);
            }
            GUILayout.EndScrollView();
        }

        // --- Memory ---
        GUIHelper.Space(8);
        SectionHeader("Memory");
        if (GcUtil.BridgeAvailable)
        {
            long monoUsed  = GcUtil.TCMonoHeapUsed;
            long monoTotal = GcUtil.TCMonoHeapTotal;
            long memUsed   = GcUtil.TCMemUsed;
            long memTotal  = GcUtil.TCMemTotal;
            double threshold = GcUtil.TCHeapThresholdMB;
            float heapFrac = monoTotal > 0 ? (float)monoUsed / monoTotal : 0f;
            Color heapColor = monoUsed / (1024.0 * 1024.0) > threshold * 0.9 ? Color.red
                            : monoUsed / (1024.0 * 1024.0) > threshold * 0.7 ? Color.yellow
                            : Color.green;
            ColorLabel($"Mono heap: {monoUsed >> 20}MB / {monoTotal >> 20}MB  (TC threshold {threshold:F0}MB)", heapColor);
            Label($"Unity memory: {memUsed >> 20}MB used / {memTotal >> 20}MB reserved");
        }
        else
        {
            long monoUsed = GC.GetTotalMemory(false);
            Label($"Mono heap: {monoUsed >> 20}MB  (TC bridge unavailable)");
        }
        Label($"GC collections: gen0={GC.CollectionCount(0)}  gen1={GC.CollectionCount(1)}  gen2={GC.CollectionCount(2)}");

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
        Label($"FileCache entries: {FileCache.Count}  (cleared on pack change)");
        if (DialogueHandler.StaleKeyCount > 0)
            ColorLabel($"  \u26A0 {DialogueHandler.StaleKeyCount} stale text key(s)", Color.yellow);
        Label($"Plugin packs: {PackManager.AllPacks.Count(p => p.IsEnabled)} active / {PackManager.AllPacks.Count} total");

        foreach (var pack in PackManager.AllPacks.Where(p => p.IsEnabled))
            Label($"  \u2022 {pack.Name}");

        // --- Utilities ---
        GUIHelper.Space(8);
        SectionHeader("Utilities");

        if (GUILayout.Button("Force GC Collect", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            GcUtil.ForceCollect();

        if (GUILayout.Button("Reload All Sprites", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            SpriteLoader.Reload();

        if (GUILayout.Button("Reload Audio", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioHandler.Reload();

        if (GUILayout.Button("Reload Text", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            DialogueHandler.Reload();

        if (GUILayout.Button("Log Scene Hierarchy", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            LogSceneHierarchy();
    }

    private static void DrawFrameGraph()
    {
        float graphH = GUIHelper.Scaled(GraphHeight);
        Rect graphRect = GUILayoutUtility.GetRect(0, float.MaxValue, graphH, graphH);

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
        if (reloadBarTex == null)
        {
            reloadBarTex = new Texture2D(1, 1);
            reloadBarTex.SetPixel(0, 0, Color.white);
            reloadBarTex.Apply();
        }

        if (sampleCount < 2) return;

        // Target 16.67ms (60fps) as the baseline; cap display at 50ms
        const float targetMs = 16.667f;
        const float maxDisplayMs = 50f;

        float barW = graphRect.width / FrameSampleCount;
        int barsToShow = Mathf.Min(sampleCount, FrameSampleCount);

        for (int i = 0; i < barsToShow; i++)
        {
            int idx = ((frameIndex - 1 - i) % FrameSampleCount + FrameSampleCount) % FrameSampleCount;
            float ms = FrameTimes[idx] * 1000f;
            float normalized = Mathf.Clamp01(ms / maxDisplayMs);
            float barH = normalized * graphRect.height;

            Color barColor;
            if (ReloadFrames[idx]) barColor = new Color(1f, 0.55f, 0f); // orange = reload
            else if (ms <= targetMs) barColor = Color.green;
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