using Patchwork.GUI;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Performance tab.
/// Renders the DevProfiler content inside the Dev Hub window (no nested window).
/// </summary>
public static class PerformancePillar
{
    private static Vector2 _scroll;

    public static void Draw()
    {
        _scroll = GUILayout.BeginScrollView(_scroll);
        DevProfiler.DrawPillarContent();
        GUILayout.EndScrollView();
    }
}
