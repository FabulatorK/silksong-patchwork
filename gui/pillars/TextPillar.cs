using Patchwork;
using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Text tab.
/// Left pane: accessed dialogue keys (from TextLog data), clickable.
/// Right pane: Dialogue Editor — edit selected entry.
/// Clicking a key in the left pane populates the right pane directly.
/// </summary>
public static class TextPillar
{
    private static Vector2 _leftScroll;

    public static void Draw()
    {
        // ── Summary row ───────────────────────────────────────────────────────
        int entryCount = TextLog.EntryCount;
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"Accessed text keys: {entryCount}", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(6);

        // ── Two-pane layout ───────────────────────────────────────────────────
        GUILayout.BeginHorizontal();

        // Left — Accessed Keys
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(GUIHelper.Scaled(300)));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Accessed Keys", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

        int maxVisible    = Mathf.Clamp(Plugin.Config.TextLogMaxVisible, 5, 50);
        double fadeDuration = Plugin.Config.TextLogDuration;

        // DrawEntries returns true when an entry was clicked; TextLog updates selected entry internally
        TextLog.DrawEntries(maxVisible, fadeDuration, onEntryClick: (sheet, key, text) =>
        {
            DialogueEditor.SelectEntry(sheet, key, text);
        });

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUIHelper.Space(6);

        // Right — Dialogue Editor surface
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Editor", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        DialogueEditor.DrawEditorSurface();

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // ── Actions row ───────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Log", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            TextLog.ClearLog();
        GUILayout.EndHorizontal();
    }
}
