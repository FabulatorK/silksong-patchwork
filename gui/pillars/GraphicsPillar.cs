using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Graphics tab.
/// Shows the Animation Controller frame inspector.
/// Cross-asset file overview lives in DashboardPillar.
/// </summary>
public static class GraphicsPillar
{
    private static Vector2 _scroll;

    public static void Draw()
    {
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Animation Controller", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(4);

        _scroll = GUILayout.BeginScrollView(_scroll);
        AnimationController.DrawPillarContent();
        GUILayout.EndScrollView();
    }
}
