using System.Collections.Generic;
using System.IO;
using System.Linq;
using Patchwork;
using Patchwork.GUI;
using Patchwork.Handlers;
using Patchwork.Packs;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Dashboard tab.
/// Cross-asset file-scan overview: sprite / T2D / audio / video / text counts
/// with per-file load-status badges. Extracted from GraphicsPillar.
/// </summary>
public static class DashboardPillar
{
    private static Vector2 _scroll;
    private static Dictionary<string, List<FileEntry>> _scannedFiles;
    private static float _lastScanTime = -999f;
    private const  float ScanCooldown  = 2f;

    private static bool _showSprites = true;
    private static bool _showT2D     = true;
    private static bool _showAudio   = false;
    private static bool _showVideo   = false;
    private static bool _showText    = false;

    private enum FileStatus { OnDisk, Pending, Loaded }
    private class FileEntry
    {
        public string Name;
        public FileStatus Status;
    }

    public static void Draw()
    {
        // ── Summary row ───────────────────────────────────────────────────────
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label(
            $"tk2d sprites: {SpriteLoader.LoadedSpriteCount} in {SpriteLoader.LoadedCollectionCount} collections  " +
            $"T2D: {T2DLoader.LoadedT2DSpriteCount} loaded / {T2DLoader.PreloadedT2DTextureCount} pending  " +
            $"Sheets: {T2DLoader.SpritesheetOverrideCount}",
            GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(4);

        // ── Action buttons ────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh File Scan", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            _lastScanTime = -999f;
        if (GUILayout.Button("Reload Sprites", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            SpriteLoader.Reload();
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        EnsureScanned();

        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawFileSection("Sprites",     "sprites", ref _showSprites);
        DrawFileSection("T2D Sprites", "t2d",     ref _showT2D);
        DrawFileSection("Sounds",      "sounds",  ref _showAudio);
        DrawFileSection("Videos",      "videos",  ref _showVideo);
        DrawFileSection("Text",        "text",    ref _showText);
        GUILayout.EndScrollView();
    }

    private static void EnsureScanned()
    {
        if (_scannedFiles != null && Time.realtimeSinceStartup - _lastScanTime < ScanCooldown)
            return;

        _scannedFiles = new Dictionary<string, List<FileEntry>>();
        _lastScanTime = Time.realtimeSinceStartup;

        var sprites = new List<FileEntry>();
        foreach (string packPath in Plugin.PluginPackPaths)
        {
            string dir = Path.Combine(packPath, "sprites");
            if (Directory.Exists(dir))
                ScanPng(dir, sprites);
        }
        _scannedFiles["sprites"] = sprites;

        var t2d = new List<FileEntry>();
        ScanPng(T2DLoader.AtlasLoadPath, t2d);
        var loadedT2D  = new HashSet<string>(T2DLoader.LoadedT2DSpriteNames);
        var pendingT2D = new HashSet<string>(T2DLoader.PreloadedT2DTextureNames);
        var sheetNames = new HashSet<string>(T2DLoader.SpritesheetOverrideNames);
        foreach (var e in t2d)
        {
            if (loadedT2D.Contains(e.Name) || sheetNames.Contains(e.Name))
                e.Status = FileStatus.Loaded;
            else if (pendingT2D.Contains(e.Name))
                e.Status = FileStatus.Pending;
        }
        _scannedFiles["t2d"] = t2d;

        var sounds = new List<FileEntry>();
        if (Directory.Exists(AudioHandler.SoundFolder))
            ScanExt(AudioHandler.SoundFolder, sounds, new[] { ".wav", ".ogg", ".mp3" });
        bool anyAudio = AudioHandler.CachedClipCount > 0;
        foreach (var e in sounds)
            e.Status = anyAudio ? FileStatus.Loaded : FileStatus.OnDisk;
        _scannedFiles["sounds"] = sounds;

        var videos = new List<FileEntry>();
        if (Directory.Exists(VideoHandler.VideoLoadPath))
            ScanExt(VideoHandler.VideoLoadPath, videos, new[] { ".mp4", ".webm", ".ogv" });
        foreach (var e in videos)
            if (VideoHandler.VideoFileMap.TryGetValue(e.Name, out var val) && val != null)
                e.Status = FileStatus.Loaded;
        _scannedFiles["videos"] = videos;

        var text = new List<FileEntry>();
        if (Directory.Exists(DialogueHandler.TextLoadPath))
            ScanExt(DialogueHandler.TextLoadPath, text, new[] { ".yml" });
        _scannedFiles["text"] = text;
    }

    private static void DrawFileSection(string title, string key, ref bool expanded)
    {
        if (!_scannedFiles.TryGetValue(key, out var files)) return;

        GUILayout.BeginHorizontal();
        string arrow = expanded ? "\u25BC" : "\u25BA";
        if (GUILayout.Button($"{arrow} {title} ({files.Count})", GUIHelper.LabelStyle, GUIHelper.Height(22)))
            expanded = !expanded;
        GUILayout.EndHorizontal();

        if (!expanded) return;

        foreach (var entry in files)
        {
            Color c = entry.Status switch
            {
                FileStatus.Loaded  => Color.green,
                FileStatus.Pending => Color.yellow,
                _                  => new Color(0.7f, 0.7f, 0.7f)
            };
            string badge = entry.Status switch
            {
                FileStatus.Loaded  => "\u25CF loaded",
                FileStatus.Pending => "\u25CB pending",
                _                  => "\u25A1 on-disk"
            };
            GUILayout.BeginHorizontal();
            GUILayout.Label("  " + entry.Name, GUIHelper.LabelStyle, GUILayout.ExpandWidth(true));
            UnityEngine.GUI.contentColor = c;
            GUILayout.Label(badge, GUIHelper.LabelStyle, GUIHelper.Width(90));
            UnityEngine.GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();
        }
    }

    private static void ScanPng(string directory, List<FileEntry> out_)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string f in Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories))
            out_.Add(new FileEntry { Name = Path.GetFileNameWithoutExtension(f), Status = FileStatus.OnDisk });
    }

    private static void ScanExt(string directory, List<FileEntry> out_, string[] extensions)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string f in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (System.Array.IndexOf(extensions, ext) < 0) continue;
            out_.Add(new FileEntry { Name = Path.GetFileName(f), Status = FileStatus.OnDisk });
        }
    }
}
