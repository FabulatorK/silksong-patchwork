using System;
using System.Reflection;
using Patchwork.Handlers;
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
    private static MethodInfo   _piThresholdSetter;
    private static double _lastBumpMB;

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

        // Cache the private setter for HeapUsageThreshold
        _piThresholdSetter = _piThreshold?.GetSetMethod(nonPublic: true);

        // Initial calibration now that bridge is live
        BumpThresholdForPatchwork();
    }

    /// <summary>
    /// Adjusts TC's HeapUsageThreshold to account for Patchwork's own managed-heap allocations:
    /// vanilla PNG copies (_originalTextureData).
    /// TC's vanilla threshold was calibrated for the base game only — without this bump, a full
    /// skin pack can push used-heap above TC's threshold and trigger constant stutter collections.
    ///
    /// Idempotent: tracks the previous bump and applies only the delta, so calling every frame is safe.
    /// No-op when the bridge is unavailable or the change is less than 1 MB.
    /// </summary>
    public static void BumpThresholdForPatchwork()
    {
        if (_piThresholdSetter == null) return;

        long extraBytes = T2DLoader.OriginalTextureDataBytes;
        double extraMB  = extraBytes / (1024.0 * 1024.0);

        double delta = extraMB - _lastBumpMB;
        if (Math.Abs(delta) < 1.0) return; // no meaningful change

        double current  = TCHeapThresholdMB;
        double newValue = current + delta;
        _piThresholdSetter.Invoke(null, new object[] { newValue });
        Plugin.Logger.LogInfo(
            $"[GcUtil] TC heap threshold {(delta > 0 ? "raised" : "lowered")} " +
            $"{current:F0}MB → {newValue:F0}MB  (Patchwork caches: {extraMB:F0}MB)");
        _lastBumpMB = extraMB;
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
    /// Suggests a heap prewarm size in MB, derived from TC's own threshold formula.
    /// TC's threshold = 12.1% of RAM, clamped [384, 1024] MB.
    /// We target 40% of that threshold: gives Mono committed headroom for pack assets
    /// while staying well clear of the level where TC would trigger a collect.
    /// Clamped [64, 512] MB so we don't over-commit on low-RAM or waste on idle sessions.
    /// Safe to call at Awake() — does not require the GCManager bridge.
    /// </summary>
    public static int SuggestReserveMB()
    {
        int totalMb = SystemInfo.systemMemorySize;
        double tcThreshold = Math.Clamp(0.12102111566341002 * totalMb, 384.0, 1024.0);
        return (int)Math.Clamp(tcThreshold * 0.4, 64.0, 512.0);
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
