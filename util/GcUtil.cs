using System;
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
