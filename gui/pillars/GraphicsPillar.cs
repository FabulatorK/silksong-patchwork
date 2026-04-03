using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Graphics tab.
/// Sub-tabs: Animation (tk2d frame inspector) | T2D Textures (T2D browser + editor) | T2D Log.
/// Cross-asset file overview lives in DashboardPillar.
/// </summary>
public static class GraphicsPillar
{
    private static int    _tab = 0;
    private static Vector2 _animScroll;
    private static Vector2 _logScroll;

    private const int TabAnimation = 0;
    private const int TabT2D       = 1;
    private const int TabT2DLog    = 2;

    /// <summary>Switch to the T2D Browser sub-tab (called from T2D Log on entry click).</summary>
    internal static void SwitchToT2DBrowser() => _tab = TabT2D;

    public static void Draw()
    {
        // Gate trigger logging: true only when a T2D sub-tab is visible.
        // Reset to false each OnGUI pass in Plugin.OnGUI; this re-enables it for this pass.
        T2DLoader.IsT2DLogActive = (_tab == TabT2D || _tab == TabT2DLog);

        // Sub-tab strip
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_tab == TabAnimation, "Animation",    GUIHelper.ButtonStyle)) _tab = TabAnimation;
        if (GUILayout.Toggle(_tab == TabT2D,       "T2D Textures", GUIHelper.ButtonStyle)) _tab = TabT2D;
        if (GUILayout.Toggle(_tab == TabT2DLog,    "T2D Log",      GUIHelper.ButtonStyle)) _tab = TabT2DLog;
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        if (_tab == TabAnimation)
        {
            _animScroll = GUILayout.BeginScrollView(_animScroll);
            AnimationController.DrawPillarContent();
            GUILayout.EndScrollView();
        }
        else if (_tab == TabT2D)
        {
            T2DTextureController.DrawPillarContent();
        }
        else
        {
            DrawT2DLog();
        }
    }

    private static void DrawT2DLog()
    {
        int    maxVisible  = 20;
        double fadeDuration = 8.0;

        // ── Header row ────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"T2D Trigger Log  ({T2DLog.EntryCount} entries)", GUIHelper.LabelStyle,
            GUILayout.ExpandWidth(true));
        UnityEngine.GUI.contentColor = Color.white;
        if (GUILayout.Button("Clear", GUIHelper.ButtonStyle, GUIHelper.Width(50f), GUIHelper.Height(22)))
            T2DLog.ClearLog();
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
        GUILayout.Label("Click an entry to jump to it in T2D Textures.", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(4);

        _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        T2DLog.DrawEntries(maxVisible, fadeDuration, (cleanTex, spriteName) =>
        {
            T2DTextureController.SelectEntry(cleanTex, spriteName);
            SwitchToT2DBrowser();
        });
        GUILayout.EndScrollView();
    }
}
