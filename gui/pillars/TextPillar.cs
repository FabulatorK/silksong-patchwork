using System.Collections.Generic;
using Patchwork;
using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Text tab.
///
/// Layout:
///   Top-left  — Recent accessed keys (temporal, fades, clickable)
///   Top-right — Search: TextLog entries + loaded cache overrides (non-empty filter)
///   Bottom    — Dialogue Editor + direct sheet/key open row
/// </summary>
public static class TextPillar
{
    private static Vector2 _recentScroll;
    private static Vector2 _searchScroll;
    private static string  _searchFilter = "";

    // Direct key-open row state (sits above the editor).
    private static string _jumpSheet = "";
    private static string _jumpKey   = "";

    // Height of the top row (leaves room for editor below).
    private const float TopRowHeight = 250f;

    public static void Draw()
    {
        int maxVisible    = Mathf.Clamp(Plugin.Config.TextLogMaxVisible, 5, 50);
        double fadeDuration = Plugin.Config.TextLogDuration;

        // ── Top row ───────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal(GUILayout.Height(GUIHelper.Scaled(TopRowHeight)));

        // Left — Recent log
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"Recent  ({TextLog.EntryCount})", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        _recentScroll = GUILayout.BeginScrollView(_recentScroll, GUILayout.ExpandHeight(true));
        TextLog.DrawEntries(maxVisible, fadeDuration, onEntryClick: (sheet, key, text) =>
            DialogueEditor.SelectEntry(sheet, key, text));
        GUILayout.EndScrollView();

        if (GUILayout.Button("Clear Log", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            TextLog.ClearLog();
        GUILayout.EndVertical();

        GUIHelper.Space(6);

        // Right — Search (TextLog + loaded cache overrides)
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Search", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        _searchFilter = GUIHelper.TextField("TextPillar.Search", _searchFilter, GUILayout.ExpandWidth(true));
        GUIHelper.Space(2);

        _searchScroll = GUILayout.BeginScrollView(_searchScroll, GUILayout.ExpandHeight(true));
        DrawSearchResults(_searchFilter);
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // ── Bottom — Editor ───────────────────────────────────────────────────
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandHeight(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Editor", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        // Direct sheet/key open — lets users edit any known key without triggering it in-game.
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sheet:", GUIHelper.LabelStyle, GUIHelper.Width(42));
        _jumpSheet = GUIHelper.TextField("TextPillar.JumpSheet", _jumpSheet, GUIHelper.Width(110));
        GUILayout.Label("Key:", GUIHelper.LabelStyle, GUIHelper.Width(32));
        _jumpKey = GUIHelper.TextField("TextPillar.JumpKey", _jumpKey, GUILayout.ExpandWidth(true));
        bool canJump = !string.IsNullOrWhiteSpace(_jumpSheet) && !string.IsNullOrWhiteSpace(_jumpKey);
        UnityEngine.GUI.enabled = canJump;
        if (GUILayout.Button("Open", GUIHelper.ButtonStyle, GUIHelper.Width(50), GUIHelper.Height(22)))
        {
            DialogueEditor.SelectEntry(_jumpSheet.Trim(), _jumpKey.Trim());
            _jumpSheet = "";
            _jumpKey   = "";
        }
        UnityEngine.GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUIHelper.Space(2);
        DialogueEditor.DrawEditorSurface();
        GUILayout.EndVertical();
    }

    /// <summary>
    /// Renders search results from two sources, deduplicated:
    ///   1. TextLog entries (keys seen in-game this session) — always searched.
    ///   2. DialogueHandler.TextCache overrides (loaded YAML keys, not yet seen this
    ///      session) — only when the filter is non-empty.
    /// Cache-only hits are tinted blue so users can tell them apart at a glance.
    /// </summary>
    private static void DrawSearchResults(string filter)
    {
        bool hasFilter = !string.IsNullOrEmpty(filter);
        var seenKeys = new HashSet<string>();
        bool any = false;

        const int MaxPreview = 45;

        // Source 1 — TextLog.  out_seen is populated by DrawFilteredEntries so we can
        // deduplicate the cache results below.  suppressEmpty defers the "no results"
        // label until we know whether the cache has anything to add.
        TextLog.DrawFilteredEntries(
            filter,
            onEntryClick: (sheet, key, text) => DialogueEditor.SelectEntry(sheet, key, text),
            out_seen: seenKeys,
            suppressEmpty: hasFilter);
        any = seenKeys.Count > 0;

        // Source 2 — cache overrides not already shown from the log
        if (hasFilter)
        {
            foreach (var (sheet, key, value) in DialogueHandler.SearchCache(filter, seenKeys))
            {
                string preview = value.Replace("\n", "\\n").Replace("\r", "\\r");
                if (preview.Length > MaxPreview) preview = preview.Substring(0, MaxPreview - 3) + "...";
                UnityEngine.GUI.contentColor = new Color(0.8f, 0.9f, 1f);  // blue tint — override not yet seen
                if (GUILayout.Button($"{sheet}.{key}: {preview}", GUIHelper.LabelStyle))
                    DialogueEditor.SelectEntry(sheet, key, value);
                UnityEngine.GUI.contentColor = Color.white;
                any = true;
            }
        }

        // Combined empty state
        if (!any)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(
                hasFilter
                    ? "No matches in log or loaded overrides."
                    : "Type to search. Recent dialogue keys appear on the left.",
                GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
    }
}
