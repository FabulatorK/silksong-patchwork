
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Shared UI scaling, styling, IMGUI focus tracking, and game input blocking.
/// Base resolution: 1080p (1920x1080)
/// </summary>
public static class GUIHelper
{
    private const float BaseHeight = 1080f;
    private const float MinScale = 0.75f;
    private const float MaxScale = 2.5f;

    private static float? cachedScale;
    private static int lastScreenHeight;

    private static GUIStyle _windowStyle;
    private static GUIStyle _labelStyle;
    private static GUIStyle _buttonStyle;
    private static GUIStyle _toggleStyle;
    private static GUIStyle _textFieldStyle;

    private static int _cachedFontSize;

    // ============================================================================
    // STYLES
    // ============================================================================

    public static GUIStyle LabelStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_labelStyle == null || _cachedFontSize != fontSize)
            {
                _labelStyle = new GUIStyle(UnityEngine.GUI.skin.label)
                {
                    fontSize = fontSize
                };
                _cachedFontSize = fontSize;
            }

            return _labelStyle;
        }
    }

    public static GUIStyle ButtonStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_buttonStyle == null || _buttonStyle.fontSize != fontSize)
            {
                _buttonStyle = new GUIStyle(UnityEngine.GUI.skin.button)
                {
                    fontSize = fontSize
                };
            }

            return _buttonStyle;
        }
    }

    public static GUIStyle ToggleStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_toggleStyle == null || _toggleStyle.fontSize != fontSize)
            {
                _toggleStyle = new GUIStyle(UnityEngine.GUI.skin.toggle)
                {
                    fontSize = fontSize
                };
            }

            return _toggleStyle;
        }
    }

    public static GUIStyle WindowStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_windowStyle == null || _windowStyle.fontSize != fontSize)
            {
                _windowStyle = new GUIStyle(UnityEngine.GUI.skin.window)
                {
                    fontSize = fontSize,
                    padding = new RectOffset(
                        ScaledInt(10),
                        ScaledInt(10),
                        ScaledInt(16),
                        ScaledInt(10)
                    ),
                    contentOffset = new Vector2(0, ScaledInt(-4)),

                    // Keep focused/active appearance consistent.
                    onNormal = UnityEngine.GUI.skin.window.normal,
                    onFocused = UnityEngine.GUI.skin.window.normal,
                    onActive = UnityEngine.GUI.skin.window.normal,
                    focused = UnityEngine.GUI.skin.window.normal,
                    active = UnityEngine.GUI.skin.window.normal
                };
            }

            return _windowStyle;
        }
    }

    public static GUIStyle TextFieldStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_textFieldStyle == null || _textFieldStyle.fontSize != fontSize)
            {
                _textFieldStyle = new GUIStyle(UnityEngine.GUI.skin.textField)
                {
                    fontSize = fontSize,
                    padding = new RectOffset(
                        ScaledInt(6),
                        ScaledInt(6),
                        ScaledInt(4),
                        ScaledInt(4)
                    )
                };
            }

            return _textFieldStyle;
        }
    }

    // ── Design tokens ─────────────────────────────────────────────────────────
    // Cassette-label palette: warm orange accent, teal confirm, magenta-red danger.
    // Strip identity colours (StripRed / StripBone) are for the side-band only.
    public static readonly Color ColSurface    = new Color(0.10f, 0.10f, 0.13f, 1f);
    public static readonly Color ColSurface1   = new Color(0.17f, 0.17f, 0.22f, 1f);
    public static readonly Color ColBorder     = new Color(0.28f, 0.28f, 0.35f, 1f);
    public static readonly Color ColAccent     = new Color(0.91f, 0.46f, 0.19f, 1f); // #E87530 orange
    public static readonly Color ColAccentSoft = new Color(0.80f, 0.33f, 0.13f, 1f); // #CC5522 burnt orange
    public static readonly Color ColMuted      = new Color(0.55f, 0.55f, 0.65f, 1f);
    public static readonly Color ColConfirm    = new Color(0.20f, 0.67f, 0.53f, 1f); // #33AA88 teal-green
    public static readonly Color ColSuccess    = new Color(0.20f, 0.67f, 0.53f, 1f); // alias → ColConfirm
    public static readonly Color ColWarn       = new Color(1.00f, 0.75f, 0.20f, 1f);
    public static readonly Color ColDanger     = new Color(0.80f, 0.20f, 0.40f, 1f); // #CC3366 magenta-red
    public static readonly Color ColBone       = new Color(0.87f, 0.85f, 0.82f, 1f); // #DDD8D0 mask off-white
    public static readonly Color ColPop1       = new Color(0.24f, 0.73f, 0.48f, 1f); // #3DBB7A specular green
    public static readonly Color ColPop2       = new Color(0.24f, 0.56f, 0.73f, 1f); // #3D8EBB specular blue

    // ── Cassette strip bands ─────────────────────────────────────────────────
    // Proportions: 40% red, 10% orange, 5% green transition, 5% blue transition, 40% bone.
    public static readonly Color StripRed    = new Color(0.80f, 0.13f, 0.13f, 1f); // #CC2222 Hornet crimson
    public static readonly Color StripOrange = new Color(0.91f, 0.46f, 0.19f, 1f); // = ColAccent
    public static readonly Color StripGreen  = new Color(0.24f, 0.73f, 0.48f, 1f); // = ColPop1
    public static readonly Color StripBlue   = new Color(0.24f, 0.56f, 0.73f, 1f); // = ColPop2
    public static readonly Color StripBone   = new Color(0.87f, 0.85f, 0.82f, 1f); // = ColBone

    /// <summary>Band ratios for the cassette strip (must sum to 1.0).</summary>
    private static readonly (Color color, float ratio)[] StripBands =
    {
        (StripRed,    0.40f),
        (StripOrange, 0.10f),
        (StripGreen,  0.05f),
        (StripBlue,   0.05f),
        (StripBone,   0.40f),
    };

    /// <summary>
    /// Draw a vertical cassette-label strip. Uses window-local coordinates (0,0 = top-left
    /// of the window), so call from inside a <c>GUILayout.Window</c> callback.
    /// </summary>
    public static void DrawCassetteStripVertical(Rect windowRect, float stripWidth)
    {
        float sw = Scaled(stripWidth);
        // Window-local coords: x starts at a small inset, y starts below the title bar.
        float x  = Scaled(4f);
        float y  = Scaled(20f);
        float h  = windowRect.height - Scaled(28f);

        float yOff = 0f;
        foreach (var (color, ratio) in StripBands)
        {
            float bandH = h * ratio;
            UnityEngine.GUI.color = color;
            UnityEngine.GUI.DrawTexture(new Rect(x, y + yOff, sw, bandH), Texture2D.whiteTexture);
            yOff += bandH;
        }
        UnityEngine.GUI.color = Color.white;
    }

    /// <summary>
    /// Draw a horizontal cassette-label strip. Runs left-to-right with a transparency
    /// fade over the final 40% of its length. Designed for compact elements like StatusOverlay.
    /// </summary>
    public static void DrawCassetteStripHorizontal(Rect area, float stripHeight)
    {
        float sh = Scaled(stripHeight);
        float y  = area.yMax - sh - Scaled(2f); // 2px margin above bottom edge
        float totalW = area.width - Scaled(4f); // 2px margin each side
        float x  = area.x + Scaled(2f);

        // Fade: last 40% of the strip length fades to transparent.
        float fadeStart = 0.60f;

        float xOff = 0f;
        foreach (var (color, ratio) in StripBands)
        {
            float bandW = totalW * ratio;
            // How far into the total strip is this band's midpoint?
            float midNorm = (xOff + bandW * 0.5f) / totalW;
            float alpha = midNorm < fadeStart ? 1f
                        : 1f - (midNorm - fadeStart) / (1f - fadeStart);
            alpha = Mathf.Clamp01(alpha) * 0.85f; // slightly translucent overall

            UnityEngine.GUI.color = new Color(color.r, color.g, color.b, alpha);
            UnityEngine.GUI.DrawTexture(new Rect(x + xOff, y, bandW, sh), Texture2D.whiteTexture);
            xOff += bandW;
        }
        UnityEngine.GUI.color = Color.white;
    }

    /// <summary>Create a 1×1 solid-colour Texture2D. Style caches should hold the ref.</summary>
    public static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    /// <summary>Draw a 1px-tall horizontal rule tinted with <see cref="ColBorder"/>.</summary>
    public static void DrawSeparator()
    {
        var r = GUILayoutUtility.GetRect(0, Scaled(1f), GUILayout.ExpandWidth(true));
        UnityEngine.GUI.color = new Color(ColBorder.r, ColBorder.g, ColBorder.b, 0.6f);
        UnityEngine.GUI.DrawTexture(r, Texture2D.whiteTexture);
        UnityEngine.GUI.color = Color.white;
    }

    // ── Derived styles ───────────────────────────────────────────────────────

    private static GUIStyle _cardStyle;
    private static int      _cachedCardPad;

    /// <summary>Flat-colour container background for list cards (pack rows).</summary>
    public static GUIStyle CardStyle
    {
        get
        {
            int p = ScaledInt(6);
            if (_cardStyle == null || _cachedCardPad != p)
            {
                _cachedCardPad = p;
                _cardStyle = new GUIStyle(GUIStyle.none)
                {
                    padding = new RectOffset(p, p, ScaledInt(4), ScaledInt(4)),
                };
                _cardStyle.normal.background = MakeTex(ColSurface1);
            }
            return _cardStyle;
        }
    }

    private static GUIStyle _chipStyle;
    private static int      _cachedChipFontSize;

    /// <summary>Non-clickable badge label (source origin, asset type tags).</summary>
    public static GUIStyle ChipStyle
    {
        get
        {
            int fs = FontSize(11);
            if (_chipStyle == null || _cachedChipFontSize != fs)
            {
                _cachedChipFontSize = fs;
                _chipStyle = new GUIStyle(UnityEngine.GUI.skin.label)
                {
                    fontSize  = fs,
                    alignment = TextAnchor.MiddleCenter,
                    padding   = new RectOffset(ScaledInt(5), ScaledInt(5), ScaledInt(2), ScaledInt(2)),
                };
                _chipStyle.normal.textColor  = ColMuted;
                _chipStyle.normal.background = MakeTex(new Color(0.22f, 0.22f, 0.28f, 1f));
            }
            return _chipStyle;
        }
    }

    private static GUIStyle _tagStyle;
    private static int      _cachedTagFontSize;

    /// <summary>Compact clickable tag button (ON/OFF toggle, condition count).</summary>
    public static GUIStyle TagStyle
    {
        get
        {
            int fs = FontSize(12);
            if (_tagStyle == null || _cachedTagFontSize != fs)
            {
                _cachedTagFontSize = fs;
                _tagStyle = new GUIStyle(UnityEngine.GUI.skin.button)
                {
                    fontSize = fs,
                    padding  = new RectOffset(ScaledInt(6), ScaledInt(6), ScaledInt(2), ScaledInt(2)),
                };
            }
            return _tagStyle;
        }
    }

    private static GUIStyle _mutedLabelStyle;
    private static int      _cachedMutedFontSize;

    /// <summary>Secondary text in a muted colour (author, description, footprint).</summary>
    public static GUIStyle MutedLabelStyle
    {
        get
        {
            int fs = FontSize(12);
            if (_mutedLabelStyle == null || _cachedMutedFontSize != fs)
            {
                _cachedMutedFontSize = fs;
                _mutedLabelStyle = new GUIStyle(UnityEngine.GUI.skin.label) { fontSize = fs };
                _mutedLabelStyle.normal.textColor = ColMuted;
            }
            return _mutedLabelStyle;
        }
    }

    private static GUIStyle _sectionHeaderStyle;
    private static int      _cachedSectionFontSize;

    /// <summary>Bold accent-coloured section divider heading.</summary>
    public static GUIStyle SectionHeaderStyle
    {
        get
        {
            int fs = FontSize(12);
            if (_sectionHeaderStyle == null || _cachedSectionFontSize != fs)
            {
                _cachedSectionFontSize = fs;
                _sectionHeaderStyle = new GUIStyle(UnityEngine.GUI.skin.label)
                {
                    fontSize  = fs,
                    fontStyle = FontStyle.Bold,
                };
                _sectionHeaderStyle.normal.textColor = ColAccent;
            }
            return _sectionHeaderStyle;
        }
    }

    // ============================================================================
    // SCALING
    // ============================================================================

    public static float Scale
    {
        get
        {
            if (!cachedScale.HasValue || lastScreenHeight != Screen.height)
            {
                lastScreenHeight = Screen.height;
                cachedScale = Mathf.Clamp(Screen.height / BaseHeight, MinScale, MaxScale);
            }

            return cachedScale.Value;
        }
    }

    public static float Scaled(float value) => value * Scale;

    public static int ScaledInt(int value) => Mathf.RoundToInt(value * Scale);

    public static Rect ScaledRect(float x, float y, float width, float height)
    {
        return new Rect(
            Scaled(x),
            Scaled(y),
            Scaled(width),
            Scaled(height)
        );
    }

    public static Rect ScaledRectFromRight(float rightMargin, float y, float width, float height)
    {
        return new Rect(
            Screen.width - Scaled(rightMargin + width),
            Scaled(y),
            Scaled(width),
            Scaled(height)
        );
    }

    public static GUILayoutOption[] WindowLayout(float minWidth = 300, float minHeight = 200)
    {
        return new[]
        {
            GUILayout.MinWidth(Scaled(minWidth)),
            GUILayout.MinHeight(Scaled(minHeight))
        };
    }

    public static int FontSize(int baseSize = 14) => ScaledInt(baseSize);

    public static Rect DragRect => new Rect(0, 0, 10000, Scaled(24));

    public static void ApplyScaledSkin()
    {
        // Intentionally left as a no-op for compatibility.
    }

    public static void Space(float basePixels = 10)
    {
        GUILayout.Space(Scaled(basePixels));
    }

    public static GUILayoutOption Width(float baseWidth)
    {
        return GUILayout.Width(Scaled(baseWidth));
    }

    /// <summary>Alias for Width(). Prefer Width() in new code.</summary>
    public static GUILayoutOption LabelWidth(float baseWidth) => Width(baseWidth);

    public static GUILayoutOption Height(float baseHeight)
    {
        return GUILayout.Height(Scaled(baseHeight));
    }

    // ============================================================================
    // TEXT INPUT CAPTURE
    // ============================================================================

    /// <summary>
    /// True when any Patchwork IMGUI text field/area is actively capturing keyboard input.
    /// Check this from Update() to suppress gameplay input while typing.
    /// </summary>
    public static bool IsTextFieldFocused { get; private set; }

    /// <summary>
    /// Name of the currently active Patchwork text control, if any.
    /// </summary>
    public static string ActiveTextControlName { get; private set; }

    /// <summary>
    /// Internal control ID of the currently active Patchwork text control.
    /// </summary>
    private static int _activeKeyboardControl;

    /// <summary>
    /// If set, this control will be focused during the next OnGUI pass.
    /// Useful when opening a window and wanting the search box ready immediately.
    /// </summary>
    private static string _pendingFocusControlName;

    /// <summary>
    /// Call once at the beginning of the plugin's OnGUI.
    /// Rebuilds focus state from IMGUI's current control state.
    /// </summary>
    public static void BeginOnGUI()
    {
        // Clear any stale tooltip from the Layout pass.  Unity sets GUI.tooltip during
        // Layout-event control processing and doesn't always reset it before Repaint.
        // If the mouse left a control between Layout and Repaint we'd draw a phantom
        // tooltip.  Clearing here lets the Repaint pass re-derive the correct value.
        if (Event.current.type == EventType.Repaint)
            UnityEngine.GUI.tooltip = "";

        if (!string.IsNullOrEmpty(_pendingFocusControlName))
        {
            UnityEngine.GUI.FocusControl(_pendingFocusControlName);
            _pendingFocusControlName = null;
        }

        string focusedName = UnityEngine.GUI.GetNameOfFocusedControl();
        int keyboardControl = GUIUtility.keyboardControl;

        bool stillFocused =
            !string.IsNullOrEmpty(ActiveTextControlName) &&
            focusedName == ActiveTextControlName &&
            keyboardControl != 0 &&
            keyboardControl == _activeKeyboardControl;

        if (!stillFocused)
        {
            IsTextFieldFocused = false;
            ActiveTextControlName = null;
            _activeKeyboardControl = 0;
        }
    }

    /// <summary>
    /// Optional end-of-OnGUI cleanup hook. Safe to call but not strictly required.
    /// </summary>
    public static void EndOnGUI()
    {
        DrawTooltip();
    }

    // ── Cursor ───────────────────────────────────────────────────────────────
    // Call InputHandler.SetCursorVisible(true) directly from LateUpdate rather
    // than patching it.  Patching the same method DebugMod patches caused its
    // "Always Show Cursor" option to break (Harmony patch-chain corruption from
    // the ref parameter name mismatch).  Calling the method instead means
    // DebugMod's own prefix fires on our call — both mods request visibility
    // independently and neither interferes with the other.

    private static System.Reflection.MethodInfo _setCursorVisibleMethod;
    private static object                        _inputHandlerInstance;

    public static void InitCursor()
    {
        _setCursorVisibleMethod = AccessTools.Method(typeof(InputHandler), "SetCursorVisible");
        if (_setCursorVisibleMethod == null)
            Plugin.Logger.LogWarning("[GUIHelper] InputHandler.SetCursorVisible not found — cursor call skipped");
    }

    public static void ShowCursorForWindow()
    {
        if (_setCursorVisibleMethod == null) return;

        // Cache the InputHandler instance on first use (singleton, stable after scene load).
        if (_inputHandlerInstance == null || _inputHandlerInstance.Equals(null))
            _inputHandlerInstance = UnityEngine.Object.FindAnyObjectByType<InputHandler>();
        if (_inputHandlerInstance == null) return;

        _setCursorVisibleMethod.Invoke(_inputHandlerInstance, new object[] { true });
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────
    // The game aggressively re-hides and re-locks the hardware cursor every frame
    // via InputSystem callbacks that fire even after LateUpdate.  Drawing a software
    // cursor in OnGUI/Repaint is the only reliable solution: it runs every render
    // frame and cannot be overridden by game code.
    // ── Tooltip ──────────────────────────────────────────────────────────────

    private static GUIStyle _tooltipStyle;
    private static int      _cachedTooltipFontSize;

    /// <summary>
    /// Draws the tooltip for whatever control the mouse is over, if any.
    /// Uses <see cref="GUI.tooltip"/> which IMGUI sets automatically when a
    /// control was created with <c>new GUIContent(label, tooltip)</c>.
    /// Must be called at the end of OnGUI so it renders on top of everything.
    /// </summary>
    public static void DrawTooltip()
    {
        // GUI.tooltip is only populated during Repaint; empty on other events.
        if (string.IsNullOrEmpty(UnityEngine.GUI.tooltip)) return;
        if (Event.current.type != EventType.Repaint) return;

        int ttFontSize = FontSize(12);
        if (_tooltipStyle == null || _cachedTooltipFontSize != ttFontSize)
        {
            _cachedTooltipFontSize = ttFontSize;
            _tooltipStyle = new GUIStyle(UnityEngine.GUI.skin.box)
            {
                fontSize  = ttFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = true,
                padding   = new RectOffset(ScaledInt(6), ScaledInt(6), ScaledInt(4), ScaledInt(4)),
            };
            _tooltipStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        }

        float maxW   = Scaled(260f);
        float mouseX = Event.current.mousePosition.x;
        float mouseY = Event.current.mousePosition.y;

        // Measure before drawing
        var content = new GUIContent(UnityEngine.GUI.tooltip);
        float h = _tooltipStyle.CalcHeight(content, maxW);

        // Offset right-and-below the cursor; flip left/up when near screen edge
        float offX = Scaled(14f);
        float offY = Scaled(18f);
        float x = mouseX + offX;
        float y = mouseY + offY;
        if (x + maxW > Screen.width)  x = mouseX - maxW - offX;
        if (y + h    > Screen.height) y = mouseY - h   - offY;

        // Semi-transparent background layer, then the styled box
        var bgRect = new Rect(x, y, maxW, h);
        UnityEngine.GUI.color = new Color(0f, 0f, 0f, 0.72f);
        UnityEngine.GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
        UnityEngine.GUI.color = Color.white;
        UnityEngine.GUI.Box(bgRect, content, _tooltipStyle);
    }

    /// <summary>
    /// Convenience shorthand: <c>new GUIContent(label, tooltip)</c>.
    /// Use this instead of a bare string wherever a tooltip is useful.
    /// </summary>
    public static GUIContent TT(string label, string tooltip)
        => new GUIContent(label, tooltip);

    /// <summary>
    /// Focus a named Patchwork text control on the next OnGUI pass.
    /// </summary>
    public static void RequestFocus(string controlName)
    {
        if (string.IsNullOrEmpty(controlName))
            return;

        _pendingFocusControlName = controlName;
    }

    /// <summary>
    /// Explicitly release Patchwork text capture.
    /// Useful on window close or Escape.
    /// </summary>
    public static void ReleaseFocus()
    {
        if (!string.IsNullOrEmpty(ActiveTextControlName))
        {
            UnityEngine.GUI.FocusControl(string.Empty);
        }

        IsTextFieldFocused = false;
        ActiveTextControlName = null;
        _activeKeyboardControl = 0;
        _pendingFocusControlName = null;
    }

    /// <summary>
    /// Manual signal for custom controls that cannot use GUIHelper.TextField/TextArea directly.
    /// </summary>
    public static void NotifyTextFieldFocused(string controlName)
    {
        ActiveTextControlName = controlName;
        _activeKeyboardControl = GUIUtility.keyboardControl;
        IsTextFieldFocused = true;

        if (Event.current != null && Event.current.isKey)
        {
            Event.current.Use();
        }
    }

    /// <summary>
    /// Backward-compatible overload for existing callers.
    /// Avoid this for new code; use the named overload instead.
    /// </summary>
    public static string TextField(string text, params GUILayoutOption[] options)
    {
        return TextField("Patchwork.TextField.Default", text, options);
    }

    public static string TextField(string controlName, string text, params GUILayoutOption[] options)
    {
        UnityEngine.GUI.SetNextControlName(controlName);
        string result = GUILayout.TextField(text, TextFieldStyle, options);

        if (UnityEngine.GUI.GetNameOfFocusedControl() == controlName)
        {
            ActiveTextControlName = controlName;
            _activeKeyboardControl = GUIUtility.keyboardControl;
            IsTextFieldFocused = true;

            if (Event.current != null && Event.current.isKey)
            {
                Event.current.Use();
            }
        }

        return result;
    }

    /// <summary>
    /// Backward-compatible overload for existing callers.
    /// Avoid this for new code; use the named overload instead.
    /// </summary>
    public static string TextArea(string text, params GUILayoutOption[] options)
    {
        return TextArea("Patchwork.TextArea.Default", text, options);
    }

    public static string TextArea(string controlName, string text, params GUILayoutOption[] options)
    {
        UnityEngine.GUI.SetNextControlName(controlName);
        string result = GUILayout.TextArea(text, TextFieldStyle, options);

        if (UnityEngine.GUI.GetNameOfFocusedControl() == controlName)
        {
            ActiveTextControlName = controlName;
            _activeKeyboardControl = GUIUtility.keyboardControl;
            IsTextFieldFocused = true;

            if (Event.current != null && Event.current.isKey)
            {
                Event.current.Use();
            }
        }

        return result;
    }

    // ============================================================================
    // GAME INPUT BLOCKING - UNITY INPUT SYSTEM
    // ============================================================================

    private static bool _inputSystemAvailable;
    private static bool _actionsDisabled;

    private static PropertyInfo _systemActionsProp;
    private static PropertyInfo _assetActionMapsProp;
    private static PropertyInfo _mapEnabledProp;
    private static MethodInfo _mapDisableMethod;
    private static MethodInfo _mapEnableMethod;

    private static readonly List<object> _disabledMaps = new();

    public static void InitInputBlocking()
    {
        try
        {
            var inputSystemType = AccessTools.TypeByName("UnityEngine.InputSystem.InputSystem");
            var assetType = AccessTools.TypeByName("UnityEngine.InputSystem.InputActionAsset");
            var mapType = AccessTools.TypeByName("UnityEngine.InputSystem.InputActionMap");

            if (inputSystemType != null && assetType != null && mapType != null)
            {
                _systemActionsProp = inputSystemType.GetProperty(
                    "actions",
                    BindingFlags.Public | BindingFlags.Static
                );

                _assetActionMapsProp = assetType.GetProperty("actionMaps");
                _mapEnabledProp = mapType.GetProperty("enabled");
                _mapDisableMethod = mapType.GetMethod("Disable", Type.EmptyTypes);
                _mapEnableMethod = mapType.GetMethod("Enable", Type.EmptyTypes);
            }

            bool hasAssetPath =
                _systemActionsProp != null &&
                _assetActionMapsProp != null &&
                _mapEnabledProp != null &&
                _mapDisableMethod != null &&
                _mapEnableMethod != null;

            if (!hasAssetPath)
            {
                Plugin.Logger.LogWarning("[InputBlock] No usable Input System path found.");
                return;
            }

            _inputSystemAvailable = true;
            Plugin.Logger.LogInfo($"[InputBlock] Ready (asset path: {hasAssetPath})");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[InputBlock] Init failed: {ex}");
        }
    }

    /// <summary>
    /// Call once per frame from Update().
    /// </summary>
    public static void UpdateInputBlocking()
    {
        if (!_inputSystemAvailable)
            return;

        try
        {
            if (IsTextFieldFocused && !_actionsDisabled)
            {
                DisableGameActions();
                _actionsDisabled = true;
            }
            else if (!IsTextFieldFocused && _actionsDisabled)
            {
                RestoreGameActions();
                _actionsDisabled = false;
            }
        }
        catch (Exception ex)
        {
            ForceRestoreGameActions();
            Plugin.Logger.LogWarning($"[InputBlock] Update failed: {ex}");
        }
    }

    private static void DisableGameActions()
    {
        _disabledMaps.Clear();

        // Disable InputSystem.actions maps so game hotkeys don't fire while typing.
        if (_systemActionsProp != null &&
            _assetActionMapsProp != null &&
            _mapEnabledProp != null &&
            _mapDisableMethod != null)
        {
            object asset = _systemActionsProp.GetValue(null);

            if (asset != null)
            {
                var maps = _assetActionMapsProp.GetValue(asset) as IEnumerable;
                if (maps != null)
                {
                    foreach (object map in maps)
                    {
                        bool enabled = false;

                        try
                        {
                            enabled = (bool)_mapEnabledProp.GetValue(map);
                        }
                        catch
                        {
                            // Ignore broken map reflection.
                        }

                        if (!enabled)
                            continue;

                        try
                        {
                            _mapDisableMethod.Invoke(map, null);
                            _disabledMaps.Add(map);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger.LogDebug($"[InputBlock] Failed to disable map: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Note: we intentionally do NOT disable PlayerInput components here.
        // Toggling PlayerInput.enabled triggers OnDisable/OnEnable lifecycle events
        // that can corrupt Input System device bindings (including controller touchpad
        // mappings). The InputActionMap approach above is sufficient to block gameplay
        // hotkeys while a text field is focused.
    }

    private static void RestoreGameActions()
    {
        if (_mapEnableMethod != null)
        {
            foreach (object map in _disabledMaps)
            {
                try
                {
                    _mapEnableMethod.Invoke(map, null);
                }
                catch
                {
                    // Map may have been destroyed.
                }
            }
        }

        _disabledMaps.Clear();
    }

    /// <summary>
    /// Emergency restore; call on plugin destroy / unload.
    /// </summary>
    public static void ForceRestoreGameActions()
    {
        if (!_actionsDisabled)
            return;

        try
        {
            RestoreGameActions();
        }
        catch
        {
            // Best effort.
        }

        _actionsDisabled = false;
    }
}
