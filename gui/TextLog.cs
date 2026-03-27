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

    private static Rect windowRect;

    private static bool initialized = false;

    private static Vector2 scrollPosition = Vector2.zero;


    public static void DrawTextLog()
    {
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRect(10, 10, 700, 400);
            initialized = true;
        }
        GUIHelper.ApplyScaledSkin();
        windowRect = GUILayout.Window(6971, windowRect, TextLogWindow, "Patchwork Text Log", GUIHelper.WindowStyle, GUIHelper.WindowLayout(700, 400));
    }

    private static void TextLogWindow(int windowID)
    {
        GUIHelper.Space(16);

        int maxVisible = Mathf.Clamp(Plugin.Config.TextLogMaxVisible, 5, 50);
        double fadeDuration = Plugin.Config.TextLogDuration;

        // Remove fully faded overflow entries
        for (int i = TextLogEntries.Count - 1; i >= maxVisible; i--)
        {
            if (TextLogEntries[i].IsFadedOut(fadeDuration))
                TextLogEntries.RemoveAt(i);
        }

        // Mark overflow entries that just got bumped
        for (int i = maxVisible; i < TextLogEntries.Count; i++)
        {
            if (TextLogEntries[i].BumpedTime == null)
                TextLogEntries[i].BumpedTime = DateTime.Now;
        }

        // Clear bumped time for entries that scrolled back into visible range
        for (int i = 0; i < Math.Min(maxVisible, TextLogEntries.Count); i++)
        {
            TextLogEntries[i].BumpedTime = null;
        }

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.BeginVertical();
        for (int i = 0; i < TextLogEntries.Count; i++)
        {
            var entry = TextLogEntries[i];
            bool isVisible = i < maxVisible;
            float opacity = isVisible ? 1.0f : entry.GetFadeOpacity(fadeDuration);

            var color = new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;

            string textPreview = entry.Text.Replace("\n", "\\n").Replace("\r", "\\r");
            if (textPreview.Length > MaxPreviewLength)
                textPreview = textPreview.Substring(0, MaxPreviewLength - 3) + "...";

            string label = $"{entry.SheetName}.{entry.KeyName}: {textPreview}";

            // Clickable entry — opens in Dialogue Editor
            if (GUILayout.Button(label, GUIHelper.LabelStyle))
            {
                DialogueEditor.SelectEntry(entry.SheetName, entry.KeyName, entry.Text);
                Plugin.ShowDialogueEditor = true;
            }
        }

        if (TextLogEntries.Count == 0)
            GUILayout.Label("No log entries.", GUIHelper.LabelStyle);

        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    /// <summary>Total number of tracked text entries.</summary>
    public static int EntryCount => TextLogEntries.Count;

    /// <summary>
    /// Renders log entries into the current GUILayout context (no window chrome).
    /// Calls <paramref name="onEntryClick"/> when an entry is clicked.
    /// Called by TextPillar inside the Dev Hub window.
    /// </summary>
    public static void DrawEntries(int maxVisible, double fadeDuration, Action<string, string, string> onEntryClick)
    {
        // Remove fully faded overflow entries
        for (int i = TextLogEntries.Count - 1; i >= maxVisible; i--)
        {
            if (TextLogEntries[i].IsFadedOut(fadeDuration))
                TextLogEntries.RemoveAt(i);
        }

        // Mark overflow entries that just got bumped
        for (int i = maxVisible; i < TextLogEntries.Count; i++)
        {
            if (TextLogEntries[i].BumpedTime == null)
                TextLogEntries[i].BumpedTime = DateTime.Now;
        }

        // Clear bumped time for entries that scrolled back into visible range
        for (int i = 0; i < Math.Min(maxVisible, TextLogEntries.Count); i++)
            TextLogEntries[i].BumpedTime = null;

        GUILayout.BeginVertical();
        for (int i = 0; i < TextLogEntries.Count; i++)
        {
            var entry    = TextLogEntries[i];
            bool visible = i < maxVisible;
            float opacity = visible ? 1.0f : entry.GetFadeOpacity(fadeDuration);

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