using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Patchwork.Handlers;
using Patchwork.Util;
using UnityEngine;
using GUI = UnityEngine.GUI;

namespace Patchwork.GUI;

/// <summary>
/// Two-pane T2D texture browser.  Left: searchable atlas/texture list.
/// Right: live preview + sprite list + edit/dump buttons.
/// Mirrors the AnimationController pattern for tk2d sprites.
/// Called by GraphicsPillar (T2D sub-tab).
/// </summary>
public static class T2DTextureController
{
    // ── State ────────────────────────────────────────────────────────────────
    private static List<T2DSceneEntry> _entries = new();
    private static float               _lastRefresh = -999f;
    private const  float               RefreshInterval = 2f;

    private static T2DSceneEntry _selected;
    private static bool          _hasSelection;
    private static string        _selectedSprite = "";

    private static string  _searchFilter = "";
    private static Vector2 _listScroll;
    private static Vector2 _spriteScroll;

    // Small reusable 1×1 white texture for highlight overlays
    private static Texture2D _white;

    // ── Public entry point ───────────────────────────────────────────────────

    public static void DrawPillarContent()
    {
        MaybeRefresh();

        GUILayout.BeginHorizontal();
        DrawListPane();
        DrawDetailPane();
        GUILayout.EndHorizontal();
    }

    // ── Left pane: searchable list ───────────────────────────────────────────

    private static void DrawListPane()
    {
        GUILayout.BeginVertical(GUILayout.Width(GUIHelper.Scaled(220f)));

        // Search field
        _searchFilter = GUIHelper.TextField("T2DTex.Search", _searchFilter,
            GUILayout.ExpandWidth(true), GUIHelper.Height(22));

        _listScroll = GUILayout.BeginScrollView(_listScroll);

        bool anyT2D        = false;
        bool anyStandalone = false;
        bool inStandalone  = false;

        foreach (var entry in _entries)
        {
            if (!string.IsNullOrEmpty(_searchFilter) &&
                entry.CleanName.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            // Section header before first standalone entry
            if (!entry.IsT2D && !inStandalone)
            {
                inStandalone = true;
                GUIHelper.Space(6);
                GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
                GUILayout.Label("── Standalone ──", GUIHelper.LabelStyle);
                GUI.contentColor = Color.white;
            }

            if (entry.IsT2D) anyT2D = true;
            else anyStandalone = true;

            DrawListRow(entry);
        }

        if (!anyT2D && !anyStandalone)
        {
            GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
            GUILayout.Label(string.IsNullOrEmpty(_searchFilter)
                ? "No T2D textures tracked yet.\nLoad a save to populate."
                : "No matches.", GUIHelper.LabelStyle);
            GUI.contentColor = Color.white;
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static void DrawListRow(T2DSceneEntry entry)
    {
        bool sel = _hasSelection && _selected.RawName == entry.RawName;

        GUILayout.BeginHorizontal();

        // Selection highlight
        if (sel) GUI.contentColor = new Color(0.6f, 0.9f, 1f);

        if (GUILayout.Button(entry.CleanName, GUIHelper.LabelStyle, GUILayout.ExpandWidth(true)))
        {
            _selected       = entry;
            _hasSelection   = true;
            _selectedSprite = "";
        }

        GUI.contentColor = Color.white;

        // Badges: dimensions, loaded-sprite count, spritesheet flag
        GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
        GUILayout.Label($"{entry.Width}×{entry.Height}", GUIHelper.LabelStyle, GUIHelper.Width(70f));
        GUI.contentColor = Color.white;

        if (entry.HasSpritesheetOverride)
        {
            GUI.contentColor = new Color(0.4f, 1f, 0.4f);
            GUILayout.Label("[S]", GUIHelper.LabelStyle, GUIHelper.Width(22f));
            GUI.contentColor = Color.white;
        }
        else if (entry.LoadedIndividualCount > 0)
        {
            GUI.contentColor = new Color(0.4f, 0.85f, 1f);
            GUILayout.Label($"[{entry.LoadedIndividualCount}]", GUIHelper.LabelStyle, GUIHelper.Width(28f));
            GUI.contentColor = Color.white;
        }
        else
        {
            GUILayout.Space(GUIHelper.Scaled(28f));
        }

        GUILayout.EndHorizontal();
    }

    // ── Right pane: preview + sprite list + edit buttons ────────────────────

    private static void DrawDetailPane()
    {
        GUILayout.BeginVertical();

        if (!_hasSelection || _selected.NativeTexture == null)
        {
            GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
            GUILayout.Label("Select a texture on the left.", GUIHelper.LabelStyle);
            GUI.contentColor = Color.white;
            GUILayout.EndVertical();
            return;
        }

        var entry = _selected;

        // ── Atlas preview ────────────────────────────────────────────────────
        float previewH = GUIHelper.Scaled(180f);
        Rect previewRect = GUILayoutUtility.GetRect(0f, float.MaxValue, previewH, previewH);

        if (entry.NativeTexture != null)
        {
            GUI.DrawTexture(previewRect, entry.NativeTexture, ScaleMode.ScaleToFit, true);

            // UV highlight for selected sprite
            if (!string.IsNullOrEmpty(_selectedSprite))
                DrawSpriteHighlight(previewRect, entry, _selectedSprite);
        }

        GUIHelper.Space(4);

        // ── Info row ─────────────────────────────────────────────────────────
        GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"{entry.CleanName}  {entry.Width}×{entry.Height}  " +
                        $"{entry.SpriteNames.Count} sprite(s)", GUIHelper.LabelStyle);
        GUI.contentColor = Color.white;

        // ── Atlas-level buttons ──────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Edit Atlas", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            EditAtlas(entry);
        if (GUILayout.Button("Dump Atlas", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            DumpAtlasOnly(entry);
        if (GUILayout.Button("Dump All Sprites", GUIHelper.ButtonStyle, GUIHelper.Height(22)))
            DumpAllSprites(entry);
        GUILayout.EndHorizontal();

        GUIHelper.Space(4);

        // ── Sprite list ──────────────────────────────────────────────────────
        GUI.contentColor = new Color(0.55f, 0.55f, 0.55f);
        GUILayout.Label($"Sprites ({entry.SpriteNames.Count})", GUIHelper.LabelStyle);
        GUI.contentColor = Color.white;

        _spriteScroll = GUILayout.BeginScrollView(_spriteScroll, GUIHelper.Height(120f));
        foreach (var sname in entry.SpriteNames.OrderBy(s => s))
        {
            bool sprSel = sname == _selectedSprite;
            GUILayout.BeginHorizontal();

            if (sprSel) GUI.contentColor = new Color(1f, 1f, 0.5f);
            if (GUILayout.Button(sname, GUIHelper.LabelStyle, GUILayout.ExpandWidth(true)))
                _selectedSprite = sprSel ? "" : sname;
            if (sprSel) GUI.contentColor = Color.white;

            GUI.enabled = !string.IsNullOrEmpty(sname);
            if (GUILayout.Button("Edit", GUIHelper.ButtonStyle, GUIHelper.Width(42f), GUIHelper.Height(20)))
                EditSprite(entry, sname);
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    // ── Atlas UV highlight ───────────────────────────────────────────────────

    private static void DrawSpriteHighlight(Rect previewRect, T2DSceneEntry entry, string spriteName)
    {
        Sprite s = T2DLoader.FindSceneSprite(entry.CleanName, spriteName);
        if (s == null || s.texture == null) return;

        float tw = s.texture.width;
        float th = s.texture.height;
        if (tw <= 0 || th <= 0) return;

        Rect tr = s.textureRect;
        // Normalize to 0–1, flip Y (Unity textureRect is bottom-left; IMGUI is top-left)
        float nx = tr.x / tw;
        float ny = 1f - (tr.y + tr.height) / th;
        float nw = tr.width  / tw;
        float nh = tr.height / th;

        // Map normalized UV into the actual preview rect (ScaleToFit — compute letterboxed rect)
        Rect mapped = ScaleToFitRect(previewRect, entry.Width, entry.Height);

        Rect highlight = new Rect(
            mapped.x + nx * mapped.width,
            mapped.y + ny * mapped.height,
            nw * mapped.width,
            nh * mapped.height);

        if (_white == null)
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        GUI.color = new Color(1f, 1f, 0f, 0.35f);
        GUI.DrawTexture(highlight, _white);
        GUI.color = Color.white;
    }

    /// <summary>Computes the inner rect that ScaleMode.ScaleToFit would occupy.</summary>
    private static Rect ScaleToFitRect(Rect container, int texW, int texH)
    {
        if (texW <= 0 || texH <= 0) return container;
        float scaleW = container.width  / texW;
        float scaleH = container.height / texH;
        float scale  = Mathf.Min(scaleW, scaleH);
        float w = texW * scale;
        float h = texH * scale;
        return new Rect(
            container.x + (container.width  - w) * 0.5f,
            container.y + (container.height - h) * 0.5f,
            w, h);
    }

    // ── Edit / Dump actions ──────────────────────────────────────────────────

    private static void EditAtlas(T2DSceneEntry entry)
    {
        // Spritesheet slot: Sprites/T2D/{cleanName}/{cleanName}.png
        string loadDir  = Path.Combine(T2DLoader.AtlasLoadPath, entry.CleanName);
        string loadPath = Path.Combine(loadDir, entry.CleanName + ".png");

        if (!File.Exists(loadPath))
        {
            // Try to find an existing atlas dump first
            string dumpDir = Path.Combine(T2DDumper.DumpPath, entry.CleanName);
            string dumpPath = FindAtlasDump(dumpDir, entry);

            if (dumpPath == null)
            {
                // Force a dump now
                if (entry.NativeTexture is Texture2D t2d)
                    T2DDumper.DumpAtlasTexture(t2d);
                else
                    BlitAndDumpAtlas(entry, dumpDir);
                dumpPath = FindAtlasDump(dumpDir, entry);
            }

            if (dumpPath == null)
            {
                Plugin.Logger.LogError($"[T2DTex] Could not obtain atlas dump for '{entry.CleanName}'");
                return;
            }

            IOUtil.EnsureDirectoryExists(loadDir);
            File.Copy(dumpPath, loadPath, overwrite: true);
        }

        Process.Start(loadPath);
    }

    private static void EditSprite(T2DSceneEntry entry, string spriteName)
    {
        string loadDir  = Path.Combine(T2DLoader.AtlasLoadPath, entry.CleanName);
        string loadPath = Path.Combine(loadDir, spriteName + ".png");

        if (!File.Exists(loadPath))
        {
            string dumpPath = Path.Combine(T2DDumper.DumpPath, entry.CleanName, spriteName + ".png");
            if (!File.Exists(dumpPath))
            {
                Sprite s = T2DLoader.FindSceneSprite(entry.CleanName, spriteName);
                if (s == null)
                {
                    Plugin.Logger.LogError($"[T2DTex] Cannot find sprite '{spriteName}' to dump");
                    return;
                }
                T2DDumper.HandleDump(s);
            }

            if (!File.Exists(dumpPath))
            {
                Plugin.Logger.LogError($"[T2DTex] Dump failed for '{entry.CleanName}/{spriteName}'");
                return;
            }

            IOUtil.EnsureDirectoryExists(loadDir);
            File.Copy(dumpPath, loadPath, overwrite: true);
        }

        Process.Start(loadPath);
    }

    private static void DumpAtlasOnly(T2DSceneEntry entry)
    {
        if (entry.NativeTexture is Texture2D t2d)
            T2DDumper.DumpAtlasTexture(t2d);
        else
            BlitAndDumpAtlas(entry, Path.Combine(T2DDumper.DumpPath, entry.CleanName));
        Plugin.Logger.LogInfo($"[T2DTex] Dumped atlas '{entry.CleanName}'");
    }

    private static void DumpAllSprites(T2DSceneEntry entry)
    {
        int count = 0;
        foreach (var sname in entry.SpriteNames)
        {
            Sprite s = T2DLoader.FindSceneSprite(entry.CleanName, sname);
            if (s == null) continue;
            T2DDumper.HandleDump(s);
            count++;
        }
        Plugin.Logger.LogInfo($"[T2DTex] Dumped {count} sprite(s) from '{entry.CleanName}'");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// For RenderTexture-backed entries (SpriteLoader modified the atlas), blit to a
    /// readable Texture2D and write the atlas PNG to the dump directory.
    /// </summary>
    private static void BlitAndDumpAtlas(T2DSceneEntry entry, string dumpDir)
    {
        var rt = entry.NativeTexture as RenderTexture;
        if (rt == null) return;

        string path = Path.Combine(dumpDir, $"_atlas_{rt.width}x{rt.height}.png");
        if (File.Exists(path)) return;

        var prev     = RenderTexture.active;
        Texture2D t  = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        RenderTexture.active = prev;

        IOUtil.EnsureDirectoryExists(dumpDir);
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.Destroy(t);
    }

    private static string FindAtlasDump(string dumpDir, T2DSceneEntry entry)
    {
        if (!Directory.Exists(dumpDir)) return null;
        // Prefer exact-dimension match; fall back to any atlas PNG
        string preferred = Path.Combine(dumpDir, $"_atlas_{entry.Width}x{entry.Height}.png");
        if (File.Exists(preferred)) return preferred;
        foreach (var f in Directory.GetFiles(dumpDir, "_atlas_*.png"))
            return f;
        return null;
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private static void MaybeRefresh()
    {
        if (Time.realtimeSinceStartup - _lastRefresh < RefreshInterval) return;
        _lastRefresh = Time.realtimeSinceStartup;
        _entries     = T2DLoader.GetSceneTextureEntries();

        // If selected entry is stale, clear it
        if (_hasSelection && !_entries.Any(e => e.RawName == _selected.RawName))
        {
            _hasSelection   = false;
            _selectedSprite = "";
        }
    }
}
