using System;
using UnityEngine;

namespace Patchwork.Util;

/// <summary>
/// Helpers for strategic GC management.
/// All methods are safe to call from Plugin.Awake() and scene callbacks.
/// </summary>
public static class GcUtil
{
    /// <summary>
    /// Forces the Mono heap to expand to at least <paramref name="bytes"/> before gameplay.
    /// Allocates a dummy array, keeps it alive momentarily, then releases it and runs a
    /// full blocking gen-2 collect. The heap retains the high-water mark afterwards, so
    /// subsequent allocations from custom asset packs don't trigger incremental heap growth
    /// (which causes mid-gameplay collections).
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
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Suggests a heap reserve in MB from system RAM: 25% of total, clamped to [64, 512] MB.
    /// </summary>
    public static int SuggestReserveMB()
    {
        int totalMb = SystemInfo.systemMemorySize;
        return Math.Clamp(totalMb / 4, 64, 512);
    }

    /// <summary>
    /// Triggers a gen-2 collect. Call from <c>sceneUnloaded</c> so the collection
    /// lands during the transition window rather than mid-gameplay.
    /// </summary>
    public static void CollectAtTransition()
    {
        GC.Collect(2, GCCollectionMode.Optimized);
        GC.WaitForPendingFinalizers();
    }
}
