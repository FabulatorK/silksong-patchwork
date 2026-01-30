using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Shared UI scaling and styling utilities for Patchwork windows.
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
    private static GUIStyle _boxStyle;
    private static int _cachedFontSize;

    /// <summary>
    /// Scaled label style - use instead of default
    /// </summary>
    public static GUIStyle LabelStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_labelStyle == null || _cachedFontSize != fontSize)
            {
                _labelStyle = new GUIStyle(UnityEngine.GUI.skin.label) { fontSize = fontSize };
                _cachedFontSize = fontSize;
            }
            return _labelStyle;
        }
    }

    /// <summary>
    /// Scaled button style
    /// </summary>
    public static GUIStyle ButtonStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_buttonStyle == null || _buttonStyle.fontSize != fontSize)
                _buttonStyle = new GUIStyle(UnityEngine.GUI.skin.button) { fontSize = fontSize };
            return _buttonStyle;
        }
    }

    /// <summary>
    /// Scaled toggle style
    /// </summary>
    public static GUIStyle ToggleStyle
    {
        get
        {
            int fontSize = FontSize(14);
            if (_toggleStyle == null || _toggleStyle.fontSize != fontSize)
                _toggleStyle = new GUIStyle(UnityEngine.GUI.skin.toggle) { fontSize = fontSize };
            return _toggleStyle;
        }
    }

    /// <summary>
    /// Scaled window style with proper title bar
    /// </summary>
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
                    
                    // No highlight on focus
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

    /// <summary>
    /// Current UI scale factor based on screen height.
    /// Recalculates if resolution changes.
    /// </summary>
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

    /// <summary>
    /// Scale a value from 1080p base to current resolution.
    /// </summary>
    public static float Scaled(float value) => value * Scale;

    /// <summary>
    /// Scale an integer value.
    /// </summary>
    public static int ScaledInt(int value) => Mathf.RoundToInt(value * Scale);

    /// <summary>
    /// Create a scaled Rect positioned from top-left.
    /// </summary>
    public static Rect ScaledRect(float x, float y, float width, float height)
    {
        return new Rect(
            Scaled(x),
            Scaled(y),
            Scaled(width),
            Scaled(height)
        );
    }

    /// <summary>
    /// Create a scaled Rect positioned from top-right.
    /// </summary>
    public static Rect ScaledRectFromRight(float rightMargin, float y, float width, float height)
    {
        return new Rect(
            Screen.width - Scaled(rightMargin + width),
            Scaled(y),
            Scaled(width),
            Scaled(height)
        );
    }

    /// <summary>
    /// Default window options with scaled dimensions.
    /// </summary>
    public static GUILayoutOption[] WindowLayout(float minWidth = 300, float minHeight = 200)
    {
        return new GUILayoutOption[]
        {
            GUILayout.MinWidth(Scaled(minWidth)),
            GUILayout.MinHeight(Scaled(minHeight))
        };
    }

    /// <summary>
    /// Scaled font size for labels.
    /// </summary>
    public static int FontSize(int baseSize = 14) => ScaledInt(baseSize);

    /// <summary>
    /// Standard draggable area for window title bars.
    /// </summary>
    public static Rect DragRect => new Rect(0, 0, 10000, Scaled(24));

    /// <summary>
    /// Deprecated - use LabelStyle, ButtonStyle etc. directly instead.
    /// Kept for compatibility but does nothing now.
    /// </summary>
    public static void ApplyScaledSkin()
    {
        // No longer modifies global skin - use GUIHelper styles instead
    }

    /// <summary>
    /// Scaled GUILayout.Space
    /// </summary>
    public static void Space(float basePixels = 10)
    {
        GUILayout.Space(Scaled(basePixels));
    }

    /// <summary>
    /// Scaled fixed-width label option
    /// </summary>
    public static GUILayoutOption LabelWidth(float baseWidth)
    {
        return GUILayout.Width(Scaled(baseWidth));
    }

    /// <summary>
    /// Scaled fixed-height option
    /// </summary>
    public static GUILayoutOption Height(float baseHeight)
    {
        return GUILayout.Height(Scaled(baseHeight));
    }
    private static GUIStyle _textFieldStyle;

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
                    padding = new RectOffset(ScaledInt(6), ScaledInt(6), ScaledInt(4), ScaledInt(4))
                };
            }
            return _textFieldStyle;
        }
    }
}