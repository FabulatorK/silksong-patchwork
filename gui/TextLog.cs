using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

public static class TextLog
{
    private static readonly List<TextLogEntry> TextLogEntries = new();

    private static readonly int MaxPreviewLength = 50;

    private static Rect windowRect = new Rect(Screen.width - 710, 10, 700, 400);
    private static Vector2 scrollPosition = Vector2.zero;

    public static void DrawTextLog()
    {
        windowRect = GUILayout.Window(6971, windowRect, TextLogWindow, "Patchwork Text Log");
    }

    private static void TextLogWindow(int windowID)
    {
        TextLogEntries.RemoveAll(entry => entry.IsExpired());

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.BeginVertical();
        foreach (var entry in TextLogEntries)
        {
            GUILayout.BeginHorizontal();
            var opacity = entry.GetOpacity();
            var color = new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.skin.label.fontSize = 14;
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.SheetName}.{entry.KeyName}:");

            GUILayout.FlexibleSpace();

            UnityEngine.GUI.contentColor = new Color(0.8f, 0.8f, 0.8f, opacity);
            string textPreview = entry.Text.Replace("\n", "\\n").Replace("\r", "\\r");
            if (textPreview.Length > MaxPreviewLength)
                textPreview = textPreview.Substring(0, MaxPreviewLength - 3) + "...";
            GUILayout.Label(textPreview);
            GUILayout.EndHorizontal();
        }

        if (TextLogEntries.Count == 0)
            GUILayout.Label("No log entries.");

        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        UnityEngine.GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    public static void LogText(string sheet, string key, string text)
    {
        var existingEntry = TextLogEntries.Find(entry => entry.SheetName == sheet && entry.KeyName == key);
        if (existingEntry != null)
            TextLogEntries.Remove(existingEntry);

        TextLogEntries.Add(new TextLogEntry
        {
            SheetName = sheet,
            KeyName = key,
            Text = text,
            LogTime = DateTime.Now
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

        public float GetOpacity()
        {
            var elapsed = (DateTime.Now - LogTime).TotalSeconds;
            if (elapsed >= Plugin.Config.TextLogDuration)
                return 0.2f; // Minimum opacity
            float opacityRange = 1.0f - 0.2f;
            return 1.0f - (float)(elapsed / Plugin.Config.TextLogDuration) * opacityRange;
        }

        public bool IsExpired()
        {
            var elapsed = (DateTime.Now - LogTime).TotalSeconds;
            return elapsed >= Plugin.Config.TextLogDuration;
        }
    }
}