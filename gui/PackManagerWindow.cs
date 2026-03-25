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

    // ── Condition-editor layout constants ─────────────────────────────
    // Conditions sit 2 indent columns to the right.
    // OR buttons live in column 1 (outer), AND buttons in column 2 (inner).
    private const  float kOrColW         = 38f;
    private const  float kAndColW        = 38f;
    private static readonly Color kJoinHl = new Color(0.45f, 0.85f, 1f); // highlighted join button

    // 1×1 texture used to draw the AND-group bracket line.
    private static GUIStyle _bracketStyle;
    private static GUIStyle BracketStyle
    {
        get
        {
            if (_bracketStyle != null) return _bracketStyle;
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.5f, 0.72f, 0.9f, 0.85f));
            tex.Apply();
            _bracketStyle = new GUIStyle(GUIStyle.none);
            _bracketStyle.normal.background = tex;
            return _bracketStyle;
        }
    }

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

    // ================================================================
    //  Condition-editor helpers
    // ================================================================

    /// <summary>Groups condition indices into AND-clauses separated by OR joins.</summary>
    private static List<List<int>> BuildClauses(List<PackCondition> conds)
    {
        var clauses = new List<List<int>>();
        if (conds.Count == 0) return clauses;
        var cur = new List<int> { 0 };
        for (int i = 0; i < conds.Count - 1; i++)
        {
            if (conds[i].JoinNext == LogicJoin.And)
                cur.Add(i + 1);
            else
            { clauses.Add(cur); cur = new List<int> { i + 1 }; }
        }
        clauses.Add(cur);
        return clauses;
    }

    /// <summary>Renders the type/negate/value/remove widgets for condition <paramref name="ci"/>.</summary>
    private static void DrawConditionContent(PackInfo pack, int ci, ref int removeAt)
    {
        var    cond   = pack.Conditions[ci];
        string dropId = $"Patchwork.CondType.{pack.Path}.{ci}";
        bool   isOpen = _openDropdownId == dropId;

        if (isOpen) UnityEngine.GUI.contentColor = kJoinHl;
        if (GUILayout.Button(cond.TypeLabel, GUIHelper.ButtonStyle, GUIHelper.Width(62)))
            _openDropdownId = isOpen ? null : dropId;
        UnityEngine.GUI.contentColor = Color.white;

        string negLabel = cond.Negate ? "!=" : "==";
        if (GUILayout.Button(negLabel, GUIHelper.ButtonStyle, GUIHelper.Width(30)))
        { cond.Negate = !cond.Negate; PackManager.SaveConditions(); }

        string newVal = GUIHelper.TextField(
            $"Patchwork.Cond.{pack.Path}.{ci}", cond.Value, GUIHelper.Width(146));
        if (newVal != cond.Value) { cond.Value = newVal; PackManager.SaveConditions(); }

        UnityEngine.GUI.contentColor = new Color(1f, 0.45f, 0.45f);
        if (GUILayout.Button("×", GUIHelper.ButtonStyle, GUIHelper.Width(22)))
            removeAt = ci;
        UnityEngine.GUI.contentColor = Color.white;
    }

    /// <summary>Renders the inline type dropdown for condition <paramref name="ci"/> if it is open,
    /// indented by <paramref name="indent"/> pixels.</summary>
    private static void DrawDropdownIfOpen(PackInfo pack, int ci, float indent = 0f)
    {
        string dropId = $"Patchwork.CondType.{pack.Path}.{ci}";
        if (_openDropdownId != dropId) return;
        var cond = pack.Conditions[ci];

        GUILayout.BeginHorizontal();
        if (indent > 0) GUILayout.Space(indent);
        GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
        foreach (ConditionType t in System.Enum.GetValues(typeof(ConditionType)))
        {
            string tLabel = PackCondition.LabelFor(t);
            if (cond.Type == t) UnityEngine.GUI.contentColor = kJoinHl;
            if (GUILayout.Button(tLabel, GUIHelper.ButtonStyle))
            { cond.Type = t; _openDropdownId = null; PackManager.SaveConditions(); }
            UnityEngine.GUI.contentColor = Color.white;
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    // ================================================================
    //  Condition editor
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

            // ── Condition rows ─────────────────────────────────────
            // Two-column indent before each condition:
            //   Column 1 (kOrColW)  — "or"  button when the preceding join is OR
            //   Column 2 (kAndColW) — "and" button when the preceding join is AND
            // An AND-group (two or more consecutive AND-joined conditions) is wrapped
            // in a horizontal that prefixes a thin bracket bar and shifts the AND
            // buttons into column 2.  Clicking a join button toggles it (and/or),
            // which moves it between columns on the next frame.
            //
            //   [space ][space ][condition A]         ← first, no join button
            //   [  or  ][space ][condition B]         ← B's entry join is OR
            //   [  or  ][bracket][space ][condition C] ─┐ C & D share an AND-clause
            //            [      ][  and ][condition D] ─┘ (bracket spans both rows)

            var clauses  = BuildClauses(pack.Conditions);
            int removeAt = -1;

            for (int clauseIdx = 0; clauseIdx < clauses.Count; clauseIdx++)
            {
                var  clause       = clauses[clauseIdx];
                bool isFirst      = clauseIdx == 0;
                bool isGroup      = clause.Count > 1;
                int  entryJoinIdx = isFirst ? -1 : clause[0] - 1;

                GUILayout.BeginHorizontal();
                {
                    // ── Column 1: OR button (inter-clause join) ───
                    if (!isFirst)
                    {
                        UnityEngine.GUI.contentColor = kJoinHl; // the inter-clause join is always Or
                        if (GUILayout.Button("or", GUIHelper.ButtonStyle, GUIHelper.Width(kOrColW)))
                        { pack.Conditions[entryJoinIdx].JoinNext = LogicJoin.And; PackManager.SaveConditions(); }
                        UnityEngine.GUI.contentColor = Color.white;
                    }
                    else
                    {
                        GUILayout.Space(kOrColW);
                    }

                    // ── Column 2 + conditions ─────────────────────
                    if (isGroup)
                    {
                        // Bracket: thin coloured bar spanning the full AND-group height
                        var prevBg = UnityEngine.GUI.backgroundColor;
                        UnityEngine.GUI.backgroundColor = new Color(0.45f, 0.7f, 0.9f, 0.85f);
                        GUILayout.Box("", BracketStyle,
                            GUILayout.Width(3), GUILayout.ExpandHeight(true));
                        UnityEngine.GUI.backgroundColor = prevBg;
                        GUILayout.Space(2);

                        GUILayout.BeginVertical();
                        for (int j = 0; j < clause.Count; j++)
                        {
                            int  ci           = clause[j];
                            bool firstInGroup = j == 0;
                            int  intraJoin    = firstInGroup ? -1 : ci - 1;

                            GUILayout.BeginHorizontal();
                            {
                                if (!firstInGroup)
                                {
                                    UnityEngine.GUI.contentColor = kJoinHl; // always And within the group
                                    if (GUILayout.Button("and", GUIHelper.ButtonStyle, GUIHelper.Width(kAndColW)))
                                    { pack.Conditions[intraJoin].JoinNext = LogicJoin.Or; PackManager.SaveConditions(); }
                                    UnityEngine.GUI.contentColor = Color.white;
                                }
                                else
                                {
                                    GUILayout.Space(kAndColW);
                                }
                                DrawConditionContent(pack, ci, ref removeAt);
                            }
                            GUILayout.EndHorizontal();
                            DrawDropdownIfOpen(pack, ci, kAndColW);
                        }
                        GUILayout.EndVertical();
                    }
                    else
                    {
                        // Single-condition clause — just indent column 2 and show content
                        GUILayout.Space(kAndColW);
                        DrawConditionContent(pack, clause[0], ref removeAt);
                    }
                }
                GUILayout.EndHorizontal();

                if (!isGroup)
                    DrawDropdownIfOpen(pack, clause[0], kOrColW + kAndColW);
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
