using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Patchwork.Util;
using TeamCherry.Localization;
using UnityEngine;

namespace Patchwork.GUI;

public static class DialogueEditor
{
    private const float WindowWidth = 520f;
    private const float WindowHeight = 500f;

    private static Rect windowRect;
    private static bool initialized;
    private static Vector2 entryListScroll;
    private static Vector2 editAreaScroll;

    // All text entries seen since the editor was last cleared
    private static readonly List<DialogueEntry> Entries = new();
    private static readonly Dictionary<string, DialogueEntry> EntryLookup = new();

    // Currently selected entry for editing
    private static DialogueEntry selectedEntry;
    private static string editText = "";
    private static bool hasUnsavedChanges;

    // Search
    private static string searchText = "";

    // Status message (e.g. "Saved!", "Error: ...")
    private static string statusMessage = "";
    private static float statusTime;

    private const string SearchFieldControlName = "Patchwork.DialogueEditor.Search";

    public static void Draw()
    {
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRectFromRight(10, 10, WindowWidth, WindowHeight);
            initialized = true;
        }

        windowRect = GUILayout.Window(
            6977,
            windowRect,
            DrawWindow,
            "Patchwork Dialogue Editor",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    /// <summary>
    /// Called by DialogueHandler.GetTextPostfix to feed text entries into the editor.
    /// </summary>
    public static void TrackText(string sheet, string key, string text)
    {
        string lookupKey = $"{sheet}|{key}";
        if (EntryLookup.TryGetValue(lookupKey, out var existing))
        {
            existing.CurrentText = text;
            existing.LastSeen = DateTime.Now;
        }
        else
        {
            var entry = new DialogueEntry
            {
                Sheet = sheet,
                Key = key,
                CurrentText = text,
                LastSeen = DateTime.Now
            };
            Entries.Add(entry);
            EntryLookup[lookupKey] = entry;
        }
    }

    /// <summary>
    /// Selects an entry by sheet and key, opening it for editing.
    /// If the entry hasn't been tracked yet, it is created with the provided text.
    /// </summary>
    public static void SelectEntry(string sheet, string key, string currentText = null)
    {
        string lookupKey = $"{sheet}|{key}";
        if (!EntryLookup.TryGetValue(lookupKey, out var entry))
        {
            // Entry not tracked yet — create it so we can select it
            entry = new DialogueEntry
            {
                Sheet = sheet,
                Key = key,
                CurrentText = currentText ?? "",
                LastSeen = DateTime.Now
            };
            Entries.Add(entry);
            EntryLookup[lookupKey] = entry;
        }

        selectedEntry = entry;
        editText = entry.CurrentText;
        hasUnsavedChanges = false;
        searchText = "";
    }

    private static void DrawWindow(int windowID)
    {
        GUIHelper.Space(16);

        // Top bar: search + clear
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUIHelper.LabelStyle, GUILayout.Width(GUIHelper.Scaled(55)));
        searchText = GUIHelper.TextField(
            SearchFieldControlName,
            searchText,
            GUILayout.Width(GUIHelper.Scaled(300)),
            GUIHelper.Height(24)
        );

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
        {
            Entries.Clear();
            EntryLookup.Clear();
            selectedEntry = null;
            editText = "";
            hasUnsavedChanges = false;
        }
        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // Two-panel layout: entry list on top, edit area below
        // --- Entry list ---
        var filtered = string.IsNullOrEmpty(searchText)
            ? Entries
            : Entries.Where(e =>
                e.Key.ToLower().Contains(searchText.ToLower()) ||
                e.Sheet.ToLower().Contains(searchText.ToLower()) ||
                e.CurrentText.ToLower().Contains(searchText.ToLower())
            ).ToList();

        GUILayout.Label($"{filtered.Count} entries", GUIHelper.LabelStyle);

        entryListScroll = GUILayout.BeginScrollView(
            entryListScroll,
            GUILayout.Height(GUIHelper.Scaled(180))
        );

        foreach (var entry in filtered)
        {
            bool isSelected = entry == selectedEntry;
            string prefix = isSelected ? "\u25B6 " : "  ";

            // Check if this key has a user override
            bool hasOverride = DialogueHandler.TextCache.TryGetValue(
                Language.CurrentLanguage().ToString(), out var langCache)
                && langCache.TryGetValue(entry.Sheet, out var sheetCache)
                && sheetCache.ContainsKey(entry.Key);

            Color labelColor = hasOverride ? new Color(0.5f, 1f, 0.5f) : Color.white;
            if (isSelected) labelColor = new Color(1f, 0.9f, 0.4f);

            UnityEngine.GUI.contentColor = labelColor;

            string preview = entry.CurrentText.Replace("\n", "\\n");
            if (preview.Length > 40) preview = preview.Substring(0, 37) + "...";

            if (GUILayout.Button($"{prefix}{entry.Sheet}.{entry.Key}: {preview}", GUIHelper.LabelStyle))
            {
                if (hasUnsavedChanges && selectedEntry != null && selectedEntry != entry)
                {
                    // Warn about unsaved changes — just select anyway (simple UX)
                }
                selectedEntry = entry;
                editText = entry.CurrentText;
                hasUnsavedChanges = false;
            }
        }

        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndScrollView();

        GUIHelper.Space(6);

        // --- Edit area ---
        if (selectedEntry != null)
        {
            // Header
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
            GUILayout.Label($"Sheet: {selectedEntry.Sheet}  |  Key: {selectedEntry.Key}", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;

            GUIHelper.Space(2);

            // Editable text area
            editAreaScroll = GUILayout.BeginScrollView(
                editAreaScroll,
                GUILayout.Height(GUIHelper.Scaled(140))
            );

            string newText = GUIHelper.TextArea(
                editText,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

            if (newText != editText)
            {
                editText = newText;
                hasUnsavedChanges = true;
            }

            GUILayout.EndScrollView();

            GUIHelper.Space(4);

            // Action buttons
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(
                hasUnsavedChanges ? "Save *" : "Save",
                GUIHelper.ButtonStyle,
                GUIHelper.Height(26),
                GUILayout.Width(GUIHelper.Scaled(80))))
            {
                SaveEntry(selectedEntry, editText);
            }

            if (GUILayout.Button("Revert", GUIHelper.ButtonStyle, GUIHelper.Height(26),
                GUILayout.Width(GUIHelper.Scaled(80))))
            {
                editText = selectedEntry.CurrentText;
                hasUnsavedChanges = false;
            }

            if (GUILayout.Button("Delete Override", GUIHelper.ButtonStyle, GUIHelper.Height(26)))
            {
                DeleteOverride(selectedEntry);
            }

            GUILayout.EndHorizontal();

            // Status message
            if (!string.IsNullOrEmpty(statusMessage) && Time.realtimeSinceStartup - statusTime < 4f)
            {
                GUIHelper.Space(2);
                bool isError = statusMessage.StartsWith("Error");
                UnityEngine.GUI.contentColor = isError ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
                GUILayout.Label(statusMessage, GUIHelper.LabelStyle);
                UnityEngine.GUI.contentColor = Color.white;
            }
        }
        else
        {
            GUIHelper.Space(8);
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("Select a text entry above to edit it.\nEntries appear as the game requests dialogue text.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    private static void SaveEntry(DialogueEntry entry, string text)
    {
        try
        {
            string lang = Language.CurrentLanguage().ToString();
            string dir = Path.Combine(DialogueHandler.TextLoadPath, entry.Sheet);
            IOUtil.EnsureDirectoryExists(dir);
            string filePath = Path.Combine(dir, $"{lang}.yml");

            // Read existing file (if any) and update/add the key
            var lines = new List<string>();
            bool keyFound = false;
            string escapedValue = $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                    {
                        int splitIdx = line.IndexOf(':');
                        if (splitIdx != -1)
                        {
                            string existingKey = line.Substring(0, splitIdx).Trim();
                            if (existingKey == entry.Key)
                            {
                                lines.Add($"{entry.Key}: {escapedValue}");
                                keyFound = true;
                                continue;
                            }
                        }
                    }
                    lines.Add(line);
                }
            }

            if (!keyFound)
                lines.Add($"{entry.Key}: {escapedValue}");

            File.WriteAllLines(filePath, lines);

            // Invalidate cache and force the game to re-request all text
            DialogueHandler.InvalidateCache(entry.Sheet, lang);
            Language.SwitchLanguage(lang);

            // Update the entry's current text
            entry.CurrentText = text;
            hasUnsavedChanges = false;

            SetStatus($"Saved {entry.Sheet}.{entry.Key} — live preview applied");
            Plugin.Logger.LogInfo($"[Patchwork] Dialogue editor saved: {entry.Sheet}.{entry.Key} ({lang})");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Plugin.Logger.LogError($"[Patchwork] Dialogue editor save failed: {ex}");
        }
    }

    private static void DeleteOverride(DialogueEntry entry)
    {
        try
        {
            string lang = Language.CurrentLanguage().ToString();
            string filePath = Path.Combine(DialogueHandler.TextLoadPath, entry.Sheet, $"{lang}.yml");

            if (!File.Exists(filePath))
            {
                SetStatus("No override file exists");
                return;
            }

            var lines = File.ReadAllLines(filePath).ToList();
            bool removed = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].StartsWith("#") || string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                int splitIdx = lines[i].IndexOf(':');
                if (splitIdx == -1) continue;
                string existingKey = lines[i].Substring(0, splitIdx).Trim();
                if (existingKey == entry.Key)
                {
                    lines.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                SetStatus("Key not found in override file");
                return;
            }

            // If file would be empty (only comments/whitespace left), delete it
            bool hasContent = lines.Any(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l));
            if (hasContent)
                File.WriteAllLines(filePath, lines);
            else
                File.Delete(filePath);

            DialogueHandler.InvalidateCache(entry.Sheet, lang);
            Language.SwitchLanguage(lang);

            SetStatus($"Deleted override for {entry.Sheet}.{entry.Key} — reverted in-game");
            Plugin.Logger.LogInfo($"[Patchwork] Dialogue editor deleted override: {entry.Sheet}.{entry.Key} ({lang})");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Plugin.Logger.LogError($"[Patchwork] Dialogue editor delete failed: {ex}");
        }
    }

    private static void SetStatus(string msg)
    {
        statusMessage = msg;
        statusTime = Time.realtimeSinceStartup;
    }

    private class DialogueEntry
    {
        public string Sheet;
        public string Key;
        public string CurrentText;
        public DateTime LastSeen;
    }
}