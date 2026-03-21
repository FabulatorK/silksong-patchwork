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

    public static bool ReloadSprites = false;
    public static bool ReloadT2DSprites = false;

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
        Plugin.Logger.LogInfo($"[FileWatcher] Sprite file event: {e.ChangeType} — {e.FullPath}");
        string relativePath = Path.GetRelativePath(SpriteLoader.LoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);

        if (pathParts[^2] == "T2D" || (pathParts.Length >= 3 && pathParts[^3] == "T2D"))
        {
            string spriteName = Path.GetFileNameWithoutExtension(pathParts[^1]);
            // Extract atlas name when file is inside T2D/{atlasName}/{sprite}.png
            string atlasName = (pathParts.Length >= 3 && pathParts[^3] == "T2D") ? pathParts[^2] : null;
            Plugin.Logger.LogInfo($"[FileWatcher] → T2D sprite change detected: atlas='{atlasName}', sprite='{spriteName}', setting ReloadT2DSprites=true");
            T2DHandler.InvalidateCache(spriteName, atlasName);
            ReloadT2DSprites = true;
            return;
        }

        if (pathParts.Length < 3)
        {
            Plugin.Logger.LogInfo($"[FileWatcher] → Path too short ({pathParts.Length} parts), ignoring: {relativePath}");
            return;
        }

        string collectionName = pathParts[^3];
        string tk2dAtlasName = pathParts[^2];
        string spriteName2 = Path.GetFileNameWithoutExtension(pathParts[^1]);

        SpriteLoader.MarkReloadSprite(collectionName, tk2dAtlasName, spriteName2);
        Plugin.Logger.LogInfo($"[FileWatcher] → tk2d sprite change: collection={collectionName}, atlas={tk2dAtlasName}, sprite={spriteName2}, setting ReloadSprites=true");

        ReloadSprites = true;
    }

    private void OnAtlasChanged(object sender, FileSystemEventArgs e)
    {
        Plugin.Logger.LogInfo($"[FileWatcher] Atlas file event: {e.ChangeType} — {e.FullPath}");
        string relativePath = Path.GetRelativePath(SpriteLoader.AtlasLoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        if (pathParts.Length < 2)
        {
            Plugin.Logger.LogInfo($"[FileWatcher] → Atlas path too short ({pathParts.Length} parts), ignoring");
            return;
        }

        // T2D spritesheets: Spritesheets/T2D/{TextureName}.png
        if (pathParts[^2] == "T2D")
        {
            string cleanTexName = Path.GetFileNameWithoutExtension(pathParts[^1]);
            T2DHandler.InvalidateSpritesheet(cleanTexName);
            Plugin.Logger.LogInfo($"[FileWatcher] → T2D spritesheet change: '{cleanTexName}', setting ReloadT2DSprites=true");
            ReloadT2DSprites = true;
            return;
        }

        string collectionName = pathParts[^2];
        string atlasName = Path.GetFileNameWithoutExtension(pathParts[^1]);

        SpriteLoader.MarkReloadAtlas(collectionName, atlasName);
        Plugin.Logger.LogInfo($"[FileWatcher] → tk2d atlas change: collection={collectionName}, atlas={atlasName}, setting ReloadSprites=true");

        ReloadSprites = true;
    }
}