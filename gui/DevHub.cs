using Patchwork.GUI.Pillars;
using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Tabbed Dev Hub window for asset creators.
/// Five pillars: Graphics, Audio, Text, Performance, Video.
/// Opened via Plugin.ShowDevHub; tab selected via Plugin.DevHubTab.
/// </summary>
public static class DevHub
{
    public const int TabGraphics    = 0;
    public const int TabAudio       = 1;
    public const int TabText        = 2;
    public const int TabPerformance = 3;
    public const int TabVideo       = 4;

    private static readonly string[] TabNames = { "Graphics", "Audio", "Text", "Performance", "Video" };

    private const float WindowWidth  = 720f;
    private const float WindowHeight = 620f;

    private static Rect _windowRect;
    private static bool _initialized;

    public static void Draw()
    {
        if (!_initialized || _windowRect.width < 1)
        {
            _windowRect = GUIHelper.ScaledRect(30, 30, WindowWidth, WindowHeight);
            _initialized = true;
        }

        _windowRect = GUILayout.Window(
            6980,
            _windowRect,
            DrawWindow,
            "Patchwork Dev Hub",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    private static void DrawWindow(int windowID)
    {
        GUIHelper.Space(16);

        // ── Tab bar ──────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool active = Plugin.DevHubTab == i;
            Color prev  = UnityEngine.GUI.backgroundColor;
            if (active) UnityEngine.GUI.backgroundColor = new Color(0.25f, 0.55f, 1f);

            if (GUILayout.Button(TabNames[i], GUIHelper.ButtonStyle, GUIHelper.Height(28)))
                Plugin.DevHubTab = i;

            UnityEngine.GUI.backgroundColor = prev;
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("×", GUIHelper.ButtonStyle, GUIHelper.Width(28), GUIHelper.Height(28)))
            Plugin.ShowDevHub = false;
        GUILayout.EndHorizontal();
        GUIHelper.Space(6);

        // ── Dispatch to active pillar ─────────────────────────────────────────
        switch (Plugin.DevHubTab)
        {
            case TabGraphics:    GraphicsPillar.Draw();    break;
            case TabAudio:       AudioPillar.Draw();       break;
            case TabText:        TextPillar.Draw();        break;
            case TabPerformance: PerformancePillar.Draw(); break;
            case TabVideo:       VideoPillar.Draw();       break;
        }

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    /// <summary>Open Dev Hub at a specific tab.</summary>
    public static void OpenAt(int tab)
    {
        Plugin.ShowDevHub = true;
        Plugin.DevHubTab  = tab;
    }

    /// <summary>
    /// Open at <paramref name="tab"/>, switch to it if already open on another tab,
    /// or close if already open on that tab (keybind toggle behaviour).
    /// </summary>
    public static void ToggleAt(int tab)
    {
        if (Plugin.ShowDevHub && Plugin.DevHubTab == tab)
            Plugin.ShowDevHub = false;
        else
            OpenAt(tab);
    }
}
