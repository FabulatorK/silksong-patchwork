using System;
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
/// Dev Hub — Graphics tab.
/// Sub-tabs: Overview | Animated
/// Overview: sprite/T2D/spritesheet counts + file status table (absorbs SkinStatus).
/// Animated: AnimationController frame inspector (absorbs AnimationController UI).
/// </summary>
public static class GraphicsPillar
{
    // ── Sub-tab state ─────────────────────────────────────────────────────────
    private static int    _subTab = 0;
    private static readonly string[] SubTabNames = { "Overview", "Animated" };

    // ── Overview state ────────────────────────────────────────────────────────
    private static Vector2 _overviewScroll;
    private static Dictionary<string, List<FileEntry>> _scannedFiles;
    private static float _lastScanTime = -999f;
    private const  float ScanCooldown  = 2f;

    private static bool _showSprites = true;
    private static bool _showT2D     = true;
    private static bool _showAudio   = false;
    private static bool _showVideo   = false;
    private static bool _showText    = false;

    // ── Animated sub-tab state ────────────────────────────────────────────────
    private static Vector2 _animScroll;

    // ── Entry type ─────────────────────────────────────────────────────────────
    private enum FileStatus { OnDisk, Pending, Loaded }
    private class FileEntry
    {
        public string Name;
        public FileStatus Status;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public static void Draw()
    {
        // Sub-tab bar
        GUILayout.BeginHorizontal();
        for (int i = 0; i < SubTabNames.Length; i++)
        {
            bool active = _subTab == i;
            Color prev  = UnityEngine.GUI.backgroundColor;
            if (active) UnityEngine.GUI.backgroundColor = new Color(0.25f, 0.55f, 1f);
            if (GUILayout.Button(SubTabNames[i], GUIHelper.ButtonStyle, GUIHelper.Height(24)))
                _subTab = i;
            UnityEngine.GUI.backgroundColor = prev;
        }
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        if (_subTab == 0)
            DrawOverview();
        else
            DrawAnimated();
    }

    // ── Overview ──────────────────────────────────────────────────────────────

    private static void DrawOverview()
    {
        // Summary
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label(
            $"tk2d sprites: {SpriteLoader.LoadedSpriteCount} in {SpriteLoader.LoadedCollectionCount} collections  " +
            $"T2D: {T2DLoader.LoadedT2DSpriteCount} loaded / {T2DLoader.PreloadedT2DTextureCount} pending  " +
            $"Sheets: {T2DLoader.SpritesheetOverrideCount}",
            GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(4);

        // Refresh button
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh File Scan", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            _lastScanTime = -999f;

        if (GUILayout.Button("Reload Sprites", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            SpriteLoader.Reload();

        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        EnsureScanned();

        _overviewScroll = GUILayout.BeginScrollView(_overviewScroll);
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

        // Sprites
        var sprites = new List<FileEntry>();
        foreach (string packPath in Plugin.PluginPackPaths)
        {
            string dir = Path.Combine(packPath, "sprites");
            if (Directory.Exists(dir))
                ScanPngFiles(dir, sprites);
        }
        _scannedFiles["sprites"] = sprites;

        // T2D
        var t2d = new List<FileEntry>();
        ScanPngFiles(T2DLoader.AtlasLoadPath, t2d);
        var loadedT2D   = new HashSet<string>(T2DLoader.LoadedT2DSpriteNames);
        var pendingT2D  = new HashSet<string>(T2DLoader.PreloadedT2DTextureNames);
        var sheetNames  = new HashSet<string>(T2DLoader.SpritesheetOverrideNames);
        foreach (var e in t2d)
        {
            if (loadedT2D.Contains(e.Name) || sheetNames.Contains(e.Name))
                e.Status = FileStatus.Loaded;
            else if (pendingT2D.Contains(e.Name))
                e.Status = FileStatus.Pending;
        }
        _scannedFiles["t2d"] = t2d;

        // Sounds
        var sounds = new List<FileEntry>();
        if (Directory.Exists(AudioHandler.SoundFolder))
            ScanAllFiles(AudioHandler.SoundFolder, sounds, new[] { ".wav", ".ogg", ".mp3" });
        bool anyAudio = AudioHandler.CachedClipCount > 0;
        foreach (var e in sounds)
            e.Status = anyAudio ? FileStatus.Loaded : FileStatus.OnDisk;
        _scannedFiles["sounds"] = sounds;

        // Videos
        var videos = new List<FileEntry>();
        if (Directory.Exists(VideoHandler.VideoLoadPath))
            ScanAllFiles(VideoHandler.VideoLoadPath, videos, new[] { ".mp4", ".webm", ".ogv" });
        foreach (var e in videos)
        {
            if (VideoHandler.VideoFileMap.TryGetValue(e.Name, out var val) && val != null)
                e.Status = FileStatus.Loaded;
        }
        _scannedFiles["videos"] = videos;

        // Text
        var text = new List<FileEntry>();
        if (Directory.Exists(DialogueHandler.TextLoadPath))
            ScanAllFiles(DialogueHandler.TextLoadPath, text, new[] { ".yml" });
        _scannedFiles["text"] = text;
    }

    private static void DrawFileSection(string title, string category, ref bool expanded)
    {
        if (!_scannedFiles.TryGetValue(category, out var files))
            return;

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

    private static void ScanPngFiles(string directory, List<FileEntry> results)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories))
            results.Add(new FileEntry { Name = Path.GetFileNameWithoutExtension(file), Status = FileStatus.OnDisk });
    }

    private static void ScanAllFiles(string directory, List<FileEntry> results, string[] extensions)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (System.Array.IndexOf(extensions, ext) < 0) continue;
            results.Add(new FileEntry { Name = Path.GetFileName(file), Status = FileStatus.OnDisk });
        }
    }

    // ── Animated ──────────────────────────────────────────────────────────────

    private static void DrawAnimated()
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Animation Controller", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(4);

        _animScroll = GUILayout.BeginScrollView(_animScroll);
        AnimationController.DrawPillarContent();
        GUILayout.EndScrollView();
    }
}
