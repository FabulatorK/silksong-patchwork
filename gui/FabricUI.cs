using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Patchwork.GUI;

/// <summary>
/// Static utility factory for building Unity Canvas UI elements programmatically.
/// All sizing is resolution-independent via <see cref="GUIHelper.Scale"/>.
/// Colour tokens flow from <see cref="GUIHelper"/> (ColSurface, ColAccent, etc.).
/// </summary>
public static class FabricUI
{
    private static Canvas _rootCanvas;
    private static CanvasScaler _scaler;
    private static GameObject _rootGO;
    private static EventSystem _eventSystem;
    private static Font _font;

    // Sprite cache: key = (radius, size)
    private static readonly Dictionary<(int, int), Sprite> _roundedSpriteCache = new();

    // ================================================================
    //  Initialization
    // ================================================================

    /// <summary>
    /// Creates the root ScreenSpaceOverlay Canvas (sort order 100) and ensures
    /// an EventSystem exists in the scene. Locates a game font via
    /// <c>Resources.FindObjectsOfTypeAll&lt;Font&gt;</c>, preferring ARIAL.
    /// Safe to call multiple times; subsequent calls are no-ops if the canvas
    /// is still alive.
    /// </summary>
    public static Canvas Init()
    {
        if (_rootCanvas != null) return _rootCanvas;

        // Find font
        _font = FindGameFont();

        // Root Canvas
        _rootGO = new GameObject("PatchworkCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_rootGO);

        _rootCanvas = _rootGO.AddComponent<Canvas>();
        _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _rootCanvas.sortingOrder = 100;
        _rootCanvas.pixelPerfect = false;

        _scaler = _rootGO.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.scaleFactor = 1f;

        _rootGO.AddComponent<GraphicRaycaster>();

        // EventSystem
        EnsureEventSystem();

        return _rootCanvas;
    }

    /// <summary>Returns the root Canvas, initialising if needed.</summary>
    public static Canvas RootCanvas => _rootCanvas != null ? _rootCanvas : Init();

    /// <summary>Returns the resolved game font.</summary>
    public static Font GameFont => _font ?? (_font = FindGameFont());

    /// <summary>Whether <see cref="Init"/> has been called and the canvas is alive.</summary>
    public static bool IsReady => _rootCanvas != null;

    // ================================================================
    //  Font discovery
    // ================================================================

    private static Font FindGameFont()
    {
        Font fallback = Font.CreateDynamicFontFromOSFont("Arial", 14);
        try
        {
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var f in fonts)
            {
                if (f == null) continue;
                string n = f.name.ToUpperInvariant();
                if (n.Contains("ARIAL"))
                    return f;
            }
            // No ARIAL found; take the first non-null font
            foreach (var f in fonts)
                if (f != null) return f;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[FabricUI] Font search failed: {ex.Message}");
        }
        return fallback;
    }

    // ================================================================
    //  EventSystem
    // ================================================================

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var esGO = new GameObject("PatchworkEventSystem");
        UnityEngine.Object.DontDestroyOnLoad(esGO);
        _eventSystem = esGO.AddComponent<EventSystem>();
        esGO.AddComponent<StandaloneInputModule>();
    }

    // ================================================================
    //  Scaled helpers
    // ================================================================

    /// <summary>Scale a base-1080p value by the current resolution factor.</summary>
    public static float S(float v) => v * GUIHelper.Scale;

    /// <summary>Rounded integer scale.</summary>
    public static int SI(float v) => Mathf.RoundToInt(v * GUIHelper.Scale);

    /// <summary>Scaled font size (minimum 8).</summary>
    public static int FontSize(int baseSize) => Mathf.Max(8, SI(baseSize));

    // ================================================================
    //  RectTransform helpers
    // ================================================================

    /// <summary>
    /// Configure a RectTransform with stretch-all anchoring and explicit offsets.
    /// Offsets are in scaled pixels.
    /// </summary>
    public static RectTransform SetStretch(RectTransform rt,
        float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    /// <summary>
    /// Configure a RectTransform with explicit anchors and pivot, plus size and position.
    /// </summary>
    public static RectTransform SetAnchored(RectTransform rt,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        return rt;
    }

    /// <summary>
    /// Configure a RectTransform anchored at top-left with a given size in scaled pixels.
    /// </summary>
    public static RectTransform SetTopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    /// <summary>Stretch horizontally, fixed height at top.</summary>
    public static RectTransform SetStretchTop(RectTransform rt, float height, float left = 0, float right = 0, float top = 0)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, -top - height);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    /// <summary>Stretch horizontally, fixed height at bottom.</summary>
    public static RectTransform SetStretchBottom(RectTransform rt, float height, float left = 0, float right = 0, float bottom = 0)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, bottom + height);
        return rt;
    }

    /// <summary>Anchor at left, stretch vertically.</summary>
    public static RectTransform SetStretchLeft(RectTransform rt, float width, float top = 0, float bottom = 0, float left = 0)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(left + width, -top);
        return rt;
    }

    // ================================================================
    //  Element creation
    // ================================================================

    /// <summary>
    /// Creates a panel (GameObject with Image + RectTransform) under <paramref name="parent"/>.
    /// </summary>
    public static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return go;
    }

    /// <summary>
    /// Creates a Text element under <paramref name="parent"/>.
    /// </summary>
    public static GameObject CreateText(Transform parent, string name, string text,
        int baseFontSize = 14, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = GameFont;
        t.fontSize = FontSize(baseFontSize);
        t.color = color ?? Color.white;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return go;
    }

    /// <summary>
    /// Creates a Button with Image background and child Text.
    /// Wires <paramref name="onClick"/> and sets up a simple hover colour transition.
    /// </summary>
    public static GameObject CreateButton(Transform parent, string name, string text,
        Action onClick, Color? bgColor = null, Color? textColor = null)
    {
        Color bg = bgColor ?? GUIHelper.ColSurface1;
        Color tc = textColor ?? Color.white;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;

        if (onClick != null)
            btn.onClick.AddListener(() => onClick());

        // Child text
        var txtGO = CreateText(go.transform, name + "_Text", text, 14, tc, TextAnchor.MiddleCenter);
        var txtRT = txtGO.GetComponent<RectTransform>();
        SetStretch(txtRT);
        txtGO.GetComponent<Text>().raycastTarget = false;

        return go;
    }

    /// <summary>
    /// Creates an InputField with background Image, child text, and placeholder.
    /// </summary>
    public static GameObject CreateInputField(Transform parent, string name,
        string placeholder = "", Action<string> onChange = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var bgImg = go.AddComponent<Image>();
        bgImg.color = new Color(GUIHelper.ColSurface.r * 1.4f, GUIHelper.ColSurface.g * 1.4f,
            GUIHelper.ColSurface.b * 1.4f, 1f);
        bgImg.raycastTarget = true;

        // Text area
        var textAreaGO = new GameObject(name + "_TextArea", typeof(RectTransform));
        textAreaGO.transform.SetParent(go.transform, false);
        var textAreaRT = textAreaGO.GetComponent<RectTransform>();
        SetStretch(textAreaRT, S(6), S(6), S(2), S(2));
        textAreaGO.AddComponent<RectMask2D>();

        // Child text
        var txtGO = new GameObject(name + "_ChildText", typeof(RectTransform));
        txtGO.transform.SetParent(textAreaGO.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = GameFont;
        txt.fontSize = FontSize(13);
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.supportRichText = false;
        var txtRT = txtGO.GetComponent<RectTransform>();
        SetStretch(txtRT);

        // Placeholder
        var phGO = new GameObject(name + "_Placeholder", typeof(RectTransform));
        phGO.transform.SetParent(textAreaGO.transform, false);
        var ph = phGO.AddComponent<Text>();
        ph.font = GameFont;
        ph.fontSize = FontSize(13);
        ph.color = GUIHelper.ColMuted;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.horizontalOverflow = HorizontalWrapMode.Overflow;
        ph.verticalOverflow = VerticalWrapMode.Overflow;
        ph.fontStyle = FontStyle.Italic;
        ph.text = placeholder;
        var phRT = phGO.GetComponent<RectTransform>();
        SetStretch(phRT);

        var input = go.AddComponent<InputField>();
        input.targetGraphic = bgImg;
        input.textComponent = txt;
        input.placeholder = ph;
        input.caretColor = GUIHelper.ColAccent;
        input.selectionColor = new Color(GUIHelper.ColAccent.r, GUIHelper.ColAccent.g, GUIHelper.ColAccent.b, 0.3f);
        input.characterLimit = 0;

        if (onChange != null)
            input.onValueChanged.AddListener(val => onChange(val));

        return go;
    }

    /// <summary>
    /// Creates a ScrollRect with viewport, content, and vertical scrollbar.
    /// Returns the ScrollRect's GameObject. Content is accessed via
    /// <c>go.GetComponent&lt;ScrollRect&gt;().content</c>.
    /// </summary>
    public static GameObject CreateScrollView(Transform parent, string name)
    {
        // Root
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var scrollRect = go.AddComponent<ScrollRect>();
        go.AddComponent<Image>().color = Color.clear; // raycaster needs Image

        // Viewport
        var viewport = new GameObject(name + "_Viewport", typeof(RectTransform));
        viewport.transform.SetParent(go.transform, false);
        viewport.AddComponent<Image>().color = Color.clear;
        viewport.AddComponent<RectMask2D>();
        var vpRT = viewport.GetComponent<RectTransform>();
        SetStretch(vpRT, 0, S(8), 0, 0); // leave room for scrollbar

        // Content
        var content = new GameObject(name + "_Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cRT = content.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1);
        cRT.anchorMax = new Vector2(1, 1);
        cRT.pivot = new Vector2(0, 1);
        cRT.anchoredPosition = Vector2.zero;
        cRT.sizeDelta = new Vector2(0, 0);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Vertical scrollbar
        var scrollbarGO = new GameObject(name + "_Scrollbar", typeof(RectTransform));
        scrollbarGO.transform.SetParent(go.transform, false);
        var scrollbarImg = scrollbarGO.AddComponent<Image>();
        scrollbarImg.color = new Color(GUIHelper.ColSurface.r, GUIHelper.ColSurface.g,
            GUIHelper.ColSurface.b, 0.5f);
        var sbRT = scrollbarGO.GetComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0);
        sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 0.5f);
        sbRT.sizeDelta = new Vector2(S(6), 0);
        sbRT.anchoredPosition = Vector2.zero;

        // Scrollbar handle
        var handleArea = new GameObject(name + "_HandleArea", typeof(RectTransform));
        handleArea.transform.SetParent(scrollbarGO.transform, false);
        SetStretch(handleArea.GetComponent<RectTransform>());

        var handle = new GameObject(name + "_Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = GUIHelper.ColBorder;
        SetStretch(handle.GetComponent<RectTransform>());

        var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handleImg;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        // Wire it all up
        scrollRect.viewport = vpRT;
        scrollRect.content = cRT;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = S(30);

        return go;
    }

    /// <summary>
    /// Creates a HorizontalLayoutGroup on a new GameObject.
    /// </summary>
    public static GameObject CreateHorizontalGroup(Transform parent, string name,
        float spacing = 0, RectOffset padding = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = S(spacing);
        hlg.padding = padding ?? new RectOffset(0, 0, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        return go;
    }

    /// <summary>
    /// Creates a VerticalLayoutGroup on a new GameObject.
    /// </summary>
    public static GameObject CreateVerticalGroup(Transform parent, string name,
        float spacing = 0, RectOffset padding = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = S(spacing);
        vlg.padding = padding ?? new RectOffset(0, 0, 0, 0);
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        return go;
    }

    /// <summary>
    /// Procedurally generates a Texture2D with rounded corners, returned as a Sprite.
    /// Results are cached by (radius, size) pair.
    /// </summary>
    public static Sprite CreateRoundedSprite(int radius, int size)
    {
        var key = (radius, size);
        if (_roundedSpriteCache.TryGetValue(key, out var cached))
            return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float r = radius;

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float alpha = 1f;

                // Bottom-left
                if (px < r && py < r)
                    alpha = CornerAlpha(px, py, r);
                // Bottom-right
                else if (px > size - 1 - r && py < r)
                    alpha = CornerAlpha(size - 1 - px, py, r);
                // Top-left
                else if (px < r && py > size - 1 - r)
                    alpha = CornerAlpha(px, size - 1 - py, r);
                // Top-right
                else if (px > size - 1 - r && py > size - 1 - r)
                    alpha = CornerAlpha(size - 1 - px, size - 1 - py, r);

                pixels[py * size + px] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // 9-slice border: radius on each side so centre stretches
        var border = new Vector4(radius, radius, radius, radius);
        var sprite = Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);

        _roundedSpriteCache[key] = sprite;
        return sprite;
    }

    private static float CornerAlpha(float px, float py, float r)
    {
        float dx = r - px;
        float dy = r - py;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(r - dist + 0.5f); // +0.5 for AA
    }

    /// <summary>
    /// Creates a LayoutElement on <paramref name="go"/> and sets preferred/min sizes.
    /// Pass -1 to leave a dimension unset.
    /// </summary>
    public static LayoutElement AddLayout(GameObject go,
        float preferredWidth = -1, float preferredHeight = -1,
        float minWidth = -1, float minHeight = -1,
        float flexibleWidth = -1, float flexibleHeight = -1,
        bool ignoreLayout = false)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (preferredWidth >= 0)  le.preferredWidth  = preferredWidth;
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
        if (minWidth >= 0)        le.minWidth        = minWidth;
        if (minHeight >= 0)       le.minHeight       = minHeight;
        if (flexibleWidth >= 0)   le.flexibleWidth   = flexibleWidth;
        if (flexibleHeight >= 0)  le.flexibleHeight  = flexibleHeight;
        le.ignoreLayout = ignoreLayout;
        return le;
    }

    /// <summary>
    /// Creates a thin horizontal separator line.
    /// </summary>
    public static GameObject CreateSeparator(Transform parent, string name = "Separator")
    {
        var go = CreatePanel(parent, name, new Color(GUIHelper.ColBorder.r,
            GUIHelper.ColBorder.g, GUIHelper.ColBorder.b, 0.6f));
        AddLayout(go, preferredHeight: S(1), flexibleWidth: 1);
        return go;
    }

    /// <summary>
    /// Adds a ContentSizeFitter set to preferred on both axes.
    /// </summary>
    public static ContentSizeFitter AddFitter(GameObject go,
        ContentSizeFitter.FitMode horizontal = ContentSizeFitter.FitMode.Unconstrained,
        ContentSizeFitter.FitMode vertical = ContentSizeFitter.FitMode.PreferredSize)
    {
        var csf = go.GetComponent<ContentSizeFitter>() ?? go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = horizontal;
        csf.verticalFit = vertical;
        return csf;
    }

    /// <summary>
    /// Creates a RawImage element for displaying procedural textures (e.g. cassette strip).
    /// </summary>
    public static GameObject CreateRawImage(Transform parent, string name, Texture texture, Color? tint = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var raw = go.AddComponent<RawImage>();
        raw.texture = texture;
        raw.color = tint ?? Color.white;
        raw.raycastTarget = false;
        return go;
    }

    /// <summary>
    /// Creates a Toggle UI element with a checkmark visual.
    /// </summary>
    public static GameObject CreateToggle(Transform parent, string name, bool isOn,
        Action<bool> onChanged, Color? bgColor = null, Color? checkColor = null)
    {
        Color bg = bgColor ?? GUIHelper.ColSurface1;
        Color ck = checkColor ?? GUIHelper.ColConfirm;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        // Background
        var bgImg = go.AddComponent<Image>();
        bgImg.color = bg;
        bgImg.raycastTarget = true;

        // Checkmark
        var checkGO = CreateText(go.transform, name + "_Check", "\u2713", 16, ck, TextAnchor.MiddleCenter);
        SetStretch(checkGO.GetComponent<RectTransform>());
        checkGO.SetActive(isOn);

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkGO.GetComponent<Text>();
        toggle.isOn = isOn;

        if (onChanged != null)
            toggle.onValueChanged.AddListener(val =>
            {
                checkGO.SetActive(val);
                onChanged(val);
            });

        return go;
    }

    /// <summary>
    /// Destroys all children of <paramref name="parent"/>.
    /// Useful when rebuilding dynamic lists.
    /// </summary>
    public static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
    }
}
