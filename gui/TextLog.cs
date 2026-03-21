using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

public static class TextLog
{
    private static readonly List<TextLogEntry> TextLogEntries = new();

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

            GUILayout.BeginHorizontal();
            var color = new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.SheetName}.{entry.KeyName}:", GUIHelper.LabelStyle);

            GUILayout.FlexibleSpace();

            UnityEngine.GUI.contentColor = new Color(0.8f, 0.8f, 0.8f, opacity);
            string textPreview = entry.Text.Replace("\n", "\\n").Replace("\r", "\\r");
            if (textPreview.Length > MaxPreviewLength)
                textPreview = textPreview.Substring(0, MaxPreviewLength - 3) + "...";
            GUILayout.Label(textPreview, GUIHelper.LabelStyle);
            GUILayout.EndHorizontal();
        }

        if (TextLogEntries.Count == 0)
            GUILayout.Label("No log entries.", GUIHelper.LabelStyle);

        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    public static void LogText(string sheet, string key, string text)
    {
        var existingEntry = TextLogEntries.Find(entry => entry.SheetName == sheet && entry.KeyName == key);
        if (existingEntry != null)
            TextLogEntries.Remove(existingEntry);

        // Insert at front so newest is always on top
        TextLogEntries.Insert(0, new TextLogEntry
        {
            SheetName = sheet,
            KeyName = key,
            Text = text,
            LogTime = DateTime.Now,
            BumpedTime = null
        });
    }

    public static void ClearLog()
    {
        TextLogEntries.Clear();
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
