using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Patchwork.Packs;
using Patchwork.Util;

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
    private static string  _profileNameInput = "";

    // Staged list — null means no pending changes, non-null means user has unsaved edits.
    private static List<PackInfo> _staged;
    private static bool HasChanges => _staged != null;

    // Set of pack paths whose condition editor is currently expanded.
    private static readonly HashSet<string> _conditionsOpen = new();

    // ID of the currently open inline type-dropdown (null = none open).
    private static string _openDropdownId = null;

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
        GUIHelper.Space(4);
        DrawProfilesSection();
        GUIHelper.Space(4);
        DrawConflictsSection();

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
                if (pack.IsEnabled)
                    UnityEngine.GUI.contentColor = new Color(0.3f, 1f, 0.3f);
                bool clicked = GUILayout.Button(pack.IsEnabled ? "ON" : "OFF", GUIHelper.ButtonStyle, GUIHelper.Width(40));
                UnityEngine.GUI.contentColor = Color.white;
                if (clicked)
                {
                    EnsureStaged();
                    _staged[i].IsEnabled = !pack.IsEnabled;
                }

                // Name + source badge
                string badge = pack.IsLocal ? " [local]" : " [pack]";
                GUILayout.Label(pack.Name + badge, GUIHelper.LabelStyle);

                // Conflict badge — show when this pack has shadowed assets
                int shadowed = ConflictTracker.ShadowedCount(pack.Path);
                if (shadowed > 0)
                {
                    UnityEngine.GUI.contentColor = new Color(1f, 0.75f, 0.2f);
                    GUILayout.Label($"⚠ {shadowed}", GUIHelper.LabelStyle);
                    UnityEngine.GUI.contentColor = Color.white;
                }

                // Conditions toggle button (works on live list, not staged)
                var livePack = PackManager.AllPacks.FirstOrDefault(p =>
                    string.Equals(p.Path, pack.Path, System.StringComparison.OrdinalIgnoreCase));
                if (livePack != null)
                {
                    bool condOpen = _conditionsOpen.Contains(pack.Path);
                    if (condOpen || livePack.HasConditions)
                        UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                    string condLabel = livePack.HasConditions
                        ? $"[{livePack.Conditions.Count} cond]"
                        : "[+ cond]";
                    if (GUILayout.Button(condLabel, GUIHelper.ButtonStyle, GUIHelper.Width(74)))
                    {
                        if (condOpen) _conditionsOpen.Remove(pack.Path);
                        else          _conditionsOpen.Add(pack.Path);
                    }
                    UnityEngine.GUI.contentColor = Color.white;
                }

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

            // ── Asset footprint ───────────────────────────────────
            string footprint = pack.Stats.Badge;
            if (footprint != null)
            {
                // Show scanned file counts (e.g. "12 sprites  3 sheets  5 sfx")
                GUILayout.Label("  " + footprint, GUIHelper.LabelStyle);
            }
            else
            {
                // Fall back to directory-presence tags when stats haven't been scanned yet
                var types = new List<string>();
                if (Directory.Exists(Path.Combine(pack.Path, "Sprites")))      types.Add("sprites");
                if (Directory.Exists(Path.Combine(pack.Path, "Spritesheets"))) types.Add("sheets");
                if (Directory.Exists(Path.Combine(pack.Path, "Sounds")))       types.Add("audio");
                if (Directory.Exists(Path.Combine(pack.Path, "Videos")))       types.Add("video");
                if (Directory.Exists(Path.Combine(pack.Path, "Text")))         types.Add("text");
                if (types.Count > 0)
                    GUILayout.Label("  [" + string.Join(", ", types) + "]", GUIHelper.LabelStyle);
            }

            // ── Inline condition editor ───────────────────────────
            var livePackForEditor = PackManager.AllPacks.FirstOrDefault(p =>
                string.Equals(p.Path, pack.Path, System.StringComparison.OrdinalIgnoreCase));
            if (livePackForEditor != null && _conditionsOpen.Contains(pack.Path))
                DrawConditionEditor(livePackForEditor);
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

    // ================================================================
    //  Per-pack condition editor  (works on live list, saves immediately)
    // ================================================================

    private static void DrawConditionEditor(PackInfo pack)
    {
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
        {
            // ── Reload trigger ─────────────────────────────────────
            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("Reload:", GUIHelper.LabelStyle, GUIHelper.Width(50));

                bool sceneActive = pack.ReloadTrigger == ReloadTrigger.OnSceneTransition;
                if (sceneActive) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                if (GUILayout.Button("on scene", GUIHelper.ButtonStyle, GUIHelper.Width(72)))
                { pack.ReloadTrigger = ReloadTrigger.OnSceneTransition; PackManager.SaveConditions(); }
                UnityEngine.GUI.contentColor = Color.white;

                bool hotActive = pack.ReloadTrigger == ReloadTrigger.HotReload;
                if (hotActive) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                if (GUILayout.Button("hot reload", GUIHelper.ButtonStyle, GUIHelper.Width(80)))
                { pack.ReloadTrigger = ReloadTrigger.HotReload; PackManager.SaveConditions(); }
                UnityEngine.GUI.contentColor = Color.white;

                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            GUIHelper.Space(2);

            // ── Condition rows (Factorio-style DNF) ───────────────
            int removeAt = -1;
            for (int ci = 0; ci < pack.Conditions.Count; ci++)
            {
                var cond   = pack.Conditions[ci];
                string dropId = $"Patchwork.CondType.{pack.Path}.{ci}";
                bool dropOpen = _openDropdownId == dropId;

                // Condition row
                GUILayout.BeginHorizontal();
                {
                    // Type dropdown button
                    if (dropOpen) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                    if (GUILayout.Button(cond.TypeLabel, GUIHelper.ButtonStyle, GUIHelper.Width(62)))
                        _openDropdownId = dropOpen ? null : dropId;
                    UnityEngine.GUI.contentColor = Color.white;

                    // Negate toggle  (== / !=)
                    string negLabel = cond.Negate ? "!=" : "==";
                    if (GUILayout.Button(negLabel, GUIHelper.ButtonStyle, GUIHelper.Width(30)))
                    { cond.Negate = !cond.Negate; PackManager.SaveConditions(); }

                    // Value text field
                    string newVal = GUIHelper.TextField(
                        $"Patchwork.Cond.{pack.Path}.{ci}",
                        cond.Value,
                        GUIHelper.Width(160));
                    if (newVal != cond.Value) { cond.Value = newVal; PackManager.SaveConditions(); }

                    // Remove button
                    UnityEngine.GUI.contentColor = new Color(1f, 0.45f, 0.45f);
                    if (GUILayout.Button("×", GUIHelper.ButtonStyle, GUIHelper.Width(22)))
                        removeAt = ci;
                    UnityEngine.GUI.contentColor = Color.white;
                }
                GUILayout.EndHorizontal();

                // Inline type dropdown — expands below the row when open
                if (dropOpen)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
                    foreach (ConditionType t in System.Enum.GetValues(typeof(ConditionType)))
                    {
                        string tLabel = t switch
                        {
                            ConditionType.Scene         => "scene",
                            ConditionType.SceneContains => "scene~",
                            ConditionType.PackActive    => "pack",
                            _                           => t.ToString()
                        };
                        if (cond.Type == t) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                        if (GUILayout.Button(tLabel, GUIHelper.ButtonStyle))
                        {
                            cond.Type      = t;
                            _openDropdownId = null;
                            PackManager.SaveConditions();
                        }
                        UnityEngine.GUI.contentColor = Color.white;
                    }
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }

                // AND / OR join toggle between consecutive condition rows
                if (ci < pack.Conditions.Count - 1)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);

                    bool joinIsOr = cond.JoinNext == LogicJoin.Or;
                    if (joinIsOr) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                    if (GUILayout.Button("OR", GUIHelper.ButtonStyle, GUIHelper.Width(34)))
                    { cond.JoinNext = LogicJoin.Or; PackManager.SaveConditions(); }
                    UnityEngine.GUI.contentColor = Color.white;

                    bool joinIsAnd = cond.JoinNext == LogicJoin.And;
                    if (joinIsAnd) UnityEngine.GUI.contentColor = new Color(0.45f, 0.85f, 1f);
                    if (GUILayout.Button("AND", GUIHelper.ButtonStyle, GUIHelper.Width(38)))
                    { cond.JoinNext = LogicJoin.And; PackManager.SaveConditions(); }
                    UnityEngine.GUI.contentColor = Color.white;

                    GUILayout.EndHorizontal();
                }
            }

            if (removeAt >= 0)
            { pack.Conditions.RemoveAt(removeAt); PackManager.SaveConditions(); }

            GUIHelper.Space(2);

            // ── Bottom bar: Add condition + Done ──────────────────
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add condition", GUIHelper.ButtonStyle))
            { pack.Conditions.Add(new PackCondition()); PackManager.SaveConditions(); }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Done", GUIHelper.ButtonStyle, GUIHelper.Width(50)))
            {
                _conditionsOpen.Remove(pack.Path);
                _openDropdownId = null;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    // ================================================================
    //  Profiles section
    // ================================================================

    private static void DrawProfilesSection()
    {
        GUILayout.Label("── Profiles ──────────────────────────────", GUIHelper.LabelStyle);

        // Existing profiles
        var names = PackManager.GetProfileNames();
        if (names.Length > 0)
        {
            GUILayout.BeginHorizontal();
            foreach (var name in names)
            {
                if (GUILayout.Button(name, GUIHelper.ButtonStyle))
                {
                    var staged = PackManager.StageProfile(name);
                    if (staged != null) _staged = staged;
                }
                if (GUILayout.Button("✕", GUIHelper.ButtonStyle, GUIHelper.Width(24)))
                    PackManager.DeleteProfile(name);
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("  No saved profiles.", GUIHelper.LabelStyle);
        }

        // Save current as new profile
        GUILayout.BeginHorizontal();
        GUILayout.Label("Save as:", GUIHelper.LabelStyle, GUIHelper.Width(58));
        _profileNameInput = GUIHelper.TextField("PackManagerProfileName", _profileNameInput, GUIHelper.Width(140));
        UnityEngine.GUI.enabled = !string.IsNullOrWhiteSpace(_profileNameInput);
        if (GUILayout.Button("Save", GUIHelper.ButtonStyle, GUIHelper.Width(50)))
        {
            PackManager.SaveProfile(_profileNameInput.Trim());
            _profileNameInput = "";
        }
        UnityEngine.GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    // ================================================================
    //  Conflicts section
    // ================================================================

    private static bool _conflictsFoldout;

    private static void DrawConflictsSection()
    {
        int total = ConflictTracker.Total;
        string header = total == 0
            ? "── Conflicts: none ───────────────────────"
            : $"── Conflicts: {total} ──────────────────────────";

        if (GUILayout.Button(header, GUIHelper.LabelStyle))
            _conflictsFoldout = !_conflictsFoldout;

        if (!_conflictsFoldout || total == 0) return;

        foreach (var e in ConflictTracker.All)
        {
            string winner = PackManager.GetPackName(e.WinnerPack);
            string loser  = PackManager.GetPackName(e.LoserPack);
            GUILayout.Label($"  [{e.Type}] {e.Key}\n    {winner}  >  {loser}",
                GUIHelper.LabelStyle);
        }
    }
}
