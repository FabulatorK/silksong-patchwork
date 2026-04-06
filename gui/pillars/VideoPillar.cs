using System;
using System.Collections.Generic;
using System.Linq;
using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Video tab.
/// Left/top section: replacement file table (what's loaded on disk).
/// Bottom section: live cinematic trigger log (what the game actually played this session).
/// </summary>
public static class VideoPillar
{
    private static Vector2 _replacementsScroll;
    private static Vector2 _logScroll;

    // ── Cinematic trigger log ────────────────────────────────────────────────

    private sealed class CinematicEntry
    {
        public string   Name;
        public bool     Replaced;
        public DateTime Time;
    }

    private static readonly List<CinematicEntry> _log = new();

    /// <summary>Called by Plugin via VideoHandler.OnCinematicTriggered.</summary>
    public static void LogCinematic(string name, bool replaced)
    {
        // Move existing entry to top; otherwise insert new.
        var existing = _log.Find(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) _log.Remove(existing);
        _log.Insert(0, new CinematicEntry { Name = name, Replaced = replaced, Time = DateTime.Now });
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public static void Draw()
    {
        var videos = VideoHandler.VideoFileMap.Where(kv => kv.Value != null).ToList();

        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"Video replacements on disk: {videos.Count}", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(6);

        // ── Replacement table ────────────────────────────────────────────────
        if (videos.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(
                "No video replacements loaded.\nPlace .mp4 / .webm / .ogv files in the pack Videos folder.",
                GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            _replacementsScroll = GUILayout.BeginScrollView(_replacementsScroll,
                GUILayout.MaxHeight(GUIHelper.Scaled(160f)));

            GUILayout.BeginHorizontal();
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
            GUILayout.Label("Name",   GUIHelper.LabelStyle, GUIHelper.Width(200));
            GUILayout.Label("Status", GUIHelper.LabelStyle, GUIHelper.Width(80));
            UnityEngine.GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();

            foreach (var kv in videos)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key, GUIHelper.LabelStyle, GUIHelper.Width(200));
                UnityEngine.GUI.contentColor = Color.green;
                GUILayout.Label("\u25CF loaded", GUIHelper.LabelStyle, GUIHelper.Width(80));
                UnityEngine.GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        GUIHelper.Space(8);

        // ── Cinematic trigger log ────────────────────────────────────────────
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
        GUILayout.BeginHorizontal();
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Live Cinematic Log", GUIHelper.LabelStyle, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = Color.white;
        if (GUILayout.Button("Clear", GUIHelper.ButtonStyle, GUIHelper.Width(50f), GUIHelper.Height(20)))
            _log.Clear();
        GUILayout.EndHorizontal();

        _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));

        if (_log.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No cinematics triggered this session.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            foreach (var entry in _log)
            {
                GUILayout.BeginHorizontal();

                // Click copies name to clipboard
                if (GUILayout.Button(GUIHelper.TT(entry.Name, "Click to copy name"),
                        GUIHelper.LabelStyle, GUILayout.ExpandWidth(true)))
                    GUIUtility.systemCopyBuffer = entry.Name;

                // Replaced badge
                if (entry.Replaced)
                {
                    UnityEngine.GUI.contentColor = new Color(0.4f, 1f, 0.4f);
                    GUILayout.Label("[R]", GUIHelper.LabelStyle, GUIHelper.Width(28f));
                }
                else
                {
                    GUILayout.Space(GUIHelper.Scaled(28f));
                }

                // Timestamp
                UnityEngine.GUI.contentColor = new Color(0.45f, 0.45f, 0.45f);
                GUILayout.Label(entry.Time.ToString("HH:mm:ss"), GUIHelper.LabelStyle,
                    GUIHelper.Width(56f));

                UnityEngine.GUI.contentColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
}
