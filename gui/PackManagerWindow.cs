using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Patchwork.Packs;

namespace Patchwork.GUI;

/// <summary>
/// Ingame resource pack manager window.
/// Changes are staged locally; nothing takes effect until Apply is clicked.
/// </summary>
public static class PackManagerWindow
{
    private const float WindowWidth  = 460;
    private const float WindowHeight = 520;
    private const float LeftMargin   = 20;
    private const float TopMargin    = 60;

    private static Rect    _windowRect;
    private static bool    _initialized;
    private static Vector2 _scroll;

    // Staged list — null means no pending changes, non-null means user has unsaved edits.
    private static List<PackInfo> _staged;
    private static bool HasChanges => _staged != null;

    // ================================================================
    //  Public entry point
    // ================================================================

    public static void Draw()
    {
        if (!_initialized || _windowRect.width < 1)
        {
            _windowRect = GUIHelper.ScaledRect(LeftMargin, TopMargin, WindowWidth, WindowHeight);
            _initialized = true;
        }

        _windowRect = GUILayout.Window(
            69761,
            _windowRect,
            DrawWindow,
            "Patchwork — Resource Packs",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    // ================================================================
    //  Window content
    // ================================================================

    private static void DrawWindow(int _)
    {
        // The list we're rendering — staged changes if pending, live list otherwise.
        var list = _staged ?? PackManager.AllPacks.ToList();

        DrawToolbar(list);
        GUIHelper.Space(4);
        DrawPackList(list);
        GUIHelper.Space(4);
        DrawFooter(list);

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    private static void DrawToolbar(List<PackInfo> list)
    {
        GUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Rescan", GUIHelper.ButtonStyle, GUIHelper.Width(80)))
            {
                PackManager.Rescan();
                _staged = null;
            }

            GUILayout.FlexibleSpace();

            UnityEngine.GUI.enabled = HasChanges;
            if (GUILayout.Button("Discard", GUIHelper.ButtonStyle, GUIHelper.Width(72)))
                _staged = null;

            if (GUILayout.Button("Apply", GUIHelper.ButtonStyle, GUIHelper.Width(66)))
            {
                PackManager.Apply(_staged);
                _staged = null;
            }
            UnityEngine.GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawPackList(List<PackInfo> list)
    {
        _scroll = GUILayout.BeginScrollView(_scroll);
        {
            if (list.Count == 0)
            {
                GUILayout.Label(
                    "No packs found.\n" +
                    "Drop packs into Patchwork/Packs/ or install via Thunderstore.",
                    GUIHelper.LabelStyle);
            }

            for (int i = 0; i < list.Count; i++)
                DrawPackRow(list, i);
        }
        GUILayout.EndScrollView();
    }

    private static void DrawPackRow(List<PackInfo> list, int i)
    {
        var pack = list[i];

        GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
        {
            // ── Main row ─────────────────────────────────────────
            GUILayout.BeginHorizontal();
            {
                // Enable / disable button
                if (GUILayout.Button(pack.IsEnabled ? "ON" : "OFF", GUIHelper.ButtonStyle, GUIHelper.Width(40)))
                {
                    EnsureStaged();
                    _staged[i].IsEnabled = !pack.IsEnabled;
                }

                // Name + source badge
                string badge = pack.IsLocal ? " [local]" : " [pack]";
                GUILayout.Label(pack.Name + badge, GUIHelper.LabelStyle);
                GUILayout.FlexibleSpace();

                // Priority arrows
                UnityEngine.GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUIHelper.ButtonStyle, GUIHelper.Width(26)))
                {
                    EnsureStaged();
                    (_staged[i - 1], _staged[i]) = (_staged[i], _staged[i - 1]);
                }

                UnityEngine.GUI.enabled = i < list.Count - 1;
                if (GUILayout.Button("▼", GUIHelper.ButtonStyle, GUIHelper.Width(26)))
                {
                    EnsureStaged();
                    (_staged[i + 1], _staged[i]) = (_staged[i], _staged[i + 1]);
                }

                UnityEngine.GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            // ── Secondary info line ───────────────────────────────
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(pack.Author))  meta.Add($"by {pack.Author}");
            if (!string.IsNullOrEmpty(pack.Version)) meta.Add($"v{pack.Version}");
            if (!string.IsNullOrEmpty(pack.Description))
            {
                string desc = pack.Description.Length > 55
                    ? pack.Description.Substring(0, 52) + "…"
                    : pack.Description;
                meta.Add(desc);
            }

            if (meta.Count > 0)
                GUILayout.Label("  " + string.Join("   ", meta), GUIHelper.LabelStyle);

            // ── Asset type tags ───────────────────────────────────
            var types = new List<string>();
            if (Directory.Exists(Path.Combine(pack.Path, "Sprites")))     types.Add("sprites");
            if (Directory.Exists(Path.Combine(pack.Path, "Spritesheets"))) types.Add("sheets");
            if (Directory.Exists(Path.Combine(pack.Path, "Sounds")))      types.Add("audio");
            if (Directory.Exists(Path.Combine(pack.Path, "Videos")))      types.Add("video");
            if (Directory.Exists(Path.Combine(pack.Path, "Text")))        types.Add("text");

            if (types.Count > 0)
                GUILayout.Label("  [" + string.Join(", ", types) + "]", GUIHelper.LabelStyle);
        }
        GUILayout.EndVertical();

        GUIHelper.Space(2);
    }

    private static void DrawFooter(List<PackInfo> list)
    {
        int active = list.Count(p => p.IsEnabled);
        string status = HasChanges
            ? $"{active}/{list.Count} active  •  unsaved changes"
            : $"{active}/{list.Count} active";
        GUILayout.Label(status, GUIHelper.LabelStyle);
    }

    // ================================================================
    //  Staging helpers
    // ================================================================

    /// <summary>Creates a deep copy of the current live list for staged editing.</summary>
    private static void EnsureStaged()
    {
        _staged ??= PackManager.AllPacks.Select(p => p.Clone()).ToList();
    }
}
