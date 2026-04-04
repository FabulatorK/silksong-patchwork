using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Live log of T2D sprite and texture setter triggers.
/// Mirrors the AudioLog / TextLog pattern — bump-to-front list, fade-out for old entries.
/// Populated via T2DLoader.OnT2DTrigger, which fires only when IsT2DLogActive is true.
/// Displayed by GraphicsPillar in the T2D Log sub-tab.
/// </summary>
public static class T2DLog
{
    private static readonly List<T2DLogEntry>            _entries = new();
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

            string label = string.IsNullOrEmpty(entry.SpriteName)
                ? entry.CleanTexName
                : $"{entry.CleanTexName} / {entry.SpriteName}";

            if (GUILayout.Button(label, GUIHelper.LabelStyle))
                onEntryClick?.Invoke(entry.CleanTexName, entry.SpriteName);
        }

        if (_entries.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No T2D textures triggered yet.", GUIHelper.LabelStyle);
        }

        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    // Minimum seconds between re-bumping a known entry to the top of the log.
    // Prevents animated sprites (which fire the setter every frame) from permanently
    // owning the top of the log and drowning out newly-triggered entries.
    // First-seen entries always appear immediately regardless of this value.
    private const double ReBumpCooldownSeconds = 2.0;

    /// <summary>
    /// Returns true if this is a sprite the log has never seen before — used by
    /// T2DTextureController to know when to add the sprite to the browser's entry list.
    /// </summary>
    public static void LogTrigger(string cleanTexName, string spriteName)
    {
        if (string.IsNullOrEmpty(cleanTexName)) return;

        string key = cleanTexName + "/" + (spriteName ?? "");
        if (_lookup.TryGetValue(key, out var existing))
        {
            // Within cooldown: suppress the re-bump entirely.
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
                CleanTexName = cleanTexName,
                SpriteName   = spriteName ?? "",
                LogTime      = DateTime.Now,
                BumpedTime   = null
            };
            _entries.Insert(0, entry);
            _lookup[key] = entry;

            // New sprite discovered — add it live to the browser's atlas entry so the
            // sprite list grows as the creator plays the game, without needing a refresh.
            if (!string.IsNullOrEmpty(spriteName))
                T2DTextureController.AddDiscoveredSprite(cleanTexName, spriteName);
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
        public string    SpriteName;
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
