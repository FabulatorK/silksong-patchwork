using System.Collections.Generic;

namespace Patchwork.Util;

public static class T2DUtil
{
    private static readonly Dictionary<string, string> _cleanNameCache = new();

    public static bool IsT2DTexture(string textureName)
        => textureName.Contains("-BC7-") || textureName.Contains("DXT5|BC3-");

    /// <summary>
    /// Builds a composite key from atlas clean name and sprite name.
    /// Prevents collisions when different atlases contain sprites with the same name
    /// (e.g. "Moss Grub" in both journal_enemy_icons and journal_enemy_images).
    /// </summary>
    public static string SpriteKey(string cleanTexName, string spriteName)
        => $"{cleanTexName}/{spriteName}";

    public static string SanitizeForFilesystem(string textureName)
        => textureName
            .Replace("|", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(":", "_");

    public static string CleanTextureName(string textureName)
    {
        if (_cleanNameCache.TryGetValue(textureName, out var cached))
            return cached;
        string result = CleanTextureNameUncached(textureName);
        _cleanNameCache[textureName] = result;
        return result;
    }

    public static void ClearCleanNameCache() => _cleanNameCache.Clear();

    private static string CleanTextureNameUncached(string textureName)
    {
        string delimiter = null;
        if (textureName.Contains("-BC7-"))
            delimiter = "-BC7-";
        else if (textureName.Contains("DXT5|BC3-"))
            delimiter = "DXT5|BC3-";
        else if (textureName.Contains("DXT5_BC3-"))
            delimiter = "DXT5_BC3-";

        if (delimiter == null)
            return textureName;

        string afterDelimiter = textureName.Split([delimiter], System.StringSplitOptions.None)[1];
        // Strip trailing segment (hash/id) — everything after the last '-'
        int lastDash = afterDelimiter.LastIndexOf('-');
        return lastDash > 0 ? afterDelimiter.Substring(0, lastDash) : afterDelimiter;
    }
}
