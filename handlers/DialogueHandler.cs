using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MonoMod.Utils;
using Patchwork;
using Patchwork.Util;
using TeamCherry.Localization;

public class DialogueHandler
{
    public static string TextDumpPath { get { return Path.Combine(Plugin.BasePath, "TextDumps"); } }
    public static string TextLoadPath { get { return Path.Combine(Plugin.BasePath, "Text"); } }

    /// <summary>Current language code string (e.g. "EN"). Convenience wrapper for GUI code.</summary>
    public static string CurrentLangCode => Language.CurrentLanguage().ToString();

    public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> TextCache = [];

    // Tracks which user-override keys were actually requested by the game.
    // Key: "lang|sheet|key" — populated in GetTextPostfix when a match is found.
    private static readonly HashSet<string> RequestedOverrideKeys = new();
    // All user-override keys loaded from disk, for stale-key detection.
    private static readonly HashSet<string> AllOverrideKeys = new();
    public static int StaleKeyCount { get; private set; }
    private static bool _staleKeysReported;

    /// <summary>
    /// Fired whenever the game accesses a text key.  Subscribers receive (sheet, key, resolvedText).
    /// Replaces direct calls to TextLog and DialogueEditor — handlers must not reference GUI.
    /// </summary>
    public static event Action<string, string, string> OnTextAccessed;

    // Read-only stats for GUI
    public static int CachedSheetCount
    {
        get
        {
            int count = 0;
            foreach (var lang in TextCache.Values)
                count += lang.Count;
            return count;
        }
    }
    public static int CachedKeyCount
    {
        get
        {
            int count = 0;
            foreach (var lang in TextCache.Values)
                foreach (var sheet in lang.Values)
                    count += sheet.Count;
            return count;
        }
    }

    public static void DumpText()
    {
        string initialLang = Language.CurrentLanguage().ToString();
        foreach(var langEnum in Enum.GetValues(typeof(GlobalEnums.SupportedLanguages)))
        {
            string lang = langEnum.ToString();
            Plugin.Logger.LogInfo($"Dumping text for language code {lang}...");
            Language.SwitchLanguage(lang);
            int keys = 0;
            int sheets = 0;
            foreach(string sheet in Language.GetSheets())
            {
                IOUtil.EnsureDirectoryExists(Path.Combine(TextDumpPath, sheet));
                string filePath = Path.Combine(TextDumpPath, sheet, $"{lang}.yml");
                using StreamWriter writer = new StreamWriter(filePath);
                foreach (var key in Language.GetKeys(sheet))
                {
                    string value = Language.Get(key, sheet);
                    writer.WriteLine($"{key}: \"{value.Replace("\"", "\\\"")}\"");
                    keys++;
                }
                writer.Flush();
                writer.Close();
                sheets++;
            }
            Plugin.Logger.LogInfo($"Finished dumping text for language code {lang}. Dumped {keys} keys in {sheets} sheets.");
        }
        Language.SwitchLanguage(initialLang);
    }

    public static void InvalidateCache(string sheet, string lang)
    {
        if (TextCache.ContainsKey(lang) && TextCache[lang].ContainsKey(sheet))
        {
            Plugin.Logger.LogInfo($"Invalidating text cache for sheet: {sheet}, lang: {lang}");
            TextCache[lang].Remove(sheet);
        }
    }

    /// <summary>
    /// Searches all currently cached override entries (current language) for
    /// <paramref name="filter"/> in sheet name, key, or value.
    /// Returns an empty sequence when the filter is null/empty or the cache is empty.
    /// Skips entries already present in <paramref name="skipKeys"/> ("sheet|key" strings)
    /// so the caller can deduplicate against TextLog results.
    /// </summary>
    public static IEnumerable<(string Sheet, string Key, string Value)> SearchCache(
        string filter, HashSet<string> skipKeys = null)
    {
        if (string.IsNullOrEmpty(filter)) yield break;
        if (!TextCache.TryGetValue(CurrentLangCode, out var langCache)) yield break;
        foreach (var sheetKvp in langCache)
        {
            string sheet = sheetKvp.Key;
            bool sheetMatch = sheet.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var kvp in sheetKvp.Value)
            {
                if (skipKeys != null && skipKeys.Contains($"{sheet}|{kvp.Key}")) continue;
                if (!sheetMatch &&
                    kvp.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    kvp.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                yield return (sheet, kvp.Key, kvp.Value);
            }
        }
    }

    /// <summary>
    /// Clears caches and re-switches to the current language so the game's
    /// localization system re-requests text keys on next access. Static UI
    /// labels update immediately; active dialogue boxes update on re-trigger.
    /// </summary>
    public static void Reload()
    {
        // Clear entire cache and tracking sets so everything is re-read from disk
        TextCache.Clear();
        RequestedOverrideKeys.Clear();
        AllOverrideKeys.Clear();
        StaleKeyCount = 0;
        _staleKeysReported = false;

        // Re-switch to the current language to trigger the game's text refresh
        string currentLang = Language.CurrentLanguage().ToString();
        Language.SwitchLanguage(currentLang);
        Plugin.Logger.LogInfo("[Patchwork] Text hot-reload: cleared cache and re-switched language to force refresh.");
    }

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(Language), nameof(Language.Get), new[] { typeof(string), typeof(string) }),
            postfix: new HarmonyMethod(typeof(DialogueHandler), nameof(GetTextPostfix))
        );
    }

    private static void GetTextPostfix(string key, string sheetTitle, ref string __result)
    {
        string lang = Language.CurrentLanguage().ToString();
        if (!TextCache.ContainsKey(lang))
            TextCache[lang] = [];
        if (!TextCache[lang].ContainsKey(sheetTitle))
            TextCache[lang][sheetTitle] = LoadTextSheet(sheetTitle, lang);
        if (TextCache[lang][sheetTitle].ContainsKey(key))
        {
            __result = TextCache[lang][sheetTitle][key];
            RequestedOverrideKeys.Add($"{lang}|{sheetTitle}|{key}");
        }

        OnTextAccessed?.Invoke(sheetTitle, key, __result);
    }

    private static Dictionary<string, string> LoadTextSheet(string sheetTitle, string lang)
    {
        var sheetData = new Dictionary<string, string>();

        // Packs loaded first, in priority order.  First-wins: a higher-priority pack's
        // entry is never overwritten by a lower-priority pack.
        foreach (var packPath in Plugin.PluginPackPaths)
        {
            var packSheet = LoadTextSheet(sheetTitle, lang, Path.Combine(packPath, "Text"));
            foreach (var kvp in packSheet)
                if (!sheetData.ContainsKey(kvp.Key))
                    sheetData[kvp.Key] = kvp.Value;
        }

        // Root (Patchwork/Text/) loaded last and always wins — unconditionally
        // overwrites any pack entry for the same key.  Mirrors the sprite/audio
        // pipeline where Patchwork/Sprites/ (root) beats every pack.
        foreach (var kvp in LoadTextSheet(sheetTitle, lang, TextLoadPath))
            sheetData[kvp.Key] = kvp.Value;

        // Register all user-override keys for stale-key detection.
        foreach (var key in sheetData.Keys)
            AllOverrideKeys.Add($"{lang}|{sheetTitle}|{key}");

        return sheetData;
    }

    /// <summary>
    /// Checks for user-override keys that were loaded from disk but never requested
    /// by the game, and logs warnings. Call after the game has had a chance to request
    /// text (e.g., on scene load or after Reload).
    /// </summary>
    public static void CheckForStaleKeys()
    {
        if (_staleKeysReported) return;

        int staleCount = 0;
        foreach (var compositeKey in AllOverrideKeys)
        {
            if (RequestedOverrideKeys.Contains(compositeKey))
                continue;

            var parts = compositeKey.Split('|', 3);
            string lang = parts[0];
            string sheet = parts[1];
            string key = parts[2];

            // Only warn for the current language to avoid noise
            string currentLang = Language.CurrentLanguage().ToString();
            if (lang != currentLang)
                continue;

            staleCount++;
            Plugin.Logger.LogWarning(
                $"[Patchwork] Text override key \"{key}\" in sheet \"{sheet}\" ({lang}) " +
                $"was never requested by the game — key may have been renamed by a game update. " +
                $"Consider re-dumping text with DumpText enabled.");
        }
        StaleKeyCount = staleCount;
        _staleKeysReported = true;
        if (staleCount > 0)
            Plugin.Logger.LogWarning($"[Patchwork] {staleCount} stale text override key(s) detected. These overrides are not being applied.");
    }

    private static Dictionary<string, string> LoadTextSheet(string sheetTitle, string lang, string basePath)
    {
        string filePath = Path.Combine(basePath, sheetTitle, $"{lang}.yml");
        Dictionary<string, string> sheetData = new Dictionary<string, string>();
        if (!File.Exists(filePath))
            return sheetData;
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;
            var splitIndex = line.IndexOf(':');
            if (splitIndex == -1)
                continue;
            string key = line.Substring(0, splitIndex).Trim();
            string value = line.Substring(splitIndex + 1).Trim().Trim('"').Replace("\\\"", "\"");
            sheetData[key] = value;
        }
        return sheetData;
    }
}