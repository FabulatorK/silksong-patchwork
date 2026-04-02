using Patchwork.GUI;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork;

/// <summary>
/// Processes Patchwork hotkeys each frame.  All checks are skipped when a
/// text field is focused to prevent key presses leaking into game input.
/// Called once per frame from Plugin.Update().
/// </summary>
internal static class HotkeyController
{
    internal static void Poll()
    {
        if (GUIHelper.IsTextFieldFocused) return;

        if (Input.GetKeyDown(Plugin.Config.FullDumpKey) && Plugin.Config.DumpSprites)
            SceneTraverser.TraverseAllScenes();

        if (Input.GetKeyDown(Plugin.Config.ShowPackManagerKey))
            Plugin.ShowPackManager = !Plugin.ShowPackManager;

        if (Input.GetKeyDown(Plugin.Config.DevHubDashboardKey))
            DevHub.ToggleAt(DevHub.TabDashboard);
        if (Input.GetKeyDown(Plugin.Config.DevHubGraphicsKey))
            DevHub.ToggleAt(DevHub.TabGraphics);
        if (Input.GetKeyDown(Plugin.Config.DevHubTextKey))
            DevHub.ToggleAt(DevHub.TabText);
        if (Input.GetKeyDown(Plugin.Config.DevHubVideoKey))
            DevHub.ToggleAt(DevHub.TabVideo);
        if (Input.GetKeyDown(Plugin.Config.DevHubAudioKey))
            DevHub.ToggleAt(DevHub.TabAudio);
        if (Input.GetKeyDown(Plugin.Config.DevHubPerformanceKey))
            DevHub.ToggleAt(DevHub.TabPerformance);
    }
}
