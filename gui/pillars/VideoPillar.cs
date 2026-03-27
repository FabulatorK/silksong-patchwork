using System.Linq;
using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI.Pillars;

/// <summary>
/// Dev Hub — Video tab.
/// Stub: activates when VideoHandler has content.
/// Follows the same table pattern as GraphicsPillar when active.
/// </summary>
public static class VideoPillar
{
    private static Vector2 _scroll;

    public static void Draw()
    {
        var videos = VideoHandler.VideoFileMap.Where(kv => kv.Value != null).ToList();

        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label($"Video replacements: {videos.Count}", GUIHelper.LabelStyle);
        UnityEngine.GUI.contentColor = Color.white;
        GUIHelper.Space(6);

        if (videos.Count == 0)
        {
            UnityEngine.GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label(
                "No video replacements loaded.\nPlace .mp4 / .webm / .ogv files in the pack Videos folder.",
                GUIHelper.LabelStyle);
            UnityEngine.GUI.contentColor = Color.white;
            return;
        }

        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.BeginHorizontal();
        UnityEngine.GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label("Name",   GUIHelper.LabelStyle, GUIHelper.Width(200));
        GUILayout.Label("Status", GUIHelper.LabelStyle, GUIHelper.Width(80));
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndHorizontal();

        foreach (var kv in videos)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(kv.Key, GUIHelper.LabelStyle, GUIHelper.Width(200));
            UnityEngine.GUI.contentColor = Color.green;
            GUILayout.Label("\u25CF loaded", GUIHelper.LabelStyle, GUIHelper.Width(80));
            UnityEngine.GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }
}
