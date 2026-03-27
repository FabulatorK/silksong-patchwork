using System.IO;
using Patchwork;
using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Audio tab.
/// Left pane: loaded clip inventory (AudioList) + Clear List.
/// Right pane: live playback log (AudioLog) + Clear Log.
/// Summary row mirrors DashboardPillar audio counts.
/// </summary>
public static class AudioPillar
{
    private static Vector2 _leftScroll;
    private static Vector2 _rightScroll;

    // Disk-file count cached to avoid per-frame IO.
    private static int   _diskFileCount  = 0;
    private static float _lastScanTime   = -999f;
    private const  float ScanCooldown    = 3f;

    public static void Draw()
    {
        EnsureScanned();

        // ── Summary row ───────────────────────────────────────────────────────
        int cached  = AudioHandler.CachedClipCount;
        int tracked = AudioList.GetClipNames().Count;
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label(
            $"Audio replacements: {_diskFileCount} on disk  |  {tracked} tracked  |  {cached} cached  |  0 packed",
            GUIHelper.LabelStyle);
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
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No clips loaded.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            foreach (string clip in clips)
                GUILayout.Label(clip, GUIHelper.LabelStyle);
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("Clear List", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioList.ClearList();
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

        if (GUILayout.Button("Clear Log", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioLog.ClearLog();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private static void EnsureScanned()
    {
        if (Time.realtimeSinceStartup - _lastScanTime < ScanCooldown)
            return;
        _lastScanTime = Time.realtimeSinceStartup;

        int count = 0;
        if (Directory.Exists(AudioHandler.SoundFolder))
        {
            string[] exts = { ".wav", ".ogg", ".mp3" };
            foreach (string f in Directory.GetFiles(AudioHandler.SoundFolder, "*", SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (System.Array.IndexOf(exts, ext) >= 0)
                    count++;
            }
        }
        _diskFileCount = count;
    }
}
