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
            SpriteLoader.Reload();
        }

        if (SpriteFileWatcher.ReloadT2DSprites)
        {
            Plugin.Logger.LogInfo("[Update] ReloadT2DSprites flag set — triggering T2DHandler.ReloadSpritesInScene()");
            SpriteFileWatcher.ReloadT2DSprites = false;
            T2DLoader.ReloadSpritesInScene();
        }

        if (AudioFileWatcher.ReloadAudio)
        {
            AudioFileWatcher.ReloadAudio = false;
            AudioHandler.Reload();
        }

        if (TextFileWatcher.ReloadText)
        {
            TextFileWatcher.ReloadText = false;
            DialogueHandler.Reload();
        }

        if (VideoHandler.ReloadVideos)
        {
            VideoHandler.ReloadVideos = false;
            VideoHandler.Reload();
        }
    }
}
