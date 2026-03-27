using System;
using System.Collections.Generic;
using System.IO;
using Patchwork.Packs;

namespace Patchwork.Util;

/// <summary>
/// Experimental: holds pack PNG bytes in memory permanently so that
/// FileCache.ReadBytes can serve pack files without any filesystem I/O
/// (no timestamp check, no ReadAllBytes) on subsequent scene reloads.
///
/// Only covers pack paths (Plugin.PluginPackPaths), never the hot-reload
/// base folder — those files must stay timestamp-checked.
///
/// Invalidated automatically when PackManager.Apply() changes the active
/// pack set. The user must re-pin after applying.
/// </summary>
public static class PackRamCache
{
    private static readonly Dictionary<string, byte[]> _bytes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True while pack bytes are pinned in RAM.</summary>
    public static bool IsPinned { get; private set; }

    /// <summary>Total bytes held across all pinned files.</summary>
    public static long PinnedBytes { get; private set; }

    /// <summary>Number of files currently pinned.</summary>
    public static int PinnedFileCount => _bytes.Count;

    /// <summary>
    /// Scans all active pack paths for PNG files under Sprites/ and Spritesheets/,
    /// reads them into memory, and marks the cache as active.
    /// Called from the UI; runs synchronously on the main thread.
    /// </summary>
    public static void Pin(IEnumerable<string> activePackPaths)
    {
        _bytes.Clear();
        long total = 0;

        foreach (var packPath in activePackPaths)
        {
            foreach (var subdir in new[] { "Sprites", "Spritesheets" })
            {
                string dir = Path.Combine(packPath, subdir);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(file);
                        _bytes[file] = data;
                        total += data.Length;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning($"[PackRamCache] Could not read '{file}': {ex.Message}");
                    }
                }
            }
        }

        PinnedBytes = total;
        IsPinned    = true;
        Plugin.Logger.LogInfo($"[PackRamCache] Pinned {_bytes.Count} files ({total / 1024} KB)");
    }

    /// <summary>Releases all pinned bytes and deactivates the cache.</summary>
    public static void Unpin()
    {
        _bytes.Clear();
        PinnedBytes = 0;
        IsPinned    = false;
        Plugin.Logger.LogInfo("[PackRamCache] Unpinned — pack bytes released");
    }

    /// <summary>
    /// Returns pinned bytes for <paramref name="path"/> if available.
    /// Called from FileCache.ReadBytes to bypass the timestamp check.
    /// </summary>
    public static bool TryGet(string path, out byte[] bytes) =>
        _bytes.TryGetValue(path, out bytes);

    /// <summary>Human-readable size string for UI display (e.g. "24.3 MB").</summary>
    public static string PinnedSizeLabel
    {
        get
        {
            if (PinnedBytes < 1024)           return $"{PinnedBytes} B";
            if (PinnedBytes < 1024 * 1024)    return $"{PinnedBytes / 1024.0:F1} KB";
            return $"{PinnedBytes / (1024.0 * 1024):F1} MB";
        }
    }
}
