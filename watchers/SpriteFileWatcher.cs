using System.IO;
using UnityEngine.SceneManagement;

namespace Patchwork.Handlers;

/// <summary>
/// Watches the sprite load directory for changes and invalidates cache entries accordingly.
/// </summary>
public class SpriteFileWatcher
{
    public FileSystemWatcher SpriteWatcher;
    public FileSystemWatcher AtlasWatcher;

    public static bool ReloadScene = false;

    public SpriteFileWatcher()
    {
        SpriteWatcher = new FileSystemWatcher();
        SpriteWatcher.Path = SpriteLoader.LoadPath;
        SpriteWatcher.IncludeSubdirectories = true;
        SpriteWatcher.Filter = "*.png";
        SpriteWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        SpriteWatcher.Changed += OnSpriteChanged;
        SpriteWatcher.Created += OnSpriteChanged;
        SpriteWatcher.Deleted += OnSpriteChanged;
        SpriteWatcher.Renamed += OnSpriteChanged;
        SpriteWatcher.EnableRaisingEvents = true;

        AtlasWatcher = new FileSystemWatcher();
        AtlasWatcher.Path = SpriteLoader.AtlasLoadPath;
        AtlasWatcher.IncludeSubdirectories = true;
        AtlasWatcher.Filter = "*.png";
        AtlasWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        AtlasWatcher.Changed += OnAtlasChanged;
        AtlasWatcher.Created += OnAtlasChanged;
        AtlasWatcher.Deleted += OnAtlasChanged;
        AtlasWatcher.Renamed += OnAtlasChanged;
        AtlasWatcher.EnableRaisingEvents = true;
    }

    private void OnSpriteChanged(object sender, FileSystemEventArgs e)
    {
        string relativePath = Path.GetRelativePath(SpriteLoader.LoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);

        if (pathParts[^2] == "T2D" || (pathParts.Length >= 3 && pathParts[^3] == "T2D"))
        {
            T2DHandler.InvalidateCache(Path.GetFileNameWithoutExtension(pathParts[^1]));
            if (Plugin.Config.ReloadSceneOnChange)
                ReloadScene = true;
            return;
        }

        if (pathParts.Length < 3)
            return;

        string collectionName = pathParts[^3];
        string atlasName = pathParts[^2];
        string spriteName = Path.GetFileNameWithoutExtension(pathParts[^1]);

        SpriteLoader.MarkReloadSprite(collectionName, atlasName, spriteName);
        Plugin.Logger.LogDebug($"Invalidated cache for collection {collectionName}, atlas {atlasName}, sprite {spriteName} due to file change: {e.ChangeType} {e.FullPath}");

        if (Plugin.Config.ReloadSceneOnChange)
            ReloadScene = true;
    }

    private void OnAtlasChanged(object sender, FileSystemEventArgs e)
    {
        string relativePath = Path.GetRelativePath(SpriteLoader.AtlasLoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        if (pathParts.Length < 2)
            return;

        string collectionName = pathParts[^2];
        string atlasName = Path.GetFileNameWithoutExtension(pathParts[^1]);

        SpriteLoader.MarkReloadAtlas(collectionName, atlasName);
        Plugin.Logger.LogDebug($"Invalidated cache for collection {collectionName} due to atlas change: {e.ChangeType} {e.FullPath}");

        if (Plugin.Config.ReloadSceneOnChange)
            ReloadScene = true;
    }
}