using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Graphics tab.
/// Sub-tabs: Animation (tk2d frame inspector) | T2D Textures (T2D browser + editor).
/// Cross-asset file overview lives in DashboardPillar.
/// </summary>
public static class GraphicsPillar
{
    private static int    _tab = 0;
    private static Vector2 _animScroll;

    private const int TabAnimation = 0;
    private const int TabT2D       = 1;

    public static void Draw()
    {
        // Sub-tab strip
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_tab == TabAnimation, "Animation",    GUIHelper.ButtonStyle)) _tab = TabAnimation;
        if (GUILayout.Toggle(_tab == TabT2D,       "T2D Textures", GUIHelper.ButtonStyle)) _tab = TabT2D;
        GUILayout.EndHorizontal();
        GUIHelper.Space(4);

        if (_tab == TabAnimation)
        {
            _animScroll = GUILayout.BeginScrollView(_animScroll);
            AnimationController.DrawPillarContent();
            GUILayout.EndScrollView();
        }
        else
        {
            T2DTextureController.DrawPillarContent();
        }
    }
}
