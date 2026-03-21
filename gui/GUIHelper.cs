using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
    /// <summary>
    /// True when a GUI text field or text area has keyboard focus. Check this in Update()
    /// to suppress game input while the user is typing in a search bar or editor.
    /// Reset each OnGUI frame and set by TextField() or NotifyTextFieldFocused().
    /// </summary>
    public static bool IsTextFieldFocused { get; private set; }

    /// <summary>
    /// Manually signal that a text input control is focused this frame.
    /// Use for controls that can't go through GUIHelper.TextField() (e.g. TextArea).
    /// </summary>
    public static void NotifyTextFieldFocused()
    {
        IsTextFieldFocused = true;
    }

    /// <summary>
    /// Call at the start of OnGUI to reset focus tracking for this frame.
    /// </summary>
    public static void BeginOnGUI()
    {
        IsTextFieldFocused = GUIUtility.keyboardControl != 0 && _lastActiveTextField != 0
            && GUIUtility.keyboardControl == _lastActiveTextField;
    }

    private static int _lastActiveTextField;

    /// <summary>
    /// Wraps GUILayout.TextField with automatic focus tracking.
    /// When the returned field has keyboard focus, IsTextFieldFocused is set to true
    /// and keyboard events are consumed to prevent them reaching the game.
    /// </summary>
    public static string TextField(string text, params GUILayoutOption[] options)
    {
        // Give the next control a known name so we can check its ID
        string controlName = "PatchworkTextField";
        UnityEngine.GUI.SetNextControlName(controlName);
        string result = GUILayout.TextField(text, TextFieldStyle, options);

        // Check if this text field is focused
        if (UnityEngine.GUI.GetNameOfFocusedControl() == controlName)
        {
            _lastActiveTextField = GUIUtility.keyboardControl;
            IsTextFieldFocused = true;

            // Consume keyboard events so they don't reach the game
            if (Event.current != null && Event.current.isKey)
                Event.current.Use();
        }

        return result;
    }

    /// <summary>
    /// Wraps GUILayout.TextArea with automatic focus tracking.
    /// Same input blocking behavior as TextField() but for multi-line editing.
    /// </summary>
    public static string TextArea(string text, params GUILayoutOption[] options)
    {
        string controlName = "PatchworkTextArea";
        UnityEngine.GUI.SetNextControlName(controlName);
        string result = GUILayout.TextArea(text, TextFieldStyle, options);

        if (UnityEngine.GUI.GetNameOfFocusedControl() == controlName)
        {
            _lastActiveTextField = GUIUtility.keyboardControl;
            IsTextFieldFocused = true;

            if (Event.current != null && Event.current.isKey)
                Event.current.Use();
        }

        return result;
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

    #region Game Input Blocking — New Input System (Action Maps)
    // The game uses Unity's new Input System (since patch 1.0.29242).
    // When a Patchwork IMGUI text field is focused, we disable the game's
    // InputActionMaps so the game's bound actions stop firing. The keyboard
    // DEVICE stays active — IMGUI text fields keep receiving key events
    // because they read from the keyboard device / native event pipeline,
    // not from InputActions.
    //
    // Previous approaches that failed:
    //   1. Event.current.Use() — IMGUI only, game doesn't read IMGUI events
    //   2. Input.ResetInputAxes() — game reads from new Input System, not legacy Input
    //   3. Disable GameManager input MonoBehaviour — game input isn't one MonoBehaviour
    //   4. Harmony postfix on Input.GetKey* — extern methods; game doesn't use legacy Input
    //   5. InputSystem.DisableDevice(Keyboard.current) — disables IMGUI text input too
    //      because Unity 6 routes ALL keyboard events through the Input System

    private static bool _inputSystemAvailable;
    private static bool _actionsDisabled;

    // Reflection handles for InputSystem.actions → InputActionAsset → actionMaps
    private static PropertyInfo _systemActionsProp;
    private static PropertyInfo _assetActionMapsProp;
    private static PropertyInfo _mapEnabledProp;
    private static MethodInfo _mapDisableMethod;
    private static MethodInfo _mapEnableMethod;

    // Fallback: toggle PlayerInput MonoBehaviours
    private static Type _playerInputType;

    // Track what we disabled so we restore exactly the right state
    private static readonly List<object> _disabledMaps = new();
    private static readonly List<MonoBehaviour> _disabledPlayerInputs = new();

    /// <summary>
    /// Discovers the Input System types via reflection. Call once during plugin startup.
    /// </summary>
    public static void InitInputBlocking()
    {
        try
        {
            var inputSystemType = AccessTools.TypeByName("UnityEngine.InputSystem.InputSystem");
            var assetType = AccessTools.TypeByName("UnityEngine.InputSystem.InputActionAsset");
            var mapType = AccessTools.TypeByName("UnityEngine.InputSystem.InputActionMap");

            if (inputSystemType != null && assetType != null && mapType != null)
            {
                _systemActionsProp = inputSystemType.GetProperty("actions", BindingFlags.Public | BindingFlags.Static);
                _assetActionMapsProp = assetType.GetProperty("actionMaps");
                _mapEnabledProp = mapType.GetProperty("enabled");
                _mapDisableMethod = mapType.GetMethod("Disable");
                _mapEnableMethod = mapType.GetMethod("Enable");
            }

            _playerInputType = AccessTools.TypeByName("UnityEngine.InputSystem.PlayerInput");

            bool hasAssetPath = _systemActionsProp != null
                && _assetActionMapsProp != null
                && _mapEnabledProp != null
                && _mapDisableMethod != null
                && _mapEnableMethod != null;

            bool hasPlayerInputPath = _playerInputType != null;

            if (!hasAssetPath && !hasPlayerInputPath)
            {
                Plugin.Logger.LogWarning("[InputBlock] Could not resolve Input System action map or PlayerInput types — input blocking unavailable");
                return;
            }

            _inputSystemAvailable = true;
            Plugin.Logger.LogInfo($"[InputBlock] Input blocking ready (asset path: {hasAssetPath}, PlayerInput path: {hasPlayerInputPath})");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[InputBlock] Init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disables or re-enables game InputActionMaps based on IsTextFieldFocused.
    /// Call once per frame from Update().
    /// </summary>
    public static void UpdateInputBlocking()
    {
        if (!_inputSystemAvailable) return;

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
            Plugin.Logger.LogWarning($"[InputBlock] Error during update: {ex.Message}");
        }
    }

    private static void DisableGameActions()
    {
        _disabledMaps.Clear();
        _disabledPlayerInputs.Clear();

        // Strategy 1: Disable action maps via InputSystem.actions (project-wide asset)
        if (_systemActionsProp != null)
        {
            var asset = _systemActionsProp.GetValue(null);
            if (asset != null)
            {
                var maps = (IEnumerable)_assetActionMapsProp.GetValue(asset);
                foreach (var map in maps)
                {
                    if ((bool)_mapEnabledProp.GetValue(map))
                    {
                        _mapDisableMethod.Invoke(map, null);
                        _disabledMaps.Add(map);
                    }
                }
            }
        }

        // Strategy 2: Also disable PlayerInput components (they may manage
        // their own InputActionAsset separate from InputSystem.actions)
        if (_playerInputType != null)
        {
            var allPI = UnityEngine.Object.FindObjectsByType(_playerInputType, FindObjectsSortMode.None);
            foreach (var obj in allPI)
            {
                var mb = (MonoBehaviour)obj;
                if (mb != null && mb.enabled)
                {
                    mb.enabled = false;
                    _disabledPlayerInputs.Add(mb);
                }
            }
        }
    }

    private static void RestoreGameActions()
    {
        // Re-enable action maps that were disabled
        foreach (var map in _disabledMaps)
        {
            try { _mapEnableMethod.Invoke(map, null); }
            catch { /* map may have been destroyed */ }
        }
        _disabledMaps.Clear();

        // Re-enable PlayerInput components
        foreach (var mb in _disabledPlayerInputs)
        {
            try { if (mb != null) mb.enabled = true; }
            catch { /* object may have been destroyed */ }
        }
        _disabledPlayerInputs.Clear();
    }

    /// <summary>
    /// Emergency restore. Call on plugin destroy to avoid leaving game input stuck.
    /// </summary>
    public static void ForceRestoreGameActions()
    {
        if (!_actionsDisabled) return;

        try { RestoreGameActions(); }
        catch { /* best effort */ }

        _actionsDisabled = false;
    }
    #endregion
}