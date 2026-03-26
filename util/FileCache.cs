using System;
using System.Collections.Generic;
using System.IO;

namespace Patchwork.Util;

/// <summary>
/// Caches raw file bytes keyed by absolute path, using last-write-time as a
/// fingerprint. ReadBytes returns the in-memory copy when the file has not
/// changed since it was last read; otherwise re-reads from disk and updates
/// the entry. This eliminates redundant disk IO on every pack reload —
/// unchanged files are served from memory, only modified files hit the disk.
/// </summary>
public static class FileCache
{
    private static readonly Dictionary<string, (DateTime ModTime, byte[] Data)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns cached bytes if the file's last-write timestamp matches, otherwise
    /// reads from disk, updates the cache, and returns the new bytes.
    /// Returns null and logs a warning if the file cannot be read.
    /// </summary>
    public static byte[] ReadBytes(string path)
    {
        try
        {
            DateTime modTime = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(path, out var cached) && cached.ModTime == modTime)
                return cached.Data;

            byte[] data = File.ReadAllBytes(path);
            _cache[path] = (modTime, data);
            return data;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[FileCache] Could not read '{path}': {ex.Message}");
            return null;
        }
    }

    public static void Clear() => _cache.Clear();
    public static int Count => _cache.Count;
}
