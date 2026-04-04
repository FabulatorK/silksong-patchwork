using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Live log of T2D sprite and texture setter triggers.
/// One entry per atlas (cleanTexName) — shows the most recently triggered sprite name.
/// Deduplicating at atlas level prevents animated backgrounds (e.g. Black_Thread_BG
/// with 30 frames) from flooding the log with per-frame entries.
/// Populated via T2DLoader.OnT2DTrigger, which fires only when IsT2DLogActive is true.
/// Displayed by GraphicsPillar in the T2D Log sub-tab.
/// </summary>
public static class T2DLog
{
    private static readonly List<T2DLogEntry>               _entries = new();
    private static readonly Dictionary<string, T2DLogEntry> _lookup  =
        new(StringComparer.OrdinalIgnoreCase);

    public static int EntryCount => _entries.Count;

    /// <summary>
    /// Renders log entries into the current GUILayout context (no window chrome).
    /// Clickable entries invoke <paramref name="onEntryClick"/> with (cleanTexName, spriteName).
    /// </summary>
    public static void DrawEntries(int maxVisible, double fadeDuration,
        Action<string, string> onEntryClick)
    {
        UpdateLifecycle(maxVisible, fadeDuration);

        GUILayout.BeginVertical();
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            float opacity = i < maxVisible ? 1f : entry.GetFadeOpacity(fadeDuration);
            UnityEngine.GUI.contentColor = new Color(1f, 1f, 1f, opacity);

            string label = string.IsNullOrEmpty(entry.LastSpriteName)
                ? entry.CleanTexName
                : $"{entry.CleanTexName} / {entry.LastSpriteName}";

            if (GUILayout.Button(label, GUIHelper.LabelStyle))
                onEntryClick?.Invoke(entry.CleanTexName, entry.LastSpriteName);
        }

        if (_entries.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No T2D textures triggered yet.", GUIHelper.LabelStyle);
        }

        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    // Minimum seconds between re-bumping an atlas entry to the top of the log.
    // Sprite name updates (e.g. animation cycling) within this window are applied
    // silently without re-ordering the list.
    private const double ReBumpCooldownSeconds = 2.0;

    public static void LogTrigger(string cleanTexName, string spriteName)
    {
        if (string.IsNullOrEmpty(cleanTexName)) return;

        // Key is atlas only — all sprites from the same atlas share one log entry.
        if (_lookup.TryGetValue(cleanTexName, out var existing))
        {
            // Always update the displayed sprite name so the entry stays current.
            if (!string.IsNullOrEmpty(spriteName))
                existing.LastSpriteName = spriteName;

            // Only re-order to the top after the cooldown has elapsed.
            if ((DateTime.Now - existing.LogTime).TotalSeconds < ReBumpCooldownSeconds)
                return;

            _entries.Remove(existing);
            existing.LogTime    = DateTime.Now;
            existing.BumpedTime = null;
            _entries.Insert(0, existing);
        }
        else
        {
            var entry = new T2DLogEntry
            {
                CleanTexName    = cleanTexName,
                LastSpriteName  = spriteName ?? "",
                LogTime         = DateTime.Now,
                BumpedTime      = null
            };
            _entries.Insert(0, entry);
            _lookup[cleanTexName] = entry;
        }
    }

    public static void ClearLog()
    {
        _entries.Clear();
        _lookup.Clear();
    }

    private static void UpdateLifecycle(int maxVisible, double fadeDuration)
    {
        for (int i = _entries.Count - 1; i >= maxVisible; i--)
            if (_entries[i].IsFadedOut(fadeDuration))
                _entries.RemoveAt(i);
        for (int i = maxVisible; i < _entries.Count; i++)
            if (_entries[i].BumpedTime == null)
                _entries[i].BumpedTime = DateTime.Now;
        for (int i = 0; i < Math.Min(maxVisible, _entries.Count); i++)
            _entries[i].BumpedTime = null;
    }

    internal class T2DLogEntry
    {
        public string    CleanTexName;
        public string    LastSpriteName;  // most recently triggered sprite on this atlas
        public DateTime  LogTime;
        public DateTime? BumpedTime;

        public float GetFadeOpacity(double fadeDuration)
        {
            if (BumpedTime == null) return 1f;
            var elapsed = (DateTime.Now - BumpedTime.Value).TotalSeconds;
            return elapsed >= fadeDuration ? 0f : 1f - (float)(elapsed / fadeDuration);
        }

        public bool IsFadedOut(double fadeDuration)
        {
            if (BumpedTime == null) return false;
            return (DateTime.Now - BumpedTime.Value).TotalSeconds >= fadeDuration;
        }
    }
}
