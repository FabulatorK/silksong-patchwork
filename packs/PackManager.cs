using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    private static string ConfigPath      => Path.Combine(Plugin.BasePath, "packs.txt");
    private static string LocalPacksPath  => Path.Combine(Plugin.BasePath, "Packs");
    private static string StatsCachePath  => Path.Combine(Plugin.BasePath, "packs-stats.txt");
    private static string ProfilesDir     => Path.Combine(Plugin.BasePath, "Profiles");
    private static string ConditionsPath  => Path.Combine(Plugin.BasePath, "packs-conditions.txt");

    // Current scene name, updated by OnSceneLoaded; used for condition evaluation.
    private static string _currentScene = "";
    // Previous conditional-active state per pack path, for change detection.
    private static readonly Dictionary<string, bool> _prevConditionalStates = new();

    public static IReadOnlyList<PackInfo> AllPacks => _packs;

    /// <summary>Paths of enabled packs whose conditions are satisfied, in priority order.</summary>
    public static IEnumerable<string> ActivePackPaths =>
        _packs.Where(p => p.IsEnabled &&
                          (!p.HasConditions || p.EvaluateConditions(_currentScene, _packs)))
              .Select(p => p.Path);

    // ================================================================
    //  Initialization
    // ================================================================

    public static void Initialize()
    {
        IOUtil.EnsureDirectoryExists(LocalPacksPath);
        var discovered = DiscoverAll();
        MergeWithSavedConfig(discovered);
        LoadStatsCache();
        ScanMissingStats();
        LoadConditions();
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
        {
            string folderName = Path.GetFileName(path);
            string fallback;
            // Thunderstore packs always land in a fixed "Patchwork" subfolder, so every pack
            // would show the same name. Use the unique parent directory name instead.
            // Local packs (Patchwork/Packs/MyCoolPack/) already have a unique folder name.
            if (string.Equals(folderName, "Patchwork", StringComparison.OrdinalIgnoreCase))
            {
                string parentName = Path.GetFileName(Path.GetDirectoryName(path));
                fallback = string.IsNullOrEmpty(parentName) ? folderName : parentName;
            }
            else
            {
                fallback = folderName;
            }
            return new PackInfo(path, fallback, isLocal);
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            string name    = ParseJsonString(json, "name");
            string author  = ParseJsonString(json, "author");
            string version = ParseJsonString(json, "version");
            string desc    = ParseJsonString(json, "description");
            if (name == null)
                Plugin.Logger.LogWarning($"[PackManager] pack.json at '{manifestPath}' has no 'name' field — using folder name");
            return new PackInfo(path, name ?? Path.GetFileName(path), isLocal, author, version, desc);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[PackManager] Failed to read '{manifestPath}': {ex.Message} — using folder name");
            return new PackInfo(path, Path.GetFileName(path), isLocal);
        }
    }

    /// <summary>
    /// Extracts a string value from flat JSON by key. Handles whitespace and
    /// backslash escape sequences (including escaped quotes).
    /// Returns null if the key is absent or the value is not a string.
    /// </summary>
    private static string ParseJsonString(string json, string key)
    {
        string searchKey = $"\"{key}\"";
        int ki = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
        if (ki < 0) return null;

        int colon = json.IndexOf(':', ki + searchKey.Length);
        if (colon < 0) return null;

        // Skip whitespace to find the opening quote
        int q1 = -1;
        for (int i = colon + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"') { q1 = i; break; }
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return null; // not a string value
        }
        if (q1 < 0) return null;

        // Read until unescaped closing quote, processing backslash escapes
        var sb = new StringBuilder();
        for (int i = q1 + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                char next = json[++i];
                switch (next)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    default:   sb.Append('\\'); sb.Append(next); break;
                }
                continue;
            }
            if (c == '"') return sb.ToString();
            sb.Append(c);
        }
        return null; // unterminated string
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
        SaveConditions();
        Util.ConflictTracker.Clear();
        Util.PackRamCache.Unpin(); // pack set changed — pinned bytes are stale
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
        Util.ConflictTracker.Clear();
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
    //  Lookup helpers
    // ================================================================

    /// <summary>Returns the display name of the pack at <paramref name="path"/>,
    /// falling back to the directory name if the pack is not in the current list.</summary>
    public static string GetPackName(string path)
    {
        if (path == null) return "(base)";
        var pack = _packs.Find(p =>
            string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        return pack?.Name ?? Path.GetFileName(path);
    }

    // ================================================================
    //  Profiles — save / load named pack configurations
    // ================================================================

    public static string[] GetProfileNames()
    {
        IOUtil.EnsureDirectoryExists(ProfilesDir);
        return Directory.GetFiles(ProfilesDir, "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n)
            .ToArray();
    }

    /// <summary>Saves the current pack order and enabled state as a named profile.</summary>
    public static void SaveProfile(string name)
    {
        IOUtil.EnsureDirectoryExists(ProfilesDir);
        var lines = new List<string>
        {
            $"# Patchwork Profile: {name}",
            "# Format: +|path (enabled)   or   -|path (disabled)"
        };
        foreach (var p in _packs)
            lines.Add($"{(p.IsEnabled ? '+' : '-')}|{p.Path}");
        File.WriteAllLines(Path.Combine(ProfilesDir, $"{name}.txt"), lines);
        Plugin.Logger.LogInfo($"[PackManager] Saved profile '{name}'");
    }

    /// <summary>Builds a staged pack list from a saved profile, ready for user review before Apply.
    /// Returns null if the profile file does not exist.</summary>
    public static List<PackInfo> StageProfile(string name)
    {
        string filePath = Path.Combine(ProfilesDir, $"{name}.txt");
        if (!File.Exists(filePath)) return null;

        var entries = new List<(string Path, bool Enabled)>();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.Length < 3) continue;
            entries.Add((line.Substring(2), line[0] == '+'));
        }

        var result = new List<PackInfo>();
        foreach (var (entryPath, enabled) in entries)
        {
            var pack = _packs.Find(p =>
                string.Equals(p.Path, entryPath, StringComparison.OrdinalIgnoreCase));
            if (pack == null) continue;
            var clone = pack.Clone();
            clone.IsEnabled = enabled;
            result.Add(clone);
        }
        // Append any currently-known packs absent from the profile (newly added since save).
        foreach (var p in _packs)
            if (!result.Any(r => string.Equals(r.Path, p.Path, StringComparison.OrdinalIgnoreCase)))
                result.Add(p.Clone());

        Plugin.Logger.LogInfo($"[PackManager] Staged profile '{name}'");
        return result;
    }

    public static void DeleteProfile(string name)
    {
        string filePath = Path.Combine(ProfilesDir, $"{name}.txt");
        if (File.Exists(filePath)) File.Delete(filePath);
        Plugin.Logger.LogInfo($"[PackManager] Deleted profile '{name}'");
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
    //  Conditional pack evaluation
    // ================================================================

    /// <summary>
    /// Called by Plugin on <c>SceneManager.sceneLoaded</c>.
    /// Updates the current scene name and evaluates all conditional packs.
    /// </summary>
    public static void OnSceneLoaded(string sceneName)
    {
        _currentScene = sceneName;
        EvaluateConditionalPacks(onlyHotReload: false);
    }

    /// <summary>
    /// Called periodically from Plugin.Update for packs with <see cref="ReloadTrigger.HotReload"/>
    /// so that non-scene conditions (future condition types) are re-checked mid-scene.
    /// </summary>
    public static void PollHotReloadConditions()
    {
        EvaluateConditionalPacks(onlyHotReload: true);
    }

    private static void EvaluateConditionalPacks(bool onlyHotReload)
    {
        var conditional = _packs.Where(p =>
            p.HasConditions &&
            (!onlyHotReload || p.ReloadTrigger == ReloadTrigger.HotReload)).ToList();

        if (conditional.Count == 0) return;

        bool anyChanged = false;
        foreach (var pack in conditional)
        {
            bool newActive = pack.IsEnabled && pack.EvaluateConditions(_currentScene, _packs);
            _prevConditionalStates.TryGetValue(pack.Path, out bool prevActive);
            if (newActive == prevActive) continue;

            _prevConditionalStates[pack.Path] = newActive;
            anyChanged = true;
            Plugin.Logger.LogInfo(
                $"[PackManager] '{pack.Name}' condition → {(newActive ? "active" : "inactive")} " +
                $"(scene='{_currentScene}')");
        }

        if (anyChanged)
        {
            Util.ConflictTracker.Clear();
            TriggerFullReload();
        }
    }

    // ================================================================
    //  Condition persistence
    // ================================================================

    /// <summary>
    /// Reads packs-conditions.txt and restores conditions onto matching packs.
    /// Format per line: packPath \t trigger \t cond1_serialized [\t cond2 ...]
    /// (Old format had a logic field at column 2; detected and skipped for backward compat.)
    /// </summary>
    public static void LoadConditions()
    {
        if (!File.Exists(ConditionsPath)) return;
        foreach (var line in File.ReadAllLines(ConditionsPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;

            var pack = _packs.Find(p =>
                string.Equals(p.Path, parts[0], StringComparison.OrdinalIgnoreCase));
            if (pack == null) continue;

            pack.ReloadTrigger = parts[1] == "hot" ? ReloadTrigger.HotReload : ReloadTrigger.OnSceneTransition;

            // Backward compat: old format had a global logic field ("and"/"or") at parts[2]
            int condStart = (parts[2] == "and" || parts[2] == "or") ? 3 : 2;

            pack.Conditions.Clear();
            for (int i = condStart; i < parts.Length; i++)
            {
                var cond = PackCondition.TryDeserialize(parts[i]);
                if (cond != null) pack.Conditions.Add(cond);
            }
        }
        Plugin.Logger.LogInfo($"[PackManager] Loaded conditions for " +
            $"{_packs.Count(p => p.HasConditions)} pack(s)");
    }

    /// <summary>Writes current conditions for all packs that have any to packs-conditions.txt.</summary>
    public static void SaveConditions()
    {
        var lines = new List<string>
        {
            "# Patchwork Pack Conditions — auto-generated, do not edit manually",
            "# Format: packPath \\t trigger \\t cond1 [\\t cond2 ...]"
        };
        foreach (var p in _packs.Where(q => q.HasConditions))
        {
            string trigger = p.ReloadTrigger == ReloadTrigger.HotReload ? "hot" : "scene";
            string condStr = string.Join("\t", p.Conditions.Select(c => c.Serialize()));
            lines.Add($"{p.Path}\t{trigger}\t{condStr}");
        }
        File.WriteAllLines(ConditionsPath, lines);
    }

    // ================================================================
    //  Hot reload
    // ================================================================

    public static void TriggerFullReload()
    {
        // Rebuild text pack watchers so newly-enabled packs have their Text/ dirs watched.
        Plugin.TextFileWatcher?.RebuildPackWatchers();

        // The pack set has changed — every cached PNG byte[] from the old active packs is
        // now stale. Clearing here is safe: FileCache uses timestamp-based fingerprinting,
        // so files will be re-read from disk correctly on the next Reload() sweep.
        // This is the primary defence against managed-heap bloat: without this clear,
        // byte[] arrays for every PNG in every pack that was ever loaded accumulate
        // permanently and are never GC-eligible.
        Util.FileCache.Clear();

        SpriteFileWatcher.ReloadSprites    = true;
        SpriteFileWatcher.ReloadT2DSprites = true;
        AudioFileWatcher.ReloadAudio       = true;
        TextFileWatcher.ReloadText         = true;
        VideoHandler.ReloadVideos          = true;
    }
}
