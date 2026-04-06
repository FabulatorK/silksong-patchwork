using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

namespace Patchwork.Util;

/// <summary>
/// Helpers for strategic GC management aware of TC's GCManager.
/// TC disables the runtime GC (GarbageCollector.GCMode = Disabled) at scene load and manages
/// collections manually via a heap-threshold loop. Any bare GC.Collect() call issued while
/// GCMode is Disabled is silently ignored — callers must do the enable/restore dance themselves.
/// </summary>
public static class GcUtil
{
    // ================================================================
    //  TC GCManager bridge (reflection — graceful fallback if absent)
    // ================================================================

    private static bool   _bridgeInit;
    private static MethodInfo _miMonoUsed;
    private static MethodInfo _miMonoTotal;
    private static MethodInfo _miMemUsed;
    private static MethodInfo _miMemTotal;
    private static PropertyInfo _piThreshold;

    /// <summary>Fired whenever TC's GCManager detects a stutter and bumps the heap threshold.</summary>
    public static event Action OnTCGCStutter;

    /// <summary>
    /// Call once after the game scene has loaded (GCManager is created via RuntimeInitializeOnLoadMethod).
    /// Caches reflection handles and hooks the private OnGCStutter event.
    /// </summary>
    public static void InitBridge()
    {
        if (_bridgeInit) return;
        _bridgeInit = true;

        var t = Type.GetType("GCManager, Assembly-CSharp");
        if (t == null) return;

        const BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
        _miMonoUsed  = t.GetMethod("GetMonoHeapUsage",  pub);
        _miMonoTotal = t.GetMethod("GetMonoHeapTotal",  pub);
        _miMemUsed   = t.GetMethod("GetMemoryUsage",    pub);
        _miMemTotal  = t.GetMethod("GetMemoryTotal",    pub);
        _piThreshold = t.GetProperty("HeapUsageThreshold", pub);

        // Wire TC's private OnGCStutter → our public event
        var fi = t.GetField("OnGCStutter", BindingFlags.NonPublic | BindingFlags.Static);
        if (fi != null)
        {
            Action handler = () => OnTCGCStutter?.Invoke();
            var existing = (Action)fi.GetValue(null);
            fi.SetValue(null, Delegate.Combine(existing, handler));
        }
    }

    private static long InvokeLong(MethodInfo mi) =>
        mi != null ? (long)mi.Invoke(null, null) : -1L;

    /// <summary>Mono heap used (bytes). Falls back to GC.GetTotalMemory if bridge unavailable.</summary>
    public static long TCMonoHeapUsed  => _miMonoUsed  != null ? InvokeLong(_miMonoUsed)  : GC.GetTotalMemory(false);
    /// <summary>Mono heap total reserved (bytes).</summary>
    public static long TCMonoHeapTotal => InvokeLong(_miMonoTotal);
    /// <summary>Total Unity memory in use (bytes). -1 if bridge unavailable.</summary>
    public static long TCMemUsed       => InvokeLong(_miMemUsed);
    /// <summary>Total Unity memory reserved (bytes). -1 if bridge unavailable.</summary>
    public static long TCMemTotal      => InvokeLong(_miMemTotal);
    /// <summary>TC's current heap-usage threshold in MB. 0 if bridge unavailable.</summary>
    public static double TCHeapThresholdMB =>
        _piThreshold != null ? (double)_piThreshold.GetValue(null) : 0.0;
    /// <summary>True if the TC GCManager bridge is active.</summary>
    public static bool BridgeAvailable => _miMonoUsed != null;

    /// <summary>
    /// Heap pressure as a fraction of TC's threshold (0–1+).
    /// 1.0 means heap used == TC's threshold; above 1.0 means TC would already have collected.
    /// Returns 0 when bridge is unavailable.
    /// </summary>
    public static double HeapPressure
    {
        get
        {
            double threshold = TCHeapThresholdMB;
            if (threshold <= 0.0) return 0.0;
            return (TCMonoHeapUsed / (1024.0 * 1024.0)) / threshold;
        }
    }

    // ================================================================
    //  Heap management
    // ================================================================

    /// <summary>
    /// Forces the Mono heap to expand to at least <paramref name="bytes"/> before gameplay.
    /// Safe to call from Plugin.Awake() — TC's GCManager is created via
    /// RuntimeInitializeOnLoadMethod(AfterSceneLoad) so GCMode is still Enabled at that point.
    /// </summary>
    public static void PrewarmHeap(long bytes)
    {
        if (bytes <= 0) return;
        // int[] cap is ~2 GB on 64-bit; 512 MB chunks to stay safely inside that.
        const int chunkSize = 512 * 1024 * 1024;
        long remaining = bytes;
        while (remaining > 0)
        {
            int alloc = (int)Math.Min(remaining, chunkSize);
            var dummy = new byte[alloc];
            GC.KeepAlive(dummy);
            dummy = null;
            remaining -= alloc;
        }
        ForceCollect();
    }

    /// <summary>
    /// Suggests a heap reserve in MB from system RAM.
    /// TC uses ~12.1%, clamped [384, 1024] MB. We use a more conservative 15%, clamped [64, 512] MB
    /// to leave headroom for pack assets without fighting TC's threshold.
    /// </summary>
    public static int SuggestReserveMB()
    {
        int totalMb = SystemInfo.systemMemorySize;
        return Math.Clamp((int)(totalMb * 0.15), 64, 512);
    }

    /// <summary>
    /// Triggers a full blocking collect during scene transitions.
    /// Mirrors TC's GCManager.ForceCollect: temporarily re-enables the runtime GC so the
    /// collect is not silently dropped, then restores the previous mode.
    /// </summary>
    public static void CollectAtTransition() => ForceCollect();

    /// <summary>
    /// Temporarily re-enables the Mono runtime GC, runs a full gen-max blocking collect,
    /// then restores the previous GCMode. Safe to call regardless of current GCMode state.
    /// Mirrors TC's GCManager.ForceCollect pattern exactly.
    /// </summary>
    public static void ForceCollect()
    {
        var prev = GarbageCollector.GCMode;
        GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GarbageCollector.GCMode = prev;
    }
}
