using Patchwork;
using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Text tab.
///
/// Layout:
///   Top-left  — Recent accessed keys (temporal, fades, clickable)
///   Top-right — Search across all accessed keys (filter box + results)
///   Bottom    — Dialogue Editor (populated by clicking any entry above)
/// </summary>
public static class TextPillar
{
    private static Vector2 _recentScroll;
    private static Vector2 _searchScroll;
    private static string  _searchFilter = "";

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

        // Right — Search
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Search", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        _searchFilter = GUILayout.TextField(_searchFilter, GUIHelper.LabelStyle);
        GUIHelper.Space(2);

        _searchScroll = GUILayout.BeginScrollView(_searchScroll, GUILayout.ExpandHeight(true));
        TextLog.DrawFilteredEntries(_searchFilter, onEntryClick: (sheet, key, text) =>
            DialogueEditor.SelectEntry(sheet, key, text));
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // ── Bottom — Editor ───────────────────────────────────────────────────
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandHeight(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Editor", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        DialogueEditor.DrawEditorSurface();
        GUILayout.EndVertical();
    }
}
