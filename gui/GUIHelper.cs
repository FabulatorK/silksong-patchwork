
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
        // Intentionally minimal for now.
    }

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

    private static Type _playerInputType;

    private static readonly List<object> _disabledMaps = new();
    private static readonly List<MonoBehaviour> _disabledPlayerInputs = new();

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

            _playerInputType = AccessTools.TypeByName("UnityEngine.InputSystem.PlayerInput");

            bool hasAssetPath =
                _systemActionsProp != null &&
                _assetActionMapsProp != null &&
                _mapEnabledProp != null &&
                _mapDisableMethod != null &&
                _mapEnableMethod != null;

            bool hasPlayerInputPath = _playerInputType != null;

            if (!hasAssetPath && !hasPlayerInputPath)
            {
                Plugin.Logger.LogWarning("[InputBlock] No usable Input System path found.");
                return;
            }

            _inputSystemAvailable = true;
            Plugin.Logger.LogInfo(
                $"[InputBlock] Ready (asset path: {hasAssetPath}, playerInput path: {hasPlayerInputPath})"
            );
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
        _disabledPlayerInputs.Clear();

        // Strategy 1: Disable InputSystem.actions maps, if available.
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

        // Strategy 2: Disable PlayerInput components too, in case the game owns separate assets.
        if (_playerInputType != null)
        {
            var allPlayerInputs = UnityEngine.Object.FindObjectsByType(_playerInputType, FindObjectsSortMode.None);
            foreach (var obj in allPlayerInputs)
            {
                if (obj is MonoBehaviour mb && mb != null && mb.enabled)
                {
                    mb.enabled = false;
                    _disabledPlayerInputs.Add(mb);
                }
            }
        }
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

        foreach (MonoBehaviour mb in _disabledPlayerInputs)
        {
            try
            {
                if (mb != null)
                    mb.enabled = true;
            }
            catch
            {
                // Object may have been destroyed.
            }
        }

        _disabledPlayerInputs.Clear();
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
