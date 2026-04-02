using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Patchwork.GUI;

public static class TextLog
{
    private static readonly List<TextLogEntry> TextLogEntries = new();
    private static readonly Dictionary<string, TextLogEntry> TextLogLookup = new();

    private static readonly int MaxPreviewLength = 50;

    /// <summary>Total number of tracked text entries.</summary>
    public static int EntryCount => TextLogEntries.Count;

    /// <summary>
    /// Renders log entries into the current GUILayout context (no window chrome).
    /// Calls <paramref name="onEntryClick"/> when an entry is clicked.
    /// Called by TextPillar inside the Dev Hub window.
    /// </summary>
    public static void DrawEntries(int maxVisible, double fadeDuration, Action<string, string, string> onEntryClick)
    {
        UpdateEntryLifecycle(maxVisible, fadeDuration);

        GUILayout.BeginVertical();
        for (int i = 0; i < TextLogEntries.Count; i++)
        {
            var entry = TextLogEntries[i];
            float opacity = i < maxVisible ? 1f : entry.GetFadeOpacity(fadeDuration);
            UnityEngine.GUI.contentColor = new Color(1f, 1f, 1f, opacity);

            string preview = entry.Text.Replace("\n", "\\n").Replace("\r", "\\r");
            if (preview.Length > MaxPreviewLength)
                preview = preview.Substring(0, MaxPreviewLength - 3) + "...";

            if (GUILayout.Button($"{entry.SheetName}.{entry.KeyName}: {preview}", GUIHelper.LabelStyle))
                onEntryClick?.Invoke(entry.SheetName, entry.KeyName, entry.Text);
        }

        if (TextLogEntries.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No entries yet.", GUIHelper.LabelStyle);
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    /// <summary>
    /// Renders all entries matching <paramref name="filter"/> (sheet, key, or text).
    /// No fade/bump — intended for the search pane where order is alphabetical not temporal.
    /// Pass null or empty to show all entries.
    /// If <paramref name="out_seen"/> is provided, every rendered entry's composite key
    /// ("sheet|key") is added to it — useful for deduplication against a second source.
    /// If <paramref name="suppressEmpty"/> is true the "No matches" / "No entries yet"
    /// label is omitted, letting the caller render a combined empty state.
    /// </summary>
    public static void DrawFilteredEntries(string filter, Action<string, string, string> onEntryClick,
        HashSet<string> out_seen = null, bool suppressEmpty = false)
    {
        bool hasFilter = !string.IsNullOrEmpty(filter);
        GUILayout.BeginVertical();
        bool any = false;
        foreach (var entry in TextLogEntries)
        {
            if (hasFilter &&
                entry.SheetName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.KeyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string preview = entry.Text.Replace("\n", "\\n").Replace("\r", "\\r");
            if (preview.Length > MaxPreviewLength)
                preview = preview.Substring(0, MaxPreviewLength - 3) + "...";

            if (GUILayout.Button($"{entry.SheetName}.{entry.KeyName}: {preview}", GUIHelper.LabelStyle))
                onEntryClick?.Invoke(entry.SheetName, entry.KeyName, entry.Text);
            out_seen?.Add($"{entry.SheetName}|{entry.KeyName}");
            any = true;
        }
        if (!any && !suppressEmpty)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(hasFilter ? "No matches." : "No entries yet.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        GUILayout.EndVertical();
    }

    private static void UpdateEntryLifecycle(int maxVisible, double fadeDuration)
    {
        for (int i = TextLogEntries.Count - 1; i >= maxVisible; i--)
            if (TextLogEntries[i].IsFadedOut(fadeDuration))
                TextLogEntries.RemoveAt(i);
        for (int i = maxVisible; i < TextLogEntries.Count; i++)
            if (TextLogEntries[i].BumpedTime == null)
                TextLogEntries[i].BumpedTime = DateTime.Now;
        for (int i = 0; i < Math.Min(maxVisible, TextLogEntries.Count); i++)
            TextLogEntries[i].BumpedTime = null;
    }

    public static void LogText(string sheet, string key, string text)
    {
        string lookupKey = $"{sheet}|{key}";
        if (TextLogLookup.TryGetValue(lookupKey, out var existingEntry))
        {
            // Move existing entry to front — update in place, no new allocation
            TextLogEntries.Remove(existingEntry);
            existingEntry.Text = text;
            existingEntry.LogTime = DateTime.Now;
            existingEntry.BumpedTime = null;
            TextLogEntries.Insert(0, existingEntry);
        }
        else
        {
            var entry = new TextLogEntry
            {
                SheetName = sheet,
                KeyName = key,
                Text = text,
                LogTime = DateTime.Now,
                BumpedTime = null
            };
            TextLogEntries.Insert(0, entry);
            TextLogLookup[lookupKey] = entry;
        }
    }

    public static void ClearLog()
    {
        TextLogEntries.Clear();
        TextLogLookup.Clear();
    }

    internal class TextLogEntry
    {
        public string SheetName;
        public string KeyName;
        public string Text;
        public DateTime LogTime;
        public DateTime? BumpedTime;

        /// <summary>
        /// Opacity for entries that have been bumped out of the visible slots.
        /// Fades from 1.0 to 0.0 over fadeDuration seconds since BumpedTime.
        /// </summary>
        public float GetFadeOpacity(double fadeDuration)
        {
            if (BumpedTime == null) return 1.0f;
            var elapsed = (DateTime.Now - BumpedTime.Value).TotalSeconds;
            if (elapsed >= fadeDuration) return 0.0f;
            return 1.0f - (float)(elapsed / fadeDuration);
        }

        public bool IsFadedOut(double fadeDuration)
        {
            if (BumpedTime == null) return false;
            return (DateTime.Now - BumpedTime.Value).TotalSeconds >= fadeDuration;
        }
    }
}