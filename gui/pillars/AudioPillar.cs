using System.Collections.Generic;
using System.IO;
using Patchwork;
using Patchwork.GUI;
using Patchwork.Handlers;
using Patchwork.Util;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Audio tab.
///
/// Left pane:  full clip browser from memory sweep (AudioList).
///             Finds clips that never fire through a harmony-patched play path —
///             ambient sounds, music, pre-assigned clips, T2D-style pre-loads.
///             Each entry shows length, channel count, and a [R] badge when a
///             replacement file exists in the active pack.
///
/// Right pane: live play log (AudioLog).
///             Each entry shows "clip_name ← GameObjectPath" so creators
///             immediately know what action triggered a sound.
///             Clicking a log entry focuses it in the left pane.
/// </summary>
public static class AudioPillar
{
    private static Vector2 _leftScroll;
    private static Vector2 _rightScroll;

    // Left-pane selection state
    private static AudioClipEntry _selected;
    private static bool           _hasSelection;
    private static string         _searchFilter = "";

    // Filtered entry list — rebuilt only when filter text or AudioList.Version changes.
    // Never iterated to draw all rows; virtual scroll reads a slice by index.
    private static readonly List<AudioClipEntry> _filteredEntries = new();
    private static string _lastAppliedFilter = null;
    private static int    _lastListVersion   = -1;

    // Height of the browser scroll view captured last Repaint — used to compute visible row range.
    private static float _browserScrollViewH = 300f;

    // Unscaled row height — must match the Height() constraint in DrawBrowserRow.
    private const float RowH = 22f;

    // Disk-file count cached to avoid per-frame IO.
    private static int   _diskFileCount = 0;
    private static float _lastScanTime  = -999f;
    private const  float ScanCooldown   = 3f;

    public static void Draw()
    {
        // Signal handler that the browser is open — enables the full sweep path.
        AudioHandler.IsAudioBrowserActive = true;

        EnsureScanned();
        if (AudioList.NeedsRefresh)
            AudioList.Refresh();

        // Consume any pending focus request from the log
        string focus = AudioLog.ConsumePendingFocus();
        if (!string.IsNullOrEmpty(focus))
            FocusClipByName(focus);

        // ── Summary row ───────────────────────────────────────────────────────
        int total   = AudioList.Entries.Count;
        int cached  = AudioHandler.CachedClipCount;
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label(
            $"Audio: {_diskFileCount} replacement(s) on disk  |  {total} clip(s) in memory  |  {cached} cached",
            GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(6);

        // ── Two-pane layout ───────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        DrawBrowserPane();
        GUIHelper.Space(6);
        DrawLogPane();
        GUILayout.EndHorizontal();
    }

    // ── Left pane: clip browser ──────────────────────────────────────────────

    private static void DrawBrowserPane()
    {
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box, GUILayout.ExpandWidth(true));

        // Header + search + refresh
        GUILayout.BeginHorizontal();
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Clip Browser", GUIHelper.LabelStyle, GUILayout.ExpandWidth(false));
        UnityEngine.GUI.contentColor = Color.white;
        _searchFilter = GUIHelper.TextField("Audio.Search", _searchFilter,
            GUILayout.ExpandWidth(true), GUIHelper.Height(20));
        if (!string.IsNullOrEmpty(_searchFilter) &&
            GUILayout.Button("x", GUIHelper.ButtonStyle, GUIHelper.Width(22f), GUIHelper.Height(20)))
            _searchFilter = "";
        if (GUILayout.Button("↺", GUIHelper.ButtonStyle, GUIHelper.Width(26f), GUIHelper.Height(20)))
            AudioList.RequestRefresh();
        GUILayout.EndHorizontal();

        // Rebuild filtered list only when search text or list contents change.
        if (_lastAppliedFilter != _searchFilter || _lastListVersion != AudioList.Version)
        {
            if (_lastAppliedFilter != _searchFilter)
                _leftScroll.y = 0; // reset scroll when filter text changes
            _lastAppliedFilter = _searchFilter;
            _lastListVersion   = AudioList.Version;
            _filteredEntries.Clear();
            foreach (var e in AudioList.Entries)
            {
                if (string.IsNullOrEmpty(_searchFilter) ||
                    e.ClipName.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _filteredEntries.Add(e);
            }
        }

        // Virtual scroll: only instantiate IMGUI controls for visible rows.
        // GUILayout.BeginScrollView renders ALL children even when outside the viewport —
        // this becomes the dominant OnGUI cost with 200+ clips.
        float scaledRowH = GUIHelper.Scaled(RowH);
        int   total      = _filteredEntries.Count;

        _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

        if (total == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(string.IsNullOrEmpty(_searchFilter)
                ? "No clips in memory.\nLoad a save to populate."
                : "No matches.", GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            int first = Mathf.Max(0, Mathf.FloorToInt(_leftScroll.y / scaledRowH) - 1);
            int last  = Mathf.Min(total, first + Mathf.CeilToInt(_browserScrollViewH / scaledRowH) + 2);

            if (first > 0)
                GUILayout.Space(first * scaledRowH);

            for (int i = first; i < last; i++)
                DrawBrowserRow(_filteredEntries[i]);

            if (last < total)
                GUILayout.Space((total - last) * scaledRowH);
        }

        GUILayout.EndScrollView();

        // Capture scroll view height one frame after layout so the virtual window is accurate.
        if (Event.current.type == EventType.Repaint)
            _browserScrollViewH = GUILayoutUtility.GetLastRect().height;

        // Detail pane for selected clip (source associations + edit button)
        if (_hasSelection && _selected != null)
            DrawSelectionDetail(_selected);

        if (GUILayout.Button("Clear List", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            AudioList.ClearList();

        GUILayout.EndVertical();
    }

    private static void DrawBrowserRow(AudioClipEntry entry)
    {
        bool sel = _hasSelection && _selected?.ClipName == entry.ClipName;
        // Height must match RowH so virtual scroll calculations stay accurate.
        GUILayout.BeginHorizontal(GUIHelper.Height(RowH));

        if (sel) UnityEngine.GUI.contentColor = new Color(0.6f, 0.9f, 1f);
        if (GUILayout.Button(GUIHelper.TT(entry.ClipName, "Click to copy name"), GUIHelper.LabelStyle, GUILayout.ExpandWidth(true)))
        {
            GUIUtility.systemCopyBuffer = entry.ClipName;
            _selected     = entry;
            _hasSelection = true;
        }
        UnityEngine.GUI.contentColor = Color.white;

        // Duration badge
        UnityEngine.GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
        GUILayout.Label($"{entry.LengthSeconds:0.0}s", GUIHelper.LabelStyle, GUIHelper.Width(36f));
        UnityEngine.GUI.contentColor = Color.white;

        // Replacement badge
        if (entry.HasReplacement)
        {
            UnityEngine.GUI.contentColor = new Color(0.4f, 1f, 0.4f);
            GUILayout.Label("[R]", GUIHelper.LabelStyle, GUIHelper.Width(28f));
            UnityEngine.GUI.contentColor = Color.white;
        }
        else
        {
            GUILayout.Space(GUIHelper.Scaled(28f));
        }

        GUILayout.EndHorizontal();
    }

    private static void DrawSelectionDetail(AudioClipEntry entry)
    {
        GUIHelper.Space(4);
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"{entry.ClipName}  {entry.LengthSeconds:0.00}s  " +
                        $"ch:{entry.Channels}  {entry.Frequency}Hz", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        // Sources currently holding this clip
        if (entry.SourcePaths.Count > 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
            foreach (var p in entry.SourcePaths)
                GUILayout.Label("  ← " + p, GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Edit", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            EditClip(entry);
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);
    }

    // ── Right pane: live log ─────────────────────────────────────────────────

    private static void DrawLogPane()
    {
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
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void FocusClipByName(string clipName)
    {
        // Clear the search filter so the target clip is guaranteed to be in _filteredEntries.
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            _searchFilter = "";
            _lastAppliedFilter = null; // force filter rebuild next frame
        }

        for (int i = 0; i < _filteredEntries.Count; i++)
        {
            if (!string.Equals(_filteredEntries[i].ClipName, clipName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            _selected     = _filteredEntries[i];
            _hasSelection = true;

            // Scroll so the row is centred in the visible window.
            float scaledRowH = GUIHelper.Scaled(RowH);
            _leftScroll.y = Mathf.Max(0f, i * scaledRowH - _browserScrollViewH * 0.5f);
            return;
        }

        // Clip played but not yet in the sweep — schedule a refresh so it appears.
        AudioList.RequestRefresh();
    }

    private static void EditClip(AudioClipEntry entry)
    {
        string dir  = AudioHandler.SoundFolder;
        // Prefer .ogg for new files; if a replacement already exists, use that extension
        string path = System.IO.Path.Combine(dir, entry.ClipName + ".ogg");
        foreach (string ext in new[] { ".ogg", ".wav", ".mp3", ".aiff", ".aif" })
        {
            string candidate = System.IO.Path.Combine(dir, entry.ClipName + ext);
            if (File.Exists(candidate)) { path = candidate; break; }
        }

        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start(path);
        }
        else
        {
            // No replacement exists yet — open the target folder so the user can drop a file in.
            IOUtil.EnsureDirectoryExists(dir);
            System.Diagnostics.Process.Start(dir);
        }
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
