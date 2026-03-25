using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Patchwork.Handlers;
using Patchwork.Util;
using Patchwork.Watchers;

namespace Patchwork.Packs;

/// <summary>
/// Discovers, orders, and activates resource packs.
/// Replaces the old Plugin.PluginPackPaths HashSet with an ordered, managed list.
///
/// Two sources:
///   1. Thunderstore packs — any Patchwork/ folder in BepInEx/plugins/ (existing behaviour)
///   2. Local packs       — Patchwork/Packs/*/ (user-managed, new)
///
/// Config is persisted to Patchwork/packs.txt — one line per pack:
///   + = enabled,  - = disabled
///   format:  +|/absolute/path/to/pack
/// </summary>
public static class PackManager
{
    private static readonly List<PackInfo> _packs = new();

    private static string ConfigPath     => Path.Combine(Plugin.BasePath, "packs.txt");
    private static string LocalPacksPath => Path.Combine(Plugin.BasePath, "Packs");
    private static string StatsCachePath => Path.Combine(Plugin.BasePath, "packs-stats.txt");

    public static IReadOnlyList<PackInfo> AllPacks => _packs;

    /// <summary>Paths of enabled packs in priority order (index 0 = highest priority).
    /// Replaces Plugin.PluginPackPaths everywhere.</summary>
    public static IEnumerable<string> ActivePackPaths =>
        _packs.Where(p => p.IsEnabled).Select(p => p.Path);

    // ================================================================
    //  Initialization
    // ================================================================

    public static void Initialize()
    {
        IOUtil.EnsureDirectoryExists(LocalPacksPath);
        var discovered = DiscoverAll();
        MergeWithSavedConfig(discovered);
        LoadStatsCache();
        // Scan any packs that have no cached stats yet (first run, or newly added packs).
        ScanMissingStats();
    }

    // ================================================================
    //  Discovery
    // ================================================================

    private static List<PackInfo> DiscoverAll()
    {
        var packs = new List<PackInfo>();

        // 1. Thunderstore packs: any Patchwork/ subdirectory in BepInEx/plugins/
        foreach (var dir in Directory.GetDirectories(
            BepInEx.Paths.PluginPath, "Patchwork", SearchOption.AllDirectories))
        {
            if (string.Equals(dir, Plugin.BasePath, StringComparison.OrdinalIgnoreCase))
                continue;
            packs.Add(ReadManifest(dir, isLocal: false));
        }

        // 2. Local packs: Patchwork/Packs/*/
        if (Directory.Exists(LocalPacksPath))
        {
            foreach (var dir in Directory.GetDirectories(LocalPacksPath))
                packs.Add(ReadManifest(dir, isLocal: true));
        }

        return packs;
    }

    private static PackInfo ReadManifest(string path, bool isLocal)
    {
        string manifestPath = Path.Combine(path, "pack.json");
        if (!File.Exists(manifestPath))
            return new PackInfo(path, Path.GetFileName(path), isLocal);

        try
        {
            string json    = File.ReadAllText(manifestPath);
            string name    = ParseJsonString(json, "name");
            string author  = ParseJsonString(json, "author");
            string version = ParseJsonString(json, "version");
            string desc    = ParseJsonString(json, "description");
            return new PackInfo(path, name ?? Path.GetFileName(path), isLocal, author, version, desc);
        }
        catch
        {
            return new PackInfo(path, Path.GetFileName(path), isLocal);
        }
    }

    /// <summary>Minimal JSON string-value extractor — no external library needed.</summary>
    private static string ParseJsonString(string json, string key)
    {
        string searchKey = $"\"{key}\"";
        int ki = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
        if (ki < 0) return null;
        int colon = json.IndexOf(':', ki + searchKey.Length);
        if (colon < 0) return null;
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    // ================================================================
    //  Config persistence
    // ================================================================

    private static void MergeWithSavedConfig(List<PackInfo> discovered)
    {
        _packs.Clear();

        if (!File.Exists(ConfigPath))
        {
            _packs.AddRange(discovered);
            return;
        }

        try
        {
            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                if (line.Length < 3 || line[1] != '|') continue;

                bool   enabled = line[0] == '+';
                string path    = line.Substring(2);

                var match = discovered.FirstOrDefault(p =>
                    string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;   // pack no longer on disk

                match.IsEnabled = enabled;
                _packs.Add(match);
                discovered.Remove(match);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[PackManager] Failed to read packs.txt: {ex.Message}");
        }

        // Newly discovered packs (not in saved config) appended as enabled
        _packs.AddRange(discovered);
    }

    public static void SaveConfig()
    {
        var lines = new List<string>
        {
            "# Patchwork Pack Order — top of list = highest priority",
            "# Format: +|path (enabled)   or   -|path (disabled)"
        };
        foreach (var p in _packs)
            lines.Add($"{(p.IsEnabled ? '+' : '-')}|{p.Path}");

        File.WriteAllLines(ConfigPath, lines);
    }

    // ================================================================
    //  Mutations (called from PackManagerWindow via staged list)
    // ================================================================

    /// <summary>Commits a staged pack list, saves config, and triggers a full reload.</summary>
    public static void Apply(List<PackInfo> staged)
    {
        _packs.Clear();
        _packs.AddRange(staged);
        SaveConfig();
        TriggerFullReload();
        // Re-scan all packs (including any newly added) and persist stats.
        ScanAllStats();
        Plugin.Logger.LogInfo($"[PackManager] Applied pack order: {_packs.Count} packs, " +
            $"{_packs.Count(p => p.IsEnabled)} enabled");
    }

    /// <summary>Re-scans disk, preserving existing order and enabled state.
    /// New packs are appended as enabled; packs no longer on disk are removed.</summary>
    public static void Rescan()
    {
        var discovered = DiscoverAll();

        // Remove packs that are no longer on disk
        _packs.RemoveAll(p => !discovered.Any(d =>
            string.Equals(p.Path, d.Path, StringComparison.OrdinalIgnoreCase)));

        // Append newly found packs
        foreach (var d in discovered)
        {
            if (!_packs.Any(p => string.Equals(p.Path, d.Path, StringComparison.OrdinalIgnoreCase)))
                _packs.Add(d);
        }

        Plugin.Logger.LogInfo($"[PackManager] Rescan complete: {_packs.Count} packs found");
    }

    // ================================================================
    //  Pack stats — file-count scanning and cache persistence
    // ================================================================

    /// <summary>Reads packs-stats.txt and assigns cached stats to matching packs.</summary>
    private static void LoadStatsCache()
    {
        if (!File.Exists(StatsCachePath)) return;
        foreach (var line in File.ReadAllLines(StatsCachePath))
        {
            if (!PackStats.TryDeserialize(line, out string path, out var stats)) continue;
            var pack = _packs.Find(p =>
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            if (pack != null) pack.Stats = stats;
        }
    }

    /// <summary>Writes current stats for all known packs to packs-stats.txt.</summary>
    private static void SaveStatsCache()
    {
        var lines = new List<string>
        {
            "# Patchwork Pack Stats — auto-generated, do not edit manually"
        };
        foreach (var p in _packs)
            if (p.Stats.IsScanned)
                lines.Add(p.Stats.Serialize(p.Path));
        File.WriteAllLines(StatsCachePath, lines);
    }

    /// <summary>Scans every pack directory and refreshes counts; saves the cache.</summary>
    public static void ScanAllStats()
    {
        foreach (var p in _packs)
            p.Stats = PackStats.Scan(p.Path);
        SaveStatsCache();
        Plugin.Logger.LogInfo($"[PackManager] Pack stats refreshed for {_packs.Count} pack(s)");
    }

    /// <summary>Scans only packs that have no cached stats yet (first run / new arrivals).</summary>
    private static void ScanMissingStats()
    {
        bool any = false;
        foreach (var p in _packs)
        {
            if (p.Stats.IsScanned) continue;
            p.Stats = PackStats.Scan(p.Path);
            any = true;
        }
        if (any) SaveStatsCache();
    }

    // ================================================================
    //  Hot reload
    // ================================================================

    public static void TriggerFullReload()
    {
        SpriteFileWatcher.ReloadSprites    = true;
        SpriteFileWatcher.ReloadT2DSprites = true;
        AudioFileWatcher.ReloadAudio       = true;
        TextFileWatcher.ReloadText         = true;
    }
}
