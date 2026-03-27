using Patchwork;
using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Audio tab.
/// Left pane: loaded clips (from AudioList data).
/// Right pane: live playback log (from AudioLog data).
/// </summary>
public static class AudioPillar
{
    private static Vector2 _leftScroll;
    private static Vector2 _rightScroll;

    public static void Draw()
    {
        // ── Summary row ───────────────────────────────────────────────────────
        int clipCount = AudioHandler.CachedClipCount;
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"Summary: {clipCount} clip{(clipCount != 1 ? "s" : "")} loaded", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(6);

        // ── Two-pane layout ───────────────────────────────────────────────────
        GUILayout.BeginHorizontal();

        // Left — Loaded Clips
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Loaded Clips", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
        var clips = AudioList.GetClipNames();
        if (clips.Count == 0)
        {
            UnityEngine.GUI.contentColor = Color.yellow;
            GUILayout.Label("No clips loaded.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            foreach (string clip in clips)
                GUILayout.Label(clip, GUIHelper.LabelStyle);
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUIHelper.Space(6);

        // Right — Live Log
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Live Log", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        int maxVisible    = Mathf.Clamp(Plugin.Config.AudioLogMaxVisible, 5, 50);
        double fadeDuration = Plugin.Config.LogAudioDuration;

        _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
        AudioLog.DrawEntries(maxVisible, fadeDuration, Plugin.Config.HideModdedAudioInLog);
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // ── Config row ────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Log", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioLog.ClearLog();
        if (GUILayout.Button("Clear List", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioList.ClearList();
        GUILayout.EndHorizontal();
    }
}
