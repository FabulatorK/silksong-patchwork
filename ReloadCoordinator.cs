using Patchwork.GUI;
using Patchwork.Handlers;
using Patchwork.Watchers;

namespace Patchwork;

/// <summary>
/// Polls watcher flags each frame and dispatches to the appropriate handler Reload() method.
/// Called once per frame from Plugin.Update().
/// </summary>
internal static class ReloadCoordinator
{
    internal static void Poll()
    {
        if (SpriteFileWatcher.ReloadSprites)
        {
            Plugin.Logger.LogInfo("[Update] ReloadSprites flag set — triggering SpriteLoader.Reload()");
            SpriteFileWatcher.ReloadSprites = false;
            DevProfiler.StartOp();
            SpriteLoader.Reload();
            float ms = DevProfiler.StopOp("SpriteLoader.Reload");
            Plugin.Logger.LogInfo($"[Reload] SpriteLoader.Reload completed in {ms:F1}ms");
        }

        if (SpriteFileWatcher.ReloadT2DSprites)
        {
            Plugin.Logger.LogInfo("[Update] ReloadT2DSprites flag set — triggering T2DLoader.ReloadSpritesInScene()");
            SpriteFileWatcher.ReloadT2DSprites = false;
            DevProfiler.StartOp();
            T2DLoader.ReloadSpritesInScene();
            float ms = DevProfiler.StopOp("T2DLoader.ReloadSpritesInScene");
            Plugin.Logger.LogInfo($"[Reload] T2DLoader.ReloadSpritesInScene completed in {ms:F1}ms");
        }

        if (AudioFileWatcher.ReloadAudio)
        {
            AudioFileWatcher.ReloadAudio = false;
            DevProfiler.StartOp();
            AudioHandler.Reload();
            DevProfiler.StopOp("AudioHandler.Reload");
        }

        if (TextFileWatcher.ReloadText)
        {
            TextFileWatcher.ReloadText = false;
            DevProfiler.StartOp();
            DialogueHandler.Reload();
            DevProfiler.StopOp("DialogueHandler.Reload");
        }

        if (VideoHandler.ReloadVideos)
        {
            VideoHandler.ReloadVideos = false;
            DevProfiler.StartOp();
            VideoHandler.Reload();
            DevProfiler.StopOp("VideoHandler.Reload");
        }
    }
}
