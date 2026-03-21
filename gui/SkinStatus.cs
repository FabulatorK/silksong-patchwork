using System.Collections.Generic;
using System.IO;
using System.Linq;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI;

public static class SkinStatus
{
    private const float WindowWidth = 360f;
    private const float WindowHeight = 480f;
    private const float LeftMargin = 10f;
    private const float TopMargin = 420f;

    private static Rect windowRect;
    private static bool initialized;
    private static Vector2 scrollPosition;
    private static string searchText = "";

    // Cached file scan results (refreshed on demand, not every frame)
    private static Dictionary<string, List<FileEntry>> scannedFiles;
    private static string lastScanTime = "never";

    // Section collapse state
    private static bool showSprites = true;
    private static bool showT2D = true;
    private static bool showAudio = true;
    private static bool showVideo = true;
    private static bool showText = true;
    private static bool showPluginPacks = true;

    public static void Draw()
    {
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRect(LeftMargin, TopMargin, WindowWidth, WindowHeight);
            initialized = true;
        }

        windowRect = GUILayout.Window(
            6976,
            windowRect,
            DrawWindow,
            "Patchwork Skin Status",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    private static void DrawWindow(int windowID)
    {
        GUIHelper.Space(16);

        // Scan button at top
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Files", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            RefreshFileScan();
        GUILayout.Label($"Last scan: {lastScanTime}", GUIHelper.LabelStyle);
        GUILayout.EndHorizontal();

        // Search field
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUIHelper.LabelStyle, GUILayout.Width(GUIHelper.Scaled(60)));
        searchText = GUIHelper.TextField(
            searchText,
            GUILayout.Width(GUIHelper.Scaled(280)),
            GUILayout.Height(GUIHelper.Scaled(32))
        );
        GUILayout.EndHorizontal();

        GUIHelper.Space(4);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        // --- Overview ---
        SectionHeader("Overview");
        Label($"tk2d sprites loaded: {SpriteLoader.LoadedSpriteCount} in {SpriteLoader.LoadedCollectionCount} collections");
        Label($"T2D spritesheets: {T2DHandler.SpritesheetOverrideCount}");
        Label($"T2D sprites: {T2DHandler.LoadedT2DSpriteCount} loaded, {T2DHandler.PreloadedT2DTextureCount} pending");
        Label($"Audio clips: {AudioHandler.CachedClipCount}");
        Label($"Video overrides: {VideoHandler.VideoFileMap.Count(kv => kv.Value != null)}");
        Label($"Text sheets: {DialogueHandler.CachedSheetCount} ({DialogueHandler.CachedKeyCount} keys)");
        if (DialogueHandler.StaleKeyCount > 0)
        {
            UnityEngine.GUI.contentColor = Color.yellow;
            Label($"  \u26A0 {DialogueHandler.StaleKeyCount} stale text key(s) — check log for details");
            UnityEngine.GUI.contentColor = Color.white;
        }

        // --- Per-category file lists ---
        if (scannedFiles != null)
        {
            GUIHelper.Space(8);
            DrawFileSection("Sprites", "sprites", ref showSprites);
            DrawFileSection("T2D Sprites", "t2d", ref showT2D);
            DrawFileSection("Sounds", "sounds", ref showAudio);
            DrawFileSection("Videos", "videos", ref showVideo);
            DrawFileSection("Text", "text", ref showText);
        }

        // --- Plugin Packs ---
        GUIHelper.Space(8);
        showPluginPacks = SectionToggle("Plugin Packs", showPluginPacks);
        if (showPluginPacks)
        {
            if (Plugin.PluginPackPaths.Count == 0)
            {
                DimLabel("  No plugin packs detected");
            }
            else
            {
                foreach (var path in Plugin.PluginPackPaths)
                {
                    string folderName = Path.GetFileName(Path.GetDirectoryName(path));
                    Label($"  \u2022 {folderName}");

                    // Show what asset types this pack provides
                    var types = new List<string>();
                    if (Directory.Exists(Path.Combine(path, "Sprites"))) types.Add("sprites");
                    if (Directory.Exists(Path.Combine(path, "Sounds"))) types.Add("sounds");
                    if (Directory.Exists(Path.Combine(path, "Videos"))) types.Add("videos");
                    if (Directory.Exists(Path.Combine(path, "Text"))) types.Add("text");
                    DimLabel($"    [{string.Join(", ", types)}]");
                }
            }
        }

        GUILayout.EndScrollView();
        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    private static void DrawFileSection(string title, string category, ref bool expanded)
    {
        if (!scannedFiles.ContainsKey(category))
            return;

        var allFiles = scannedFiles[category];
        var files = string.IsNullOrEmpty(searchText)
            ? allFiles
            : allFiles.Where(f => f.Name.ToLower().Contains(searchText.ToLower())).ToList();

        string countLabel = string.IsNullOrEmpty(searchText)
            ? $"{files.Count} files"
            : $"{files.Count} of {allFiles.Count} files";
        expanded = SectionToggle($"{title} ({countLabel})", expanded);
        if (!expanded) return;

        if (files.Count == 0)
        {
            DimLabel("  No files found");
            return;
        }

        foreach (var file in files)
        {
            Color color;
            string status;
            switch (file.Status)
            {
                case FileStatus.Loaded:
                    color = Color.green;
                    status = "\u2713";
                    break;
                case FileStatus.Pending:
                    color = Color.yellow;
                    status = "\u25CB";
                    break;
                default:
                    color = new Color(0.6f, 0.6f, 0.6f);
                    status = "\u00B7";
                    break;
            }

            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"  {status} {file.Name}", GUIHelper.LabelStyle);
        }
        UnityEngine.GUI.contentColor = Color.white;
    }

    private static void RefreshFileScan()
    {
        scannedFiles = new Dictionary<string, List<FileEntry>>();

        // Sprites (tk2d)
        var sprites = new List<FileEntry>();
        ScanPngFiles(SpriteLoader.LoadPath, sprites);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanPngFiles(Path.Combine(packPath, "Sprites"), sprites);
        // All tk2d sprites are loaded on-demand by Init postfix, mark as loaded since we can't easily track individual files
        foreach (var f in sprites) f.Status = FileStatus.Loaded;
        scannedFiles["sprites"] = sprites;

        // T2D sprites
        var t2d = new List<FileEntry>();
        ScanPngFiles(Path.Combine(SpriteLoader.LoadPath, "T2D"), t2d);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanPngFiles(Path.Combine(packPath, "Sprites", "T2D"), t2d);
        // T2D spritesheet overrides
        ScanPngFiles(T2DHandler.T2DAtlasLoadPath, t2d);

        var loadedT2DNames = new HashSet<string>(T2DHandler.LoadedT2DSpriteNames);
        var pendingT2DNames = new HashSet<string>(T2DHandler.PreloadedT2DTextureNames);
        var sheetNames = new HashSet<string>(T2DHandler.SpritesheetOverrideNames);
        foreach (var f in t2d)
        {
            if (loadedT2DNames.Contains(f.Name) || sheetNames.Contains(f.Name))
                f.Status = FileStatus.Loaded;
            else if (pendingT2DNames.Contains(f.Name))
                f.Status = FileStatus.Pending;
        }
        scannedFiles["t2d"] = t2d;

        // Sounds
        var sounds = new List<FileEntry>();
        ScanAllFiles(AudioHandler.SoundFolder, sounds, new[] { ".wav", ".ogg", ".mp3", ".flac", ".aiff" });
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanAllFiles(Path.Combine(packPath, "Sounds"), sounds, new[] { ".wav", ".ogg", ".mp3", ".flac", ".aiff" });
        foreach (var f in sounds)
        {
            // AudioHandler caches by name without extension
            if (AudioHandler.CachedClipCount > 0)
                f.Status = FileStatus.Loaded; // Can't easily check individual names, but if cache has entries the system is working
        }
        scannedFiles["sounds"] = sounds;

        // Videos
        var videos = new List<FileEntry>();
        ScanAllFiles(VideoHandler.VideoLoadPath, videos, new[] { ".mp4", ".webm", ".ogv" });
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanAllFiles(Path.Combine(packPath, "Videos"), videos, new[] { ".mp4", ".webm", ".ogv" });
        foreach (var f in videos)
        {
            if (VideoHandler.VideoFileMap.TryGetValue(f.Name, out var val) && val != null)
                f.Status = FileStatus.Loaded;
        }
        scannedFiles["videos"] = videos;

        // Text
        var text = new List<FileEntry>();
        ScanAllFiles(DialogueHandler.TextLoadPath, text, new[] { ".yml" });
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanAllFiles(Path.Combine(packPath, "Text"), text, new[] { ".yml" });
        foreach (var f in text) f.Status = FileStatus.Loaded; // Text is loaded on demand by Language.Get
        scannedFiles["text"] = text;

        lastScanTime = System.DateTime.Now.ToString("HH:mm:ss");
    }

    private static void ScanPngFiles(string directory, List<FileEntry> results)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string relativePath = file.Substring(Plugin.BasePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            results.Add(new FileEntry { Name = name, RelativePath = relativePath, Status = FileStatus.OnDisk });
        }
    }

    private static void ScanAllFiles(string directory, List<FileEntry> results, string[] extensions)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!extensions.Contains(ext)) continue;
            string name = Path.GetFileNameWithoutExtension(file);
            results.Add(new FileEntry { Name = name, RelativePath = file, Status = FileStatus.OnDisk });
        }
    }

    // --- Helpers ---

    private static void SectionHeader(string text)
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"--- {text} ---", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
    }

    private static bool SectionToggle(string text, bool current)
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        string arrow = current ? "\u25BC" : "\u25B6";
        bool clicked = GUILayout.Button($"{arrow} {text}", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        return clicked ? !current : current;
    }

    private static void Label(string text)
    {
        GUILayout.Label(text, GUIHelper.LabelStyle);
    }

    private static void DimLabel(string text)
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
        GUILayout.Label(text, GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
    }

    private class FileEntry
    {
        public string Name;
        public string RelativePath;
        public FileStatus Status;
    }

    private enum FileStatus
    {
        OnDisk,
        Pending,
        Loaded
    }
}