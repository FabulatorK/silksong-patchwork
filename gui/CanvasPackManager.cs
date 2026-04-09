using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Patchwork.Packs;
using Patchwork.Util;

namespace Patchwork.GUI;

/// <summary>
/// Canvas-based Pack Manager — retained-mode replacement for the IMGUI
/// <see cref="PackManagerWindow"/>. Built on <see cref="FabricUI"/>.
/// Changes are staged locally; nothing applies until Apply is clicked.
/// </summary>
public static class CanvasPackManager
{
    // ── Layout constants (base 1080p) ──────────────────────────────────
    private const float WinW     = 460f;
    private const float WinH     = 520f;
    private const float StripW   = 12f;
    private const float Pad      = 8f;
    private const float PadRight = 10f;
    private const float RowH     = 28f;

    // ── Root objects ───────────────────────────────────────────────────
    private static GameObject    _root;
    private static RectTransform _rootRT;
    private static bool          _built;

    // ── Staging (mirrors IMGUI PackManagerWindow) ──────────────────────
    private static List<PackInfo> _staged;
    private static bool HasChanges => _staged != null;

    // ── Dynamic content references ────────────────────────────────────
    private static Transform _packListContent;
    private static Text      _titleText;
    private static Text      _statusText;
    private static Text      _changesText;
    private static GameObject _discardBtn;
    private static GameObject _applyBtn;
    private static Image      _applyBg;
    private static GameObject _profilesContainer;
    private static Transform  _profileButtonsContent;
    private static InputField _profileNameInput;
    private static GameObject _conflictsContainer;
    private static Transform  _conflictsContent;
    private static Text       _conflictsHeader;
    private static bool       _conflictsFoldout;
    private static RawImage   _stripImage;

    // ── Condition editor state ────────────────────────────────────────
    private static readonly HashSet<string> _conditionsOpen = new();

    // ── Cassette strip texture cache ──────────────────────────────────
    private static int _lastStripH;

    // ================================================================
    //  Public API
    // ================================================================

    public static bool IsVisible => _root != null && _root.activeSelf;

    public static void Show()
    {
        if (!_built || _root == null) Build();
        _root.SetActive(true);
        Rebuild();
    }

    public static void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }

    public static void Toggle()
    {
        if (IsVisible) Hide(); else Show();
    }

    // ================================================================
    //  Build — one-time construction of the full widget tree
    // ================================================================

    private static void Build()
    {
        var canvas = FabricUI.RootCanvas;

        float w = FabricUI.S(WinW);
        float h = FabricUI.S(WinH);

        // ── Root panel ────────────────────────────────────────────────
        _root = new GameObject("CanvasPackManager", typeof(RectTransform));
        _root.transform.SetParent(canvas.transform, false);
        _rootRT = _root.GetComponent<RectTransform>();
        _rootRT.anchorMin = new Vector2(0, 1);
        _rootRT.anchorMax = new Vector2(0, 1);
        _rootRT.pivot     = new Vector2(0, 1);
        _rootRT.sizeDelta = new Vector2(w, h);
        _rootRT.anchoredPosition = new Vector2(FabricUI.S(20f), -FabricUI.S(60f));

        var bg = _root.AddComponent<Image>();
        bg.sprite = FabricUI.CreateRoundedSprite(FabricUI.SI(6), FabricUI.SI(32));
        bg.type   = Image.Type.Sliced;
        bg.color  = GUIHelper.ColSurface;
        bg.raycastTarget = true;

        // Drag handler
        _root.AddComponent<CanvasPanelDragger>();

        // ── Cassette strip (left edge) ────────────────────────────────
        BuildStrip();

        // ── Main content column ───────────────────────────────────────
        float indent = FabricUI.S(StripW + Pad);
        float right  = FabricUI.S(PadRight);
        float topBot = FabricUI.S(Pad);

        var content = FabricUI.CreateVerticalGroup(_root.transform, "Content",
            spacing: FabricUI.S(4),
            padding: new RectOffset(
                Mathf.RoundToInt(indent),
                Mathf.RoundToInt(right),
                Mathf.RoundToInt(topBot),
                Mathf.RoundToInt(topBot)));
        FabricUI.SetStretch(content.GetComponent<RectTransform>());

        // ── Title ─────────────────────────────────────────────────────
        BuildTitle(content.transform);

        // ── Toolbar (Rescan + Close) ──────────────────────────────────
        BuildToolbar(content.transform);

        // ── Pack list scroll area ─────────────────────────────────────
        BuildPackListContainer(content.transform);

        // ── Profiles section ──────────────────────────────────────────
        BuildProfilesSection(content.transform);

        // ── Conflicts section ─────────────────────────────────────────
        BuildConflictsSection(content.transform);

        // ── Action bar ────────────────────────────────────────────────
        BuildActionBar(content.transform);

        // ── Footer ────────────────────────────────────────────────────
        BuildFooter(content.transform);

        _root.SetActive(false);
        _built = true;
    }

    // ================================================================
    //  Shell sub-builders
    // ================================================================

    private static void BuildStrip()
    {
        var stripGO = new GameObject("CassetteStrip", typeof(RectTransform));
        stripGO.transform.SetParent(_root.transform, false);
        _stripImage = stripGO.AddComponent<RawImage>();
        _stripImage.raycastTarget = false;

        var rt = stripGO.GetComponent<RectTransform>();
        FabricUI.SetStretchLeft(rt, FabricUI.S(StripW));

        RefreshStripTexture();
    }

    private static void RefreshStripTexture()
    {
        int texW = Mathf.Max(FabricUI.SI(StripW), 2);
        int texH = Mathf.Max(Mathf.RoundToInt(_rootRT.sizeDelta.y), 2);
        if (texH == _lastStripH && _stripImage.texture != null) return;
        _stripImage.texture = GUIHelper.GetVerticalStripTex(texW, texH);
        _lastStripH = texH;
    }

    private static void BuildTitle(Transform parent)
    {
        var titleGO = FabricUI.CreateText(parent, "Title", "Patchwork \u2014 Resource Packs",
            baseFontSize: 14, color: GUIHelper.ColBone, alignment: TextAnchor.MiddleCenter);
        var txt = titleGO.GetComponent<Text>();
        txt.fontStyle = FontStyle.Bold;
        _titleText = txt;
        FabricUI.AddLayout(titleGO, preferredHeight: FabricUI.S(22));
    }

    private static void BuildToolbar(Transform parent)
    {
        var row = FabricUI.CreateHorizontalGroup(parent, "Toolbar", spacing: 4);
        FabricUI.AddLayout(row, preferredHeight: FabricUI.S(RowH));

        // Rescan
        var rescan = FabricUI.CreateButton(row.transform, "Rescan", "Rescan", () =>
        {
            PackManager.Rescan();
            _staged = null;
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(rescan, preferredWidth: FabricUI.S(72), preferredHeight: FabricUI.S(24));

        // Spacer
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(row.transform, false);
        FabricUI.AddLayout(spacer, flexibleWidth: 1);

        // Close
        var close = FabricUI.CreateButton(row.transform, "Close", "\u00d7", () =>
        {
            Plugin.ShowPackManager = false;
            Hide();
        }, bgColor: new Color(GUIHelper.ColDanger.r * 0.55f,
            GUIHelper.ColDanger.g * 0.55f, GUIHelper.ColDanger.b * 0.55f));
        FabricUI.AddLayout(close, preferredWidth: FabricUI.S(26), preferredHeight: FabricUI.S(22));
    }

    // ── Pack list ──────────────────────────────────────────────────────

    private static void BuildPackListContainer(Transform parent)
    {
        var scrollGO = FabricUI.CreateScrollView(parent, "PackList");
        FabricUI.AddLayout(scrollGO, flexibleHeight: 1, preferredHeight: FabricUI.S(240));
        _packListContent = scrollGO.GetComponent<ScrollRect>().content;
    }

    // ── Profiles ───────────────────────────────────────────────────────

    private static void BuildProfilesSection(Transform parent)
    {
        // Separator
        FabricUI.CreateSeparator(parent);

        _profilesContainer = FabricUI.CreateVerticalGroup(parent, "Profiles", spacing: 3);

        // Header
        var header = FabricUI.CreateText(_profilesContainer.transform, "ProfilesHeader",
            "Profiles", baseFontSize: 13, color: Color.white);
        header.GetComponent<Text>().fontStyle = FontStyle.Bold;
        FabricUI.AddLayout(header, preferredHeight: FabricUI.S(20));

        // Profile buttons row (rebuilt dynamically)
        var btnRow = FabricUI.CreateHorizontalGroup(_profilesContainer.transform, "ProfileButtons", spacing: 4);
        FabricUI.AddLayout(btnRow, preferredHeight: FabricUI.S(24));
        _profileButtonsContent = btnRow.transform;

        // Save-as row
        var saveRow = FabricUI.CreateHorizontalGroup(_profilesContainer.transform, "SaveRow", spacing: 4);
        FabricUI.AddLayout(saveRow, preferredHeight: FabricUI.S(24));

        var saveLabel = FabricUI.CreateText(saveRow.transform, "SaveLabel", "Save as:",
            baseFontSize: 12, color: GUIHelper.ColMuted);
        FabricUI.AddLayout(saveLabel, preferredWidth: FabricUI.S(55));

        var inputGO = FabricUI.CreateInputField(saveRow.transform, "ProfileNameInput",
            placeholder: "profile name");
        FabricUI.AddLayout(inputGO, preferredWidth: FabricUI.S(140), preferredHeight: FabricUI.S(22));
        _profileNameInput = inputGO.GetComponent<InputField>();

        var saveBtn = FabricUI.CreateButton(saveRow.transform, "SaveBtn", "Save", () =>
        {
            string name = _profileNameInput.text?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                PackManager.SaveProfile(name);
                _profileNameInput.text = "";
                Rebuild();
            }
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(saveBtn, preferredWidth: FabricUI.S(50), preferredHeight: FabricUI.S(22));
    }

    // ── Conflicts ──────────────────────────────────────────────────────

    private static void BuildConflictsSection(Transform parent)
    {
        FabricUI.CreateSeparator(parent);

        _conflictsContainer = FabricUI.CreateVerticalGroup(parent, "Conflicts", spacing: 2);

        // Header (clickable foldout)
        var headerBtn = FabricUI.CreateButton(_conflictsContainer.transform, "ConflictsHeader",
            "Conflicts: none", () =>
            {
                _conflictsFoldout = !_conflictsFoldout;
                RefreshConflicts();
            }, bgColor: Color.clear, textColor: Color.white);
        var headerTxt = headerBtn.GetComponentInChildren<Text>();
        headerTxt.fontStyle = FontStyle.Bold;
        headerTxt.alignment = TextAnchor.MiddleLeft;
        _conflictsHeader = headerTxt;
        FabricUI.AddLayout(headerBtn, preferredHeight: FabricUI.S(20));

        // Conflicts detail container (shown on foldout)
        var detailGO = FabricUI.CreateVerticalGroup(_conflictsContainer.transform, "ConflictDetail", spacing: 1);
        _conflictsContent = detailGO.transform;
        detailGO.SetActive(false);
    }

    // ── Action bar ─────────────────────────────────────────────────────

    private static void BuildActionBar(Transform parent)
    {
        FabricUI.CreateSeparator(parent);

        var bar = FabricUI.CreateVerticalGroup(parent, "ActionBar", spacing: 2);

        // Status row
        var statusRow = FabricUI.CreateHorizontalGroup(bar.transform, "StatusRow");
        FabricUI.AddLayout(statusRow, preferredHeight: FabricUI.S(20));

        var statusGO = FabricUI.CreateText(statusRow.transform, "StatusCount", "0/0 active",
            baseFontSize: 12);
        FabricUI.AddLayout(statusGO, flexibleWidth: 1);
        _statusText = statusGO.GetComponent<Text>();

        var changesGO = FabricUI.CreateText(statusRow.transform, "Changes", "",
            baseFontSize: 11, color: GUIHelper.ColWarn, alignment: TextAnchor.MiddleRight);
        FabricUI.AddLayout(changesGO, preferredWidth: FabricUI.S(120));
        _changesText = changesGO.GetComponent<Text>();

        // Button row
        var btnRow = FabricUI.CreateHorizontalGroup(bar.transform, "ActionButtons", spacing: 4);
        FabricUI.AddLayout(btnRow, preferredHeight: FabricUI.S(28));

        _discardBtn = FabricUI.CreateButton(btnRow.transform, "Discard", "Discard", () =>
        {
            _staged = null;
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(_discardBtn, flexibleWidth: 1, preferredHeight: FabricUI.S(26));

        _applyBtn = FabricUI.CreateButton(btnRow.transform, "Apply", "Apply", () =>
        {
            if (_staged == null) return;
            PackManager.Apply(_staged);
            _staged = null;
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(_applyBtn, preferredWidth: FabricUI.S(100), preferredHeight: FabricUI.S(26));
        _applyBg = _applyBtn.GetComponent<Image>();
    }

    // ── Footer ─────────────────────────────────────────────────────────

    private static void BuildFooter(Transform parent)
    {
        FabricUI.CreateSeparator(parent);

        var row = FabricUI.CreateHorizontalGroup(parent, "Footer", spacing: 4);
        FabricUI.AddLayout(row, preferredHeight: FabricUI.S(26));

        // Status Overlay toggle
        var overlayBtn = FabricUI.CreateButton(row.transform, "OverlayToggle",
            Plugin.ShowStatusOverlay ? "Status Overlay: ON" : "Status Overlay: OFF", () =>
            {
                Plugin.ShowStatusOverlay = !Plugin.ShowStatusOverlay;
                Rebuild();
            },
            bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(overlayBtn, flexibleWidth: 1, preferredHeight: FabricUI.S(24));

        // Dev Tools button
        var devBtn = FabricUI.CreateButton(row.transform, "DevTools", "\u2692", () =>
        {
            DevHub.OpenAt(DevHub.TabGraphics);
        }, bgColor: new Color(GUIHelper.ColPop2.r * 0.4f,
            GUIHelper.ColPop2.g * 0.4f, GUIHelper.ColPop2.b * 0.4f),
            textColor: GUIHelper.ColPop2);
        FabricUI.AddLayout(devBtn, preferredWidth: FabricUI.S(28), preferredHeight: FabricUI.S(24));
    }

    // ================================================================
    //  Rebuild — refresh dynamic content from current state
    // ================================================================

    /// <summary>
    /// Tears down and recreates all dynamic content (pack rows, profiles,
    /// conflicts, action bar state). Called on Show and after any mutation.
    /// </summary>
    private static void Rebuild()
    {
        if (_root == null) return;

        var list = _staged ?? PackManager.AllPacks.ToList();

        RefreshStripTexture();
        RebuildPackList(list);
        RebuildProfiles();
        RefreshConflicts();
        RefreshActionBar(list);
        RefreshFooter();
    }

    // ── Pack rows ──────────────────────────────────────────────────────

    private static void RebuildPackList(List<PackInfo> list)
    {
        FabricUI.ClearChildren(_packListContent);

        if (list.Count == 0)
        {
            var empty = FabricUI.CreateText(_packListContent, "Empty",
                "No packs found.\nDrop packs into Patchwork/Packs/ or install via Thunderstore.",
                baseFontSize: 12, color: GUIHelper.ColMuted);
            FabricUI.AddLayout(empty, preferredHeight: FabricUI.S(40));
            return;
        }

        for (int i = 0; i < list.Count; i++)
            BuildPackRow(list, i);
    }

    private static void BuildPackRow(List<PackInfo> list, int idx)
    {
        var pack = list[idx];
        var livePack = PackManager.AllPacks.FirstOrDefault(p =>
            string.Equals(p.Path, pack.Path, System.StringComparison.OrdinalIgnoreCase));
        bool hasConds = livePack != null && livePack.HasConditions;

        // Card container
        var card = FabricUI.CreatePanel(_packListContent, $"Pack_{idx}", Color.clear);
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = FabricUI.S(2);
        vlg.padding = new RectOffset(FabricUI.SI(4), FabricUI.SI(4), FabricUI.SI(3), FabricUI.SI(3));
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        FabricUI.AddFitter(card, vertical: ContentSizeFitter.FitMode.PreferredSize);

        // ── Main row ──────────────────────────────────────────────────
        var mainRow = FabricUI.CreateHorizontalGroup(card.transform, "MainRow", spacing: 4);
        FabricUI.AddLayout(mainRow, preferredHeight: FabricUI.S(24));
        // Override the HLG to expand children vertically
        var hlg = mainRow.GetComponent<HorizontalLayoutGroup>();
        hlg.childControlHeight = true;
        hlg.childForceExpandHeight = true;

        // Toggle button
        int capturedIdx = idx;
        Color toggleBg = pack.IsEnabled ? GUIHelper.ColConfirm : GUIHelper.ColSurface1;
        Color toggleTxt = pack.IsEnabled ? Color.white : GUIHelper.ColMuted;
        var toggle = FabricUI.CreateButton(mainRow.transform, "Toggle",
            pack.IsEnabled ? "\u2713" : "", () =>
            {
                EnsureStaged();
                _staged[capturedIdx].IsEnabled = !_staged[capturedIdx].IsEnabled;
                Rebuild();
            }, bgColor: toggleBg, textColor: toggleTxt);
        FabricUI.AddLayout(toggle, preferredWidth: FabricUI.S(22), preferredHeight: FabricUI.S(22));

        // Name + chip column
        var nameCol = FabricUI.CreateVerticalGroup(mainRow.transform, "NameCol");
        FabricUI.AddLayout(nameCol, flexibleWidth: 1);
        var nameColVlg = nameCol.GetComponent<VerticalLayoutGroup>();
        nameColVlg.childForceExpandWidth = false;
        nameColVlg.childControlWidth = false;

        var nameRow = FabricUI.CreateHorizontalGroup(nameCol.transform, "NameRow", spacing: 4);
        FabricUI.AddLayout(nameRow, preferredHeight: FabricUI.S(18));

        var nameGO = FabricUI.CreateText(nameRow.transform, "Name", pack.Name, baseFontSize: 13);
        FabricUI.AddLayout(nameGO, preferredWidth: FabricUI.S(160));

        var chipGO = FabricUI.CreateText(nameRow.transform, "Chip",
            pack.IsLocal ? "local" : "pack", baseFontSize: 10, color: GUIHelper.ColMuted);
        FabricUI.AddLayout(chipGO, preferredWidth: FabricUI.S(36));

        // Conflict badge
        int shadowed = ConflictTracker.ShadowedCount(pack.Path);
        if (shadowed > 0)
        {
            var warnGO = FabricUI.CreateText(nameRow.transform, "Warn",
                $"\u26a0 {shadowed}", baseFontSize: 11, color: GUIHelper.ColWarn);
            FabricUI.AddLayout(warnGO, preferredWidth: FabricUI.S(40));
        }

        // Priority arrows
        bool canUp = idx > 0;
        bool canDown = idx < list.Count - 1;
        var upBtn = FabricUI.CreateButton(mainRow.transform, "Up", "\u25b2", () =>
        {
            if (!canUp) return;
            EnsureStaged();
            (_staged[capturedIdx - 1], _staged[capturedIdx]) = (_staged[capturedIdx], _staged[capturedIdx - 1]);
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(upBtn, preferredWidth: FabricUI.S(26), preferredHeight: FabricUI.S(22));
        if (!canUp) upBtn.GetComponent<Button>().interactable = false;

        var downBtn = FabricUI.CreateButton(mainRow.transform, "Down", "\u25bc", () =>
        {
            if (!canDown) return;
            EnsureStaged();
            (_staged[capturedIdx + 1], _staged[capturedIdx]) = (_staged[capturedIdx], _staged[capturedIdx + 1]);
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(downBtn, preferredWidth: FabricUI.S(26), preferredHeight: FabricUI.S(22));
        if (!canDown) downBtn.GetComponent<Button>().interactable = false;

        // Gear button (conditions)
        if (livePack != null)
        {
            string packPath = pack.Path;
            bool condOpen = _conditionsOpen.Contains(packPath);
            Color gearBg = (condOpen || hasConds)
                ? new Color(GUIHelper.ColAccent.r * 0.4f, GUIHelper.ColAccent.g * 0.4f,
                    GUIHelper.ColAccent.b * 0.4f)
                : GUIHelper.ColSurface1;
            Color gearTxt = (condOpen || hasConds) ? GUIHelper.ColAccent : Color.white;
            var gear = FabricUI.CreateButton(mainRow.transform, "Gear", "\u2699", () =>
            {
                if (_conditionsOpen.Contains(packPath))
                    _conditionsOpen.Remove(packPath);
                else
                    _conditionsOpen.Add(packPath);
                Rebuild();
            }, bgColor: gearBg, textColor: gearTxt);
            FabricUI.AddLayout(gear, preferredWidth: FabricUI.S(22), preferredHeight: FabricUI.S(22));
        }

        // ── Secondary info ────────────────────────────────────────────
        var meta = new List<string>();
        if (!string.IsNullOrEmpty(pack.Author))  meta.Add($"by {pack.Author}");
        if (!string.IsNullOrEmpty(pack.Version)) meta.Add($"v{pack.Version}");
        if (!string.IsNullOrEmpty(pack.Description))
        {
            string desc = pack.Description.Length > 55
                ? pack.Description.Substring(0, 52) + "\u2026"
                : pack.Description;
            meta.Add(desc);
        }

        if (meta.Count > 0)
        {
            var infoGO = FabricUI.CreateText(card.transform, "Info",
                string.Join("   ", meta), baseFontSize: 11, color: GUIHelper.ColMuted);
            FabricUI.AddLayout(infoGO, preferredHeight: FabricUI.S(16));
            var infoRT = infoGO.GetComponent<RectTransform>();
            infoRT.offsetMin = new Vector2(FabricUI.S(30), infoRT.offsetMin.y);
        }

        // ── Asset footprint / type tags ───────────────────────────────
        string footprint = pack.Stats.Badge;
        if (footprint != null)
        {
            var fpGO = FabricUI.CreateText(card.transform, "Footprint",
                footprint, baseFontSize: 10, color: GUIHelper.ColMuted);
            FabricUI.AddLayout(fpGO, preferredHeight: FabricUI.S(14));
            var fpRT = fpGO.GetComponent<RectTransform>();
            fpRT.offsetMin = new Vector2(FabricUI.S(30), fpRT.offsetMin.y);
        }
        else
        {
            var typeTags = new List<string>();
            if (Directory.Exists(Path.Combine(pack.Path, "Sprites")))      typeTags.Add("sprites");
            if (Directory.Exists(Path.Combine(pack.Path, "Spritesheets"))) typeTags.Add("sheets");
            if (Directory.Exists(Path.Combine(pack.Path, "Sounds")))       typeTags.Add("audio");
            if (Directory.Exists(Path.Combine(pack.Path, "Videos")))       typeTags.Add("video");
            if (Directory.Exists(Path.Combine(pack.Path, "Text")))         typeTags.Add("text");
            if (typeTags.Count > 0)
            {
                var tagRow = FabricUI.CreateHorizontalGroup(card.transform, "Tags", spacing: 4);
                FabricUI.AddLayout(tagRow, preferredHeight: FabricUI.S(16));
                var tagRowRT = tagRow.GetComponent<RectTransform>();
                tagRowRT.offsetMin = new Vector2(FabricUI.S(30), tagRowRT.offsetMin.y);
                foreach (var tag in typeTags)
                {
                    var tagGO = FabricUI.CreateText(tagRow.transform, $"Tag_{tag}",
                        tag, baseFontSize: 10, color: GUIHelper.ColMuted);
                    FabricUI.AddLayout(tagGO, preferredWidth: FabricUI.S(44));
                }
            }
        }

        // ── Inline condition editor ──────────────────────────────────
        if (livePack != null && _conditionsOpen.Contains(pack.Path))
        {
            FabricUI.CreateSeparator(card.transform);
            BuildConditionEditor(card.transform, livePack);
        }

        // ── Card border ───────────────────────────────────────────────
        if (pack.IsEnabled || hasConds)
        {
            Color borderCol;
            if (pack.IsEnabled)
                borderCol = hasConds ? GUIHelper.ColAccent : GUIHelper.ColConfirm;
            else
                borderCol = GUIHelper.ColAccent;

            float a = pack.IsEnabled ? 0.7f : 0.4f;
            var outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, a);
            outline.effectDistance = new Vector2(FabricUI.S(1.5f), -FabricUI.S(1.5f));
        }
    }

    // ── Profiles ───────────────────────────────────────────────────────

    private static void RebuildProfiles()
    {
        FabricUI.ClearChildren(_profileButtonsContent);

        var names = PackManager.GetProfileNames();
        if (names.Length == 0)
        {
            var emptyGO = FabricUI.CreateText(_profileButtonsContent, "NoProfiles",
                "No saved profiles.", baseFontSize: 11, color: GUIHelper.ColMuted);
            FabricUI.AddLayout(emptyGO, preferredWidth: FabricUI.S(140));
            return;
        }

        foreach (var name in names)
        {
            string capName = name;

            var btn = FabricUI.CreateButton(_profileButtonsContent, $"Prof_{name}", name, () =>
            {
                var staged = PackManager.StageProfile(capName);
                if (staged != null) _staged = staged;
                Rebuild();
            }, bgColor: GUIHelper.ColSurface1);
            FabricUI.AddLayout(btn, preferredWidth: FabricUI.S(80), preferredHeight: FabricUI.S(22));

            var del = FabricUI.CreateButton(_profileButtonsContent, $"Del_{name}", "\u2715", () =>
            {
                PackManager.DeleteProfile(capName);
                Rebuild();
            }, bgColor: GUIHelper.ColSurface1, textColor: GUIHelper.ColDanger);
            FabricUI.AddLayout(del, preferredWidth: FabricUI.S(24), preferredHeight: FabricUI.S(22));
        }
    }

    // ── Conflicts ──────────────────────────────────────────────────────

    private static void RefreshConflicts()
    {
        int total = ConflictTracker.Total;
        _conflictsHeader.text = total == 0 ? "Conflicts: none" : $"Conflicts: {total}";

        var detailGO = _conflictsContent.gameObject;
        detailGO.SetActive(_conflictsFoldout && total > 0);
        if (!_conflictsFoldout || total == 0) return;

        FabricUI.ClearChildren(_conflictsContent);
        foreach (var e in ConflictTracker.All)
        {
            string winner = PackManager.GetPackName(e.WinnerPack);
            string loser  = PackManager.GetPackName(e.LoserPack);
            var entryGO = FabricUI.CreateText(_conflictsContent, "Conflict",
                $"[{e.Type}] {e.Key}\n  {winner}  >  {loser}",
                baseFontSize: 11, color: GUIHelper.ColMuted);
            FabricUI.AddLayout(entryGO, preferredHeight: FabricUI.S(28));
        }
    }

    // ── Action bar ─────────────────────────────────────────────────────

    private static void RefreshActionBar(List<PackInfo> list)
    {
        int active = list.Count(p => p.IsEnabled);
        _statusText.text  = $"{active}/{list.Count} active";
        _changesText.text = HasChanges ? "unsaved changes" : "";

        _discardBtn.GetComponent<Button>().interactable = HasChanges;
        _applyBtn.GetComponent<Button>().interactable   = HasChanges;
        _applyBg.color = HasChanges
            ? new Color(GUIHelper.ColConfirm.r * 0.5f,
                GUIHelper.ColConfirm.g * 0.5f, GUIHelper.ColConfirm.b * 0.5f)
            : GUIHelper.ColSurface1;
    }

    // ── Footer ─────────────────────────────────────────────────────────

    private static void RefreshFooter()
    {
        // The footer buttons are static; the overlay toggle label needs updating.
        // Since buttons are created once, we update the text via child lookup.
        var overlayText = _root.transform.Find("Content/Footer/OverlayToggle/OverlayToggle_Text");
        if (overlayText != null)
        {
            var txt = overlayText.GetComponent<Text>();
            if (txt != null)
                txt.text = Plugin.ShowStatusOverlay ? "Status Overlay: ON" : "Status Overlay: OFF";
        }
    }

    // ================================================================
    //  Condition editor
    // ================================================================

    /// <summary>
    /// Builds the condition editor for a pack: reload trigger selector,
    /// DNF condition rows with AND-group brackets, type/value/negate controls,
    /// add/remove, and a Done button.
    /// </summary>
    private static void BuildConditionEditor(Transform parent, PackInfo pack)
    {
        var box = FabricUI.CreatePanel(parent, "CondEditor",
            new Color(GUIHelper.ColSurface.r * 1.2f, GUIHelper.ColSurface.g * 1.2f,
                GUIHelper.ColSurface.b * 1.2f, 0.9f));
        var vlg = box.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = FabricUI.S(3);
        vlg.padding = new RectOffset(FabricUI.SI(6), FabricUI.SI(6), FabricUI.SI(4), FabricUI.SI(4));
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        FabricUI.AddFitter(box, vertical: ContentSizeFitter.FitMode.PreferredSize);

        // ── Reload trigger row ────────────────────────────────────────
        var triggerRow = FabricUI.CreateHorizontalGroup(box.transform, "TriggerRow", spacing: 4);
        FabricUI.AddLayout(triggerRow, preferredHeight: FabricUI.S(22));

        var triggerLabel = FabricUI.CreateText(triggerRow.transform, "TriggerLabel", "Reload:",
            baseFontSize: 11, color: GUIHelper.ColMuted);
        FabricUI.AddLayout(triggerLabel, preferredWidth: FabricUI.S(50));

        bool isScene = pack.ReloadTrigger == ReloadTrigger.OnSceneTransition;
        var sceneBtn = FabricUI.CreateButton(triggerRow.transform, "TrigScene", "on scene", () =>
        {
            pack.ReloadTrigger = ReloadTrigger.OnSceneTransition;
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: isScene ? new Color(GUIHelper.ColAccent.r * 0.3f, GUIHelper.ColAccent.g * 0.3f,
            GUIHelper.ColAccent.b * 0.3f) : GUIHelper.ColSurface1,
            textColor: isScene ? GUIHelper.ColAccent : Color.white);
        FabricUI.AddLayout(sceneBtn, preferredWidth: FabricUI.S(72), preferredHeight: FabricUI.S(20));

        bool isHot = pack.ReloadTrigger == ReloadTrigger.HotReload;
        var hotBtn = FabricUI.CreateButton(triggerRow.transform, "TrigHot", "hot reload", () =>
        {
            pack.ReloadTrigger = ReloadTrigger.HotReload;
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: isHot ? new Color(GUIHelper.ColAccent.r * 0.3f, GUIHelper.ColAccent.g * 0.3f,
            GUIHelper.ColAccent.b * 0.3f) : GUIHelper.ColSurface1,
            textColor: isHot ? GUIHelper.ColAccent : Color.white);
        FabricUI.AddLayout(hotBtn, preferredWidth: FabricUI.S(80), preferredHeight: FabricUI.S(20));

        // ── Condition rows (DNF) ──────────────────────────────────────
        var clauses = BuildClauses(pack.Conditions);
        for (int clauseIdx = 0; clauseIdx < clauses.Count; clauseIdx++)
        {
            var  clause  = clauses[clauseIdx];
            bool isFirst = clauseIdx == 0;
            bool isGroup = clause.Count > 1;

            // Interstitial OR button between clauses
            if (!isFirst)
            {
                int orJoinIdx = clause[0] - 1;
                var orRow = FabricUI.CreateHorizontalGroup(box.transform, "OrRow", spacing: 0);
                FabricUI.AddLayout(orRow, preferredHeight: FabricUI.S(20));

                var orSpacer = new GameObject("OrSpacer", typeof(RectTransform));
                orSpacer.transform.SetParent(orRow.transform, false);
                FabricUI.AddLayout(orSpacer, preferredWidth: FabricUI.S(10));

                int capOrIdx = orJoinIdx;
                var orBtn = FabricUI.CreateButton(orRow.transform, "OrBtn", "OR", () =>
                {
                    pack.Conditions[capOrIdx].JoinNext = LogicJoin.And;
                    PackManager.SaveConditions();
                    Rebuild();
                }, bgColor: Color.clear, textColor: GUIHelper.ColAccent);
                FabricUI.AddLayout(orBtn, preferredWidth: FabricUI.S(38), preferredHeight: FabricUI.S(18));
            }

            if (isGroup)
            {
                // AND-group: bracket bar on the left
                var groupRow = FabricUI.CreateHorizontalGroup(box.transform, $"AndGroup_{clauseIdx}", spacing: 4);
                FabricUI.AddLayout(groupRow, preferredHeight: FabricUI.S(24 * clause.Count + 20 * (clause.Count - 1)));

                // Bracket bar
                var bracket = FabricUI.CreatePanel(groupRow.transform, "Bracket",
                    new Color(GUIHelper.ColPop2.r, GUIHelper.ColPop2.g, GUIHelper.ColPop2.b, 0.85f));
                FabricUI.AddLayout(bracket, preferredWidth: FabricUI.S(4));

                // Column of conditions with AND interstitials
                var colGO = FabricUI.CreateVerticalGroup(groupRow.transform, "AndCol", spacing: 2);
                FabricUI.AddLayout(colGO, flexibleWidth: 1);

                for (int j = 0; j < clause.Count; j++)
                {
                    int ci = clause[j];

                    // AND interstitial
                    if (j > 0)
                    {
                        int andJoinIdx = ci - 1;
                        int capAndIdx = andJoinIdx;
                        var andBtn = FabricUI.CreateButton(colGO.transform, "AndBtn", "AND", () =>
                        {
                            pack.Conditions[capAndIdx].JoinNext = LogicJoin.Or;
                            PackManager.SaveConditions();
                            Rebuild();
                        }, bgColor: Color.clear, textColor: GUIHelper.ColAccent);
                        FabricUI.AddLayout(andBtn, preferredHeight: FabricUI.S(18));
                    }

                    BuildConditionRow(colGO.transform, pack, ci);
                }
            }
            else
            {
                // Single condition row
                BuildConditionRow(box.transform, pack, clause[0]);
            }
        }

        // ── Add condition row ─────────────────────────────────────────
        var addRow = FabricUI.CreateHorizontalGroup(box.transform, "AddRow", spacing: 0);
        FabricUI.AddLayout(addRow, preferredHeight: FabricUI.S(22));

        var addSpacer = new GameObject("AddSpacer", typeof(RectTransform));
        addSpacer.transform.SetParent(addRow.transform, false);
        FabricUI.AddLayout(addSpacer, preferredWidth: FabricUI.S(10));

        var addBtn = FabricUI.CreateButton(addRow.transform, "AddCond", "+ Add A New Condition", () =>
        {
            pack.Conditions.Add(new PackCondition());
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: Color.clear,
            textColor: new Color(GUIHelper.ColPop1.r, GUIHelper.ColPop1.g, GUIHelper.ColPop1.b, 0.85f));
        FabricUI.AddLayout(addBtn, flexibleWidth: 1, preferredHeight: FabricUI.S(20));

        // ── Done button ───────────────────────────────────────────────
        var doneRow = FabricUI.CreateHorizontalGroup(box.transform, "DoneRow", spacing: 0);
        FabricUI.AddLayout(doneRow, preferredHeight: FabricUI.S(22));

        var doneSpacer = new GameObject("DoneSpacer", typeof(RectTransform));
        doneSpacer.transform.SetParent(doneRow.transform, false);
        FabricUI.AddLayout(doneSpacer, flexibleWidth: 1);

        string packPath2 = pack.Path;
        var doneBtn = FabricUI.CreateButton(doneRow.transform, "Done", "Done", () =>
        {
            _conditionsOpen.Remove(packPath2);
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(doneBtn, preferredWidth: FabricUI.S(50), preferredHeight: FabricUI.S(20));
    }

    /// <summary>
    /// Builds a single condition row: [type dropdown] [negate] [value input] [remove].
    /// </summary>
    private static void BuildConditionRow(Transform parent, PackInfo pack, int ci)
    {
        var cond = pack.Conditions[ci];
        var row = FabricUI.CreateHorizontalGroup(parent, $"Cond_{ci}", spacing: 3);
        FabricUI.AddLayout(row, preferredHeight: FabricUI.S(22));

        // Type button (opens inline type picker — for Canvas, we use a simple cycle)
        int capCi = ci;
        var typeBtn = FabricUI.CreateButton(row.transform, "Type", cond.TypeLabel, () =>
        {
            // Cycle through condition types
            var values = (ConditionType[])System.Enum.GetValues(typeof(ConditionType));
            int cur = System.Array.IndexOf(values, pack.Conditions[capCi].Type);
            pack.Conditions[capCi].Type = values[(cur + 1) % values.Length];
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1, textColor: GUIHelper.ColAccent);
        FabricUI.AddLayout(typeBtn, preferredWidth: FabricUI.S(62), preferredHeight: FabricUI.S(20));

        // Negate toggle
        string negLabel = cond.Negate ? "!=" : "==";
        var negBtn = FabricUI.CreateButton(row.transform, "Negate", negLabel, () =>
        {
            pack.Conditions[capCi].Negate = !pack.Conditions[capCi].Negate;
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: GUIHelper.ColSurface1);
        FabricUI.AddLayout(negBtn, preferredWidth: FabricUI.S(30), preferredHeight: FabricUI.S(20));

        // Value input
        if (cond.Type == ConditionType.CrestEquipped)
        {
            // Cycle through known crests
            string displayVal = PackCondition.CrestDisplayName(cond.Value);
            var crestBtn = FabricUI.CreateButton(row.transform, "CrestVal", displayVal, () =>
            {
                var crests = PackCondition.KnownCrests;
                int cur = -1;
                for (int k = 0; k < crests.Length; k++)
                    if (string.Equals(crests[k].Id, pack.Conditions[capCi].Value,
                        System.StringComparison.OrdinalIgnoreCase))
                    { cur = k; break; }
                pack.Conditions[capCi].Value = crests[(cur + 1) % crests.Length].Id;
                PackManager.SaveConditions();
                Rebuild();
            }, bgColor: GUIHelper.ColSurface1);
            FabricUI.AddLayout(crestBtn, preferredWidth: FabricUI.S(120), preferredHeight: FabricUI.S(20));
        }
        else if (cond.Type == ConditionType.NailUpgrade)
        {
            // Cycle through known nail levels
            string displayVal = PackCondition.NailDisplayValue(cond.Value);
            var nailBtn = FabricUI.CreateButton(row.transform, "NailVal", displayVal, () =>
            {
                var levels = PackCondition.KnownNailLevels;
                int cur = -1;
                for (int k = 0; k < levels.Length; k++)
                    if (string.Equals(levels[k].Value, pack.Conditions[capCi].Value,
                        System.StringComparison.OrdinalIgnoreCase))
                    { cur = k; break; }
                pack.Conditions[capCi].Value = levels[(cur + 1) % levels.Length].Value;
                PackManager.SaveConditions();
                Rebuild();
            }, bgColor: GUIHelper.ColSurface1);
            FabricUI.AddLayout(nailBtn, preferredWidth: FabricUI.S(120), preferredHeight: FabricUI.S(20));
        }
        else
        {
            // Free-text input for Scene, SceneContains, PackActive, PlayerData
            var inputGO = FabricUI.CreateInputField(row.transform, $"CondVal_{ci}",
                placeholder: "value");
            FabricUI.AddLayout(inputGO, preferredWidth: FabricUI.S(120), preferredHeight: FabricUI.S(20));
            var input = inputGO.GetComponent<InputField>();
            input.text = cond.Value;
            int capCi2 = ci;
            input.onEndEdit.AddListener(val =>
            {
                if (val != pack.Conditions[capCi2].Value)
                {
                    pack.Conditions[capCi2].Value = val;
                    PackManager.SaveConditions();
                }
            });
        }

        // Remove button
        var removeBtn = FabricUI.CreateButton(row.transform, "Remove", "\u00d7", () =>
        {
            pack.Conditions.RemoveAt(capCi);
            PackManager.SaveConditions();
            Rebuild();
        }, bgColor: Color.clear, textColor: GUIHelper.ColDanger);
        FabricUI.AddLayout(removeBtn, preferredWidth: FabricUI.S(22), preferredHeight: FabricUI.S(20));
    }

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

    // ================================================================
    //  Staging
    // ================================================================

    private static void EnsureStaged()
    {
        _staged ??= PackManager.AllPacks.Select(p => p.Clone()).ToList();
    }
}

// ====================================================================
//  Drag handler MonoBehaviour
// ====================================================================

/// <summary>
/// Allows a Canvas panel to be dragged by pointer. Attach to the root
/// <see cref="GameObject"/> of a Canvas-based window.
/// </summary>
public class CanvasPanelDragger : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private Vector2 _offset;

    public void OnBeginDrag(PointerEventData eventData)
    {
        var rt = GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt.parent as RectTransform, eventData.position, eventData.pressEventCamera, out var local);
        _offset = rt.anchoredPosition - local;
    }

    public void OnDrag(PointerEventData eventData)
    {
        var rt = GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt.parent as RectTransform, eventData.position, eventData.pressEventCamera, out var local);
        rt.anchoredPosition = local + _offset;
    }
}
