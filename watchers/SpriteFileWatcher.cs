using System.IO;
using UnityEngine.SceneManagement;

namespace Patchwork.Handlers;

/// <summary>
/// Watches the sprite load directory for changes and invalidates cache entries accordingly.
/// </summary>
public class SpriteFileWatcher : System.IDisposable
{
    public FileSystemWatcher SpriteWatcher;
    public FileSystemWatcher AtlasWatcher;
    public FileSystemWatcher AnchorWatcher;

    public static volatile bool ReloadSprites;
    public static volatile bool ReloadT2DSprites;

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

        // anchors.txt lives at Sprites/{collName}/{matName}/anchors.txt.
        // A change means the anchor for one or more sprites on that material changed,
        // which affects the canvas expansion plan — treat it as an atlas-level reload.
        AnchorWatcher = new FileSystemWatcher();
        AnchorWatcher.Path = SpriteLoader.LoadPath;
        AnchorWatcher.IncludeSubdirectories = true;
        AnchorWatcher.Filter = "anchors.txt";
        AnchorWatcher.NotifyFilter = NotifyFilters.LastWrite;
        AnchorWatcher.Changed += OnAnchorChanged;
        AnchorWatcher.Created += OnAnchorChanged;
        AnchorWatcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        SpriteWatcher.EnableRaisingEvents = false;
        SpriteWatcher.Dispose();
        AtlasWatcher.EnableRaisingEvents = false;
        AtlasWatcher.Dispose();
        AnchorWatcher.EnableRaisingEvents = false;
        AnchorWatcher.Dispose();
    }

    private void OnSpriteChanged(object sender, FileSystemEventArgs e)
    {
        Plugin.Logger.LogInfo($"[FileWatcher] Sprite file event: {e.ChangeType} — {e.FullPath}");
        string relativePath = Path.GetRelativePath(SpriteLoader.LoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);

        if (pathParts[^2] == "T2D" || (pathParts.Length >= 3 && pathParts[^3] == "T2D"))
        {
            string spriteName = Path.GetFileNameWithoutExtension(pathParts[^1]);
            string atlasName = (pathParts.Length >= 3 && pathParts[^3] == "T2D") ? pathParts[^2] : null;
            Plugin.Logger.LogInfo($"[FileWatcher] → T2D sprite change detected: atlas='{atlasName}', sprite='{spriteName}', setting ReloadT2DSprites=true");
            // Cache invalidation deferred to ReloadSpritesInScene on the main thread
            // to avoid dictionary corruption from concurrent access.
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

    private void OnAnchorChanged(object sender, FileSystemEventArgs e)
    {
        Plugin.Logger.LogInfo($"[FileWatcher] Anchor file event: {e.ChangeType} — {e.FullPath}");
        string relativePath = Path.GetRelativePath(SpriteLoader.LoadPath, e.FullPath);
        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        // Expected: {collName}/{matName}/anchors.txt  (3 parts)
        if (pathParts.Length < 3) return;
        string collectionName = pathParts[^3];
        string matName        = pathParts[^2];
        SpriteLoader.MarkReloadAtlas(collectionName, matName);
        Plugin.Logger.LogInfo($"[FileWatcher] → Anchor change: collection={collectionName}, mat={matName}, setting ReloadSprites=true");
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
            Plugin.Logger.LogInfo($"[FileWatcher] → T2D spritesheet change: '{cleanTexName}', setting ReloadT2DSprites=true");
            // Cache invalidation deferred to ReloadSpritesInScene on the main thread
            // to avoid dictionary corruption from concurrent access.
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