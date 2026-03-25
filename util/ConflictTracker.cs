using System.Collections.Generic;
using System.Linq;

namespace Patchwork.Util;

/// <summary>
/// Tracks asset-override conflicts: cases where multiple packs supply the same asset
/// and a lower-priority one is silently ignored.
/// Cleared at the start of each Apply/Rescan cycle; populated during load.
/// </summary>
public static class ConflictTracker
{
    public readonly record struct Entry(
        string Type,       // "sprite" | "sheet" | "t2d-sprite" | "t2d-sheet"
        string Key,        // asset identifier (e.g. "sprite:Hornet/ground/Slash")
        string WinnerPack, // null = base Patchwork folder
        string LoserPack); // null = base Patchwork folder

    private static readonly List<Entry> _entries = new();

    public static IReadOnlyList<Entry> All     => _entries;
    public static int                  Total   => _entries.Count;

    public static void Clear() => _entries.Clear();

    public static void Record(string type, string key, string winnerPack, string loserPack) =>
        _entries.Add(new(type, key, winnerPack, loserPack));

    /// <summary>Number of this pack's assets that were shadowed by a higher-priority pack.</summary>
    public static int ShadowedCount(string packPath) =>
        _entries.Count(e => string.Equals(e.LoserPack, packPath, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>Number of lower-priority packs' assets that this pack overrides.</summary>
    public static int OverridingCount(string packPath) =>
        _entries.Count(e => string.Equals(e.WinnerPack, packPath, System.StringComparison.OrdinalIgnoreCase));
}
